using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Providers.Adapters;

/// <summary>
/// Adapts Microsoft.Extensions.AI's IChatClient to our ICompletionService interface.
/// This is the bridge between the provider-agnostic Core and the LLM SDK world.
/// </summary>
public sealed class ChatClientCompletionService : ICompletionService
{
    private readonly IChatClient _client;
    private readonly ILogger<ChatClientCompletionService> _logger;
    private readonly bool _isAnthropic;

    public ChatClientCompletionService(IChatClient client, ILogger<ChatClientCompletionService> logger)
    {
        _client = client;
        _logger = logger;
        _isAnthropic = client.GetService<IAnthropicClient>() is not null;
    }

    public async Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
    {
        var chatMessages = ConvertMessages(request, _isAnthropic);
        var options = BuildOptions(request);

        _logger.LogDebug(
            "Sending completion request: model={Model}, messages={Count}, tools={ToolCount}",
            request.Model, request.Messages.Count, request.Tools?.Count ?? 0);

        var response = await _client.GetResponseAsync(chatMessages, options, ct);

        _logger.LogDebug(
            "Completion response: finishReason={FinishReason}, usage={InputTokens}in/{OutputTokens}out",
            response.FinishReason, response.Usage?.InputTokenCount, response.Usage?.OutputTokenCount);

        return ConvertResponse(response);
    }

    public async IAsyncEnumerable<Core.Models.StreamEvent> StreamAsync(
        CompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var chatMessages = ConvertMessages(request, _isAnthropic);
        var options = BuildOptions(request);

        _logger.LogDebug("Starting streaming completion request");

        // Accumulate partial tool calls across streaming updates.
        // Some providers emit FunctionCallContent incrementally (name first, then argument chunks).
        var pendingToolCalls = new Dictionary<string, (string Name, List<string> JsonParts)>();
        var emittedToolCallIds = new HashSet<string>();
        int inputTokens = 0, outputTokens = 0;
        string? finishReason = null;

        await foreach (var update in _client.GetStreamingResponseAsync(chatMessages, options, ct))
        {
            if (update.Contents is not null)
            {
                foreach (var content in update.Contents)
                {
                    switch (content)
                    {
                        case TextContent text when !string.IsNullOrEmpty(text.Text):
                            yield return new TextDeltaEvent(text.Text);
                            break;

                        case FunctionCallContent funcCall:
                        {
                            var callId = funcCall.CallId ?? Guid.NewGuid().ToString();
                            if (funcCall.Arguments is { Count: > 0 })
                            {
                                // Complete tool call — emit immediately
                                emittedToolCallIds.Add(callId);
                                yield return new ToolCallDeltaReceivedEvent(
                                    funcCall.Name,
                                    callId,
                                    SerializeArguments(funcCall.Arguments));
                            }
                            else if (!string.IsNullOrEmpty(funcCall.Name))
                            {
                                // Partial: name arrived but no arguments yet — accumulate
                                if (!pendingToolCalls.ContainsKey(callId))
                                {
                                    pendingToolCalls[callId] = (funcCall.Name, []);
                                }
                            }
                            break;
                        }

                        case UsageContent usage:
                            inputTokens = (int)(usage.Details.InputTokenCount ?? inputTokens);
                            outputTokens = (int)(usage.Details.OutputTokenCount ?? outputTokens);
                            break;

                        default:
                            _logger.LogDebug(
                                "Streaming: unhandled content type {ContentType}: {Content}",
                                content.GetType().Name, content);
                            break;
                    }
                }
            }

            if (update.FinishReason is not null)
            {
                finishReason = ConvertFinishReason(update.FinishReason.Value);
            }
        }

        // Emit any accumulated tool calls that weren't emitted during streaming
        foreach (var (callId, (name, jsonParts)) in pendingToolCalls)
        {
            if (emittedToolCallIds.Contains(callId)) continue;

            _logger.LogDebug("Emitting accumulated tool call {Name} (id={CallId})", name, callId);
            var argsJson = jsonParts.Count > 0 ? string.Concat(jsonParts) : "{}";
            JsonElement args;
            try
            {
                args = JsonDocument.Parse(argsJson).RootElement.Clone();
            }
            catch (JsonException)
            {
                _logger.LogWarning("Failed to parse accumulated tool call arguments for {Name}: {Json}", name, argsJson);
                args = JsonDocument.Parse("{}").RootElement.Clone();
            }
            yield return new ToolCallDeltaReceivedEvent(name, callId, args);
        }

        if (finishReason == "tool_use" && emittedToolCallIds.Count == 0 && pendingToolCalls.Count == 0)
        {
            _logger.LogWarning("Stream ended with tool_use finish reason but no tool calls were captured");
        }

        yield return new DoneEvent(
            finishReason ?? "end_turn",
            new TokenUsage(inputTokens, outputTokens));
    }

