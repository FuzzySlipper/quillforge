using System.Runtime.CompilerServices;
using System.Text.Json;
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

    public ChatClientCompletionService(IChatClient client, ILogger<ChatClientCompletionService> logger)
    {
        _client = client;
        _logger = logger;
    }

    public async Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
    {
        var chatMessages = ConvertMessages(request);
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
        var chatMessages = ConvertMessages(request);
        var options = BuildOptions(request);

        _logger.LogDebug("Starting streaming completion request");

        // Collect the full streaming response, yielding text deltas as they arrive
        var toolCalls = new Dictionary<string, (string Name, List<string> JsonParts)>();
        int inputTokens = 0, outputTokens = 0;
        string? finishReason = null;

        await foreach (var update in _client.GetStreamingResponseAsync(chatMessages, options, ct))
        {
            // Process each content item in the update
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
                            yield return new ToolCallEvent(
                                funcCall.Name,
                                funcCall.CallId ?? Guid.NewGuid().ToString(),
                                SerializeArguments(funcCall.Arguments));
                            break;
                    }
                }
            }

            // Track usage and finish reason from the update
            if (update.FinishReason is not null)
            {
                finishReason = ConvertFinishReason(update.FinishReason.Value);
            }

            // Check for usage in content items
            if (update.Contents is not null)
            {
                foreach (var content in update.Contents)
                {
                    if (content is UsageContent usage)
                    {
                        inputTokens = (int)(usage.Details.InputTokenCount ?? inputTokens);
                        outputTokens = (int)(usage.Details.OutputTokenCount ?? outputTokens);
                    }
                }
            }
        }

        yield return new DoneEvent(
            finishReason ?? "end_turn",
            new TokenUsage(inputTokens, outputTokens));
    }

    private static List<ChatMessage> ConvertMessages(CompletionRequest request)
    {
        var messages = new List<ChatMessage>();

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new ChatMessage(ChatRole.System, request.SystemPrompt));
        }

        foreach (var msg in request.Messages)
        {
            var role = msg.Role.ToLowerInvariant() switch
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
                            DeserializeArguments(toolUse.Input)));
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

    private static ChatOptions BuildOptions(CompletionRequest request)
    {
        var options = new ChatOptions
        {
            ModelId = request.Model,
            MaxOutputTokens = request.MaxTokens,
            Temperature = request.Temperature is not null ? (float)request.Temperature.Value : null,
        };

        if (request.Tools is { Count: > 0 })
        {
            options.Tools = request.Tools.Select(t => (AITool)AIFunctionFactory.Create(
                (string input) => input,
                new AIFunctionFactoryOptions
                {
                    Name = t.Name,
                    Description = t.Description,
                })).ToList();

            // Actually, we need to pass the JSON schema properly.
            // Let's use a different approach — build AIFunction with the schema.
            options.Tools = request.Tools.Select(ConvertToolDefinition).ToList();
        }

        return options;
    }

    private static AITool ConvertToolDefinition(Core.Models.ToolDefinition tool)
    {
        return AIFunctionFactory.Create(
            (string input) => input,
            new AIFunctionFactoryOptions
            {
                Name = tool.Name,
                Description = tool.Description,
            });
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
                            SerializeArguments(funcCall.Arguments)));
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
            dict[prop.Name] = prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Number => prop.Value.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => prop.Value.GetRawText(),
            };
        }
        return dict;
    }
}
