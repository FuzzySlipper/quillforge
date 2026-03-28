using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Providers.Adapters;

/// <summary>
/// ICompletionService for reasoning-enabled OpenAI-compatible providers (Kimi, DeepSeek, QwQ)
/// that require reasoning_content to be preserved and replayed during tool loop round-trips.
///
/// Bypasses Microsoft.Extensions.AI IChatClient and the OpenAI SDK's typed serialization,
/// constructing raw JSON requests via HttpClient. This is a provider-specific adapter,
/// not the default path — used only when IsReasoningModel returns true in ProviderFactory.
///
/// Architecture note: provider-specific adapters are first-class, not exceptions.
/// See Task 31 investigation notes for full rationale.
/// </summary>
public sealed class ReasoningCompletionService : ICompletionService
{
    private readonly HttpClient _http;
    private readonly string _endpoint;
    private readonly string _model;
    private readonly ILogger<ReasoningCompletionService> _logger;

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public ReasoningCompletionService(
        HttpClient http,
        string baseUrl,
        string apiKey,
        string model,
        ILogger<ReasoningCompletionService> logger)
    {
        _http = http;
        _endpoint = baseUrl.TrimEnd('/') + "/chat/completions";
        _model = model;
        _logger = logger;

        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
    {
        var requestBody = BuildRequestJson(request);
        _logger.LogDebug("ReasoningCompletionService: sending request to {Endpoint}, {Length} chars",
            _endpoint, requestBody.Length);

        var content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        var response = await _http.PostAsync(_endpoint, content, ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("ReasoningCompletionService: API error {Status}: {Body}",
                response.StatusCode, responseBody[..Math.Min(500, responseBody.Length)]);
            throw new InvalidOperationException($"API error {response.StatusCode}: {responseBody}");
        }

        return ParseResponse(responseBody);
    }

    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        CompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var requestJson = BuildRequestJson(request, stream: true);

        var httpRequest = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(requestJson, Encoding.UTF8, "application/json"),
        };

        var response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogError("ReasoningCompletionService stream error: {Status}", response.StatusCode);
            yield return new DoneEvent("error", new TokenUsage(0, 0));
            yield break;
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        int inputTokens = 0, outputTokens = 0;
        string? finishReason = null;

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;
            var choices = root.GetProperty("choices");
            if (choices.GetArrayLength() == 0) continue;

            var choice = choices[0];
            var delta = choice.GetProperty("delta");

            // Text content
            if (delta.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
            {
                var text = contentEl.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    yield return new TextDeltaEvent(text);
                }
            }

            // Reasoning content (for UI display)
            if (delta.TryGetProperty("reasoning_content", out var reasoningEl) && reasoningEl.ValueKind == JsonValueKind.String)
            {
                var reasoning = reasoningEl.GetString();
                if (!string.IsNullOrEmpty(reasoning))
                {
                    yield return new ReasoningDeltaEvent(reasoning);
                }
            }

            // Finish reason
            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
            {
                finishReason = fr.GetString() switch
                {
                    "stop" => "end_turn",
                    "length" => "max_tokens",
                    "tool_calls" => "tool_use",
                    var other => other,
                };
            }