    private static List<ChatMessage> ConvertMessages(CompletionRequest request, bool isAnthropic)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(BuildSystemMessage(request.SystemPrompt, request.CacheSystemPrompt && isAnthropic));
        }

        foreach (var msg in request.Messages)
        {
            // Detect tool result messages: if any blocks are ToolResultBlock, use Tool role
            var hasToolResults = msg.Content.Blocks.Any(b => b is Core.Models.ToolResultBlock);

            var role = hasToolResults
                ? ChatRole.Tool
                : msg.Role.ToLowerInvariant() switch
                {
                    "user" => ChatRole.User,
                    "assistant" => ChatRole.Assistant,
                    "system" => ChatRole.System,
                    _ => ChatRole.User,
                };

            var contents = new List<AIContent>();

            foreach (var block in msg.Content.Blocks)
            {
                switch (block)
                {
                    case Core.Models.TextBlock text:
                        contents.Add(new TextContent(text.Text));
                        break;
                    case Core.Models.ToolUseBlock toolUse:
                        contents.Add(new FunctionCallContent(toolUse.Id, toolUse.Name,
                            DeserializeArguments(toolUse.Input.ToJsonElement())));
                        break;
                    case Core.Models.ToolResultBlock toolResult:
                        contents.Add(new FunctionResultContent(toolResult.ToolUseId, toolResult.Content));
                        break;
                }
            }

            messages.Add(new ChatMessage(role, contents));
        }

        return messages;
    }

    /// <summary>
    /// Builds the system ChatMessage, optionally marking it for Anthropic prompt caching.
    /// When caching is enabled, the TextContent's RawRepresentation is set to an Anthropic
    /// TextBlockParam with cache_control: { type: "ephemeral" }. The Anthropic IChatClient
    /// adapter recognizes this raw representation and passes it directly to the API,
    /// preserving the cache_control annotation. Non-Anthropic clients ignore RawRepresentation.
    /// </summary>
    private static ChatMessage BuildSystemMessage(string systemPrompt, bool enableCaching)
    {
        var textContent = new TextContent(systemPrompt);

        if (enableCaching)
        {
            textContent.RawRepresentation = new TextBlockParam(systemPrompt)
            {
                CacheControl = new CacheControlEphemeral(),
            };
        }

        return new ChatMessage(ChatRole.System, [textContent]);
    }

    private static ChatOptions BuildOptions(CompletionRequest request)
    {
        var options = new ChatOptions
        {
            // Only override ModelId when a specific model is requested.
            // "default" means "use whatever model the IChatClient was created with".
            ModelId = string.Equals(request.Model, "default", StringComparison.OrdinalIgnoreCase) ? null : request.Model,
            MaxOutputTokens = request.MaxTokens,
            Temperature = request.Temperature is not null ? (float)request.Temperature.Value : null,
        };

        if (request.Tools is { Count: > 0 })
        {
            options.Tools = request.Tools.Select(ConvertToolDefinition).ToList();
        }

        return options;
    }

    private static AITool ConvertToolDefinition(Core.Models.ToolDefinition tool)
    {
        return new SchemaPreservingTool(tool.Name, tool.Description, tool.InputSchema);
    }

    /// <summary>
    /// Declaration-only AITool that preserves the full JSON schema from our ToolDefinition.
    /// Not invocable — actual tool execution goes through our ToolLoop.
    /// </summary>
    private sealed class SchemaPreservingTool : AIFunctionDeclaration
    {
        private readonly string _name;
        private readonly string _description;
        private readonly JsonElement _schema;

        public SchemaPreservingTool(string name, string description, JsonElement schema)
        {
            _name = name;
            _description = description;
            _schema = schema;
        }

        public override string Name => _name;
        public override string Description => _description;
        public override JsonElement JsonSchema => _schema;
    }

    private static CompletionResponse ConvertResponse(ChatResponse response)
    {
        var contentBlocks = new List<Core.Models.ContentBlock>();

        foreach (var msg in response.Messages)
        {
            foreach (var content in msg.Contents)
            {
                switch (content)
                {
                    case TextContent text:
                        if (!string.IsNullOrEmpty(text.Text))
                            contentBlocks.Add(new Core.Models.TextBlock(text.Text));
                        break;
                    case FunctionCallContent funcCall:
                        contentBlocks.Add(new Core.Models.ToolUseBlock(
                            funcCall.CallId ?? Guid.NewGuid().ToString(),
                            funcCall.Name,
                            new ToolInput(SerializeArguments(funcCall.Arguments))));
                        break;
                }
            }
        }

        if (contentBlocks.Count == 0)
        {
            contentBlocks.Add(new Core.Models.TextBlock(""));
        }

        var stopReason = response.FinishReason is not null
            ? ConvertFinishReason(response.FinishReason.Value)
            : "end_turn";

        return new CompletionResponse
        {
            Content = new MessageContent(contentBlocks),
            StopReason = stopReason,
            Usage = new TokenUsage(
                (int)(response.Usage?.InputTokenCount ?? 0),
                (int)(response.Usage?.OutputTokenCount ?? 0)),
        };
    }

    private static string ConvertFinishReason(ChatFinishReason reason)
    {
        if (reason == ChatFinishReason.Stop) return "end_turn";
        if (reason == ChatFinishReason.Length) return "max_tokens";
        if (reason == ChatFinishReason.ToolCalls) return "tool_use";
        if (reason == ChatFinishReason.ContentFilter) return "content_filter";
        return "end_turn";
    }

    private static JsonElement SerializeArguments(IDictionary<string, object?>? arguments)
    {
        if (arguments is null || arguments.Count == 0)
        {
            return JsonDocument.Parse("{}").RootElement.Clone();
        }
        var json = JsonSerializer.Serialize(arguments);
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    private static IDictionary<string, object?>? DeserializeArguments(JsonElement input)
    {
        if (input.ValueKind != JsonValueKind.Object)
            return null;

        var dict = new Dictionary<string, object?>();
        foreach (var prop in input.EnumerateObject())
        {
            dict[prop.Name] = ConvertJsonValue(prop.Value);
        }
        return dict;
    }

    private static object? ConvertJsonValue(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonValue).ToList(),
        JsonValueKind.Object => element.EnumerateObject()
            .ToDictionary(p => p.Name, p => ConvertJsonValue(p.Value)),
        _ => element.GetRawText(),
    };
}