            // Usage
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt)) inputTokens = pt.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var ct2)) outputTokens = ct2.GetInt32();
            }
        }

        yield return new DoneEvent(finishReason ?? "end_turn", new TokenUsage(inputTokens, outputTokens));
    }

    private string BuildRequestJson(CompletionRequest request, bool stream = false)
    {
        var root = new JsonObject
        {
            ["model"] = request.Model == "default" ? _model : request.Model,
            ["max_tokens"] = request.MaxTokens,
            ["stream"] = stream,
        };

        if (request.Temperature is not null)
        {
            root["temperature"] = (decimal)request.Temperature.Value;
        }

        // Build messages array
        var messages = new JsonArray();

        if (!string.IsNullOrEmpty(request.SystemPrompt))
        {
            messages.Add(new JsonObject
            {
                ["role"] = "system",
                ["content"] = request.SystemPrompt,
            });
        }

        foreach (var msg in request.Messages)
        {
            // Tool result messages may contain multiple results — expand to separate messages
            var toolResults = msg.Content.Blocks.OfType<ToolResultBlock>().ToList();
            if (toolResults.Count > 1)
            {
                foreach (var tr in toolResults)
                {
                    messages.Add(new JsonObject
                    {
                        ["role"] = "tool",
                        ["tool_call_id"] = tr.ToolUseId,
                        ["content"] = tr.Content,
                    });
                }
            }
            else
            {
                var msgObj = BuildMessageJson(msg);
                messages.Add(msgObj);
            }
        }

        root["messages"] = messages;

        // Build tools array
        if (request.Tools is { Count: > 0 })
        {
            var tools = new JsonArray();
            foreach (var tool in request.Tools)
            {
                tools.Add(new JsonObject
                {
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tool.Name,
                        ["description"] = tool.Description,
                        ["parameters"] = JsonNode.Parse(tool.InputSchema.GetRawText()),
                    },
                });
            }
            root["tools"] = tools;
        }

        if (stream)
        {
            root["stream_options"] = new JsonObject { ["include_usage"] = true };
        }

        return root.ToJsonString();
    }

    private static JsonObject BuildMessageJson(CompletionMessage msg)
    {
        // If we have a raw JSON representation (from a previous response), use it directly.
        // This preserves reasoning_content and any other provider-specific fields.
        if (msg.RawProviderMessage is JsonObject rawObj)
        {
            return rawObj.Deserialize<JsonObject>()!; // Deep copy
        }

        var role = msg.Role.ToLowerInvariant();
        var msgObj = new JsonObject { ["role"] = role };

        var toolUseBlocks = msg.Content.Blocks.OfType<ToolUseBlock>().ToList();
        var toolResultBlocks = msg.Content.Blocks.OfType<ToolResultBlock>().ToList();

        if (toolResultBlocks.Count > 0)
        {
            // Tool result message
            msgObj["role"] = "tool";
            msgObj["tool_call_id"] = toolResultBlocks[0].ToolUseId;
            msgObj["content"] = toolResultBlocks[0].Content;
        }
        else if (toolUseBlocks.Count > 0)
        {
            // Assistant message with tool calls
            var text = msg.Content.GetText();
            msgObj["content"] = string.IsNullOrEmpty(text) ? null : (JsonNode)text;

            var toolCalls = new JsonArray();
            foreach (var tc in toolUseBlocks)
            {
                toolCalls.Add(new JsonObject
                {
                    ["id"] = tc.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = tc.Name,
                        ["arguments"] = tc.Input.GetRawText(),
                    },
                });
            }
            msgObj["tool_calls"] = toolCalls;

            // Inject empty reasoning_content for reasoning providers
            msgObj["reasoning_content"] = "";
        }
        else
        {
            // Regular text message
            msgObj["content"] = msg.Content.GetText();
        }

        return msgObj;
    }

    private CompletionResponse ParseResponse(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        var choices = root.GetProperty("choices");
        if (choices.GetArrayLength() == 0)
        {
            return new CompletionResponse
            {
                Content = new MessageContent(""),
                StopReason = "end_turn",
                Usage = new TokenUsage(0, 0),
            };
        }

        var choice = choices[0];
        var message = choice.GetProperty("message");
        var finishReason = choice.TryGetProperty("finish_reason", out var fr)
            ? fr.GetString() switch
            {
                "stop" => "end_turn",
                "length" => "max_tokens",
                "tool_calls" => "tool_use",
                var other => other ?? "end_turn",
            }
            : "end_turn";

        // Extract content blocks
        var contentBlocks = new List<ContentBlock>();
        string? reasoning = null;

        // Text content
        if (message.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
        {
            var text = contentEl.GetString();
            if (!string.IsNullOrEmpty(text))
            {
                contentBlocks.Add(new TextBlock(text));
            }
        }

        // Reasoning content (for UI display)
        if (message.TryGetProperty("reasoning_content", out var rcEl) && rcEl.ValueKind == JsonValueKind.String)
        {
            reasoning = rcEl.GetString();
        }

        // Tool calls
        if (message.TryGetProperty("tool_calls", out var toolCallsEl))
        {
            foreach (var tc in toolCallsEl.EnumerateArray())
            {
                var id = tc.GetProperty("id").GetString()!;
                var fn = tc.GetProperty("function");
                var name = fn.GetProperty("name").GetString()!;
                var argsStr = fn.GetProperty("arguments").GetString() ?? "{}";
                var args = JsonDocument.Parse(argsStr).RootElement.Clone();
                contentBlocks.Add(new ToolUseBlock(id, name, args));
            }
        }

        if (contentBlocks.Count == 0)
        {
            contentBlocks.Add(new TextBlock(""));
        }

        // Usage
        var inputTokens = 0;
        var outputTokens = 0;
        if (root.TryGetProperty("usage", out var usage))
        {
            if (usage.TryGetProperty("prompt_tokens", out var pt)) inputTokens = pt.GetInt32();
            if (usage.TryGetProperty("completion_tokens", out var ct2)) outputTokens = ct2.GetInt32();
        }

        // Preserve the raw message JSON for replay in tool loops
        var rawMessage = JsonNode.Parse(message.GetRawText())?.AsObject();

        return new CompletionResponse
        {
            Content = new MessageContent(contentBlocks),
            StopReason = finishReason,
            Usage = new TokenUsage(inputTokens, outputTokens),
            Reasoning = reasoning,
            RawProviderMessage = rawMessage,
        };
    }
}
