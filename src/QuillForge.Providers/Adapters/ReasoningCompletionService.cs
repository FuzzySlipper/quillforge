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
            throw new HttpRequestException(
                $"{response.StatusCode}: {responseBody[..Math.Min(500, responseBody.Length)]}",
                inner: null,
                statusCode: response.StatusCode);
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
            _logger.LogError("ReasoningCompletionService stream error: {Status}: {Body}",
                response.StatusCode, errorBody[..Math.Min(500, errorBody.Length)]);
            throw new HttpRequestException(
                $"{response.StatusCode}: {errorBody[..Math.Min(500, errorBody.Length)]}",
                inner: null,
                statusCode: response.StatusCode);
        }

        using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        int inputTokens = 0, outputTokens = 0;
        StopReason? finishReason = null;
        var textAccumulator = new StringBuilder();
        var reasoningAccumulator = new StringBuilder();
        var toolCallIds = new Dictionary<int, string>();
        var toolCallNames = new Dictionary<int, string>();
        var toolCallArgs = new Dictionary<int, StringBuilder>();

        string? line;
        while ((line = await reader.ReadLineAsync(ct)) is not null)
        {
            ct.ThrowIfCancellationRequested();
            if (!line.StartsWith("data: ")) continue;

            var data = line["data: ".Length..];
            if (data == "[DONE]") break;

            using var doc = JsonDocument.Parse(data);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
                continue;

            var choice = choices[0];
            if (!choice.TryGetProperty("delta", out var delta))
                continue;

            // Text content
            if (delta.TryGetProperty("content", out var contentEl) && contentEl.ValueKind == JsonValueKind.String)
            {
                var text = contentEl.GetString();
                if (!string.IsNullOrEmpty(text))
                {
                    textAccumulator.Append(text);
                    yield return new TextDeltaEvent(text);
                }
            }

            // Reasoning content (for UI display)
            if (delta.TryGetProperty("reasoning_content", out var reasoningEl) && reasoningEl.ValueKind == JsonValueKind.String)
            {
                var reasoning = reasoningEl.GetString();
                if (!string.IsNullOrEmpty(reasoning))
                {
                    reasoningAccumulator.Append(reasoning);
                    yield return new ReasoningDeltaEvent(reasoning);
                }
            }

            // Tool call deltas (accumulated incrementally by index)
            if (delta.TryGetProperty("tool_calls", out var toolCallsEl))
            {
                foreach (var tc in toolCallsEl.EnumerateArray())
                {
                    if (!tc.TryGetProperty("index", out var indexEl) || !indexEl.TryGetInt32(out var index))
                    {
                        _logger.LogWarning("Skipping streamed tool call delta: missing or non-integer 'index'");
                        continue;
                    }

                    if (tc.TryGetProperty("id", out var idEl)
                        && idEl.ValueKind == JsonValueKind.String
                        && idEl.GetString() is { } idStr)
                    {
                        toolCallIds[index] = idStr;
                    }

                    if (tc.TryGetProperty("function", out var fnEl))
                    {
                        if (fnEl.TryGetProperty("name", out var nameEl)
                            && nameEl.ValueKind == JsonValueKind.String
                            && nameEl.GetString() is { } nameStr)
                        {
                            toolCallNames[index] = nameStr;
                        }

                        if (fnEl.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.String)
                        {
                            if (!toolCallArgs.TryGetValue(index, out var sb))
                            {
                                sb = new StringBuilder();
                                toolCallArgs[index] = sb;
                            }
                            sb.Append(argsEl.GetString());
                        }
                    }
                }
            }

            // Finish reason
            if (choice.TryGetProperty("finish_reason", out var fr) && fr.ValueKind == JsonValueKind.String)
            {
                finishReason = StopReasonExtensions.ParseStopReason(fr.GetString());
            }

            // Usage
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var pt) && pt.TryGetInt32(out var ptVal))
                    inputTokens = ptVal;
                if (usage.TryGetProperty("completion_tokens", out var ct2) && ct2.TryGetInt32(out var ctVal))
                    outputTokens = ctVal;
            }
        }

        // Yield accumulated tool calls
        foreach (var index in toolCallIds.Keys.OrderBy(k => k))
        {
            if (toolCallIds.TryGetValue(index, out var id) && toolCallNames.TryGetValue(index, out var name))
            {
                if (!toolCallArgs.TryGetValue(index, out var sb) || sb.Length == 0)
                {
                    var error = $"Provider emitted tool call '{name}' without any arguments payload.";
                    _logger.LogWarning(
                        "Incomplete streamed tool call for {Name} (id={Id}, index={Index}): no argument chunks were captured",
                        name,
                        id,
                        index);
                    yield return new DiagnosticEvent(DiagnosticCategory.Tool, error, DiagnosticLevel.Error);
                    yield return new ToolCallDeltaReceivedEvent(name, id, CreateEmptyObject(), error);
                    continue;
                }

                var argsJson = sb.ToString();
                if (TryParseToolArguments(argsJson, out var parsedArgs))
                {
                    yield return new ToolCallDeltaReceivedEvent(name, id, parsedArgs);
                }

                else
                {
                    var error = $"Provider emitted malformed JSON arguments for tool '{name}'.";
                    _logger.LogWarning(
                        "Failed to parse streamed tool call arguments for {Name} (id={Id}, index={Index}): {Json}",
                        name,
                        id,
                        index,
                        argsJson);
                    yield return new DiagnosticEvent(DiagnosticCategory.Tool, error, DiagnosticLevel.Error);
                    yield return new ToolCallDeltaReceivedEvent(name, id, CreateEmptyObject(), error);
                }
            }
        }

        // Build a typed replay envelope for lossless round-tripping of reasoning content.
        ProviderReplayEnvelope? providerReplay = null;
        if (toolCallIds.Count > 0 || reasoningAccumulator.Length > 0)
        {
            var fullText = textAccumulator.Length > 0 ? textAccumulator.ToString() : null;
            var replayToolCalls = new List<ReasoningReplayToolCall>();
            if (toolCallIds.Count > 0)
            {
                foreach (var index in toolCallIds.Keys.OrderBy(k => k))
                {
                    replayToolCalls.Add(new ReasoningReplayToolCall(
                        toolCallIds[index],
                        toolCallNames.GetValueOrDefault(index, ""),
                        toolCallArgs.TryGetValue(index, out var argSb) ? argSb.ToString() : "{}"));
                }
            }

            providerReplay = new ReasoningReplayEnvelope(
                fullText,
                reasoningAccumulator.Length > 0 ? reasoningAccumulator.ToString() : null,
                replayToolCalls);
        }

        yield return new DoneEvent(finishReason ?? StopReason.EndTurn, new TokenUsage(inputTokens, outputTokens))
        {
            ProviderReplay = providerReplay,
        };
    }

    private static JsonElement CreateEmptyObject()
    {
        return JsonSerializer.SerializeToElement(new Dictionary<string, object?>());
    }

    private static bool TryParseToolArguments(string json, out JsonElement parsed)
    {
        try
        {
            parsed = JsonDocument.Parse(json).RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            parsed = default;
            return false;
        }
    }

    private static string GetRequiredToolArgumentsJson(JsonElement functionElement, string toolName)
    {
        if (!functionElement.TryGetProperty("arguments", out var argumentsElement)
            || argumentsElement.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Provider response omitted string arguments for tool '{toolName}'.");
        }

        var argumentsJson = argumentsElement.GetString();
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            throw new JsonException($"Provider response emitted empty arguments for tool '{toolName}'.");
        }

        return argumentsJson;
    }

    private string BuildRequestJson(CompletionRequest request, bool stream = false)
    {
        var effectiveModel = request.Model == "default" ? _model : request.Model;
        var addReasoningContent = ShouldAddReasoningContent(request.AdditionalOptions, effectiveModel);
        var stripEmptyRequired = ShouldStripEmptyRequired(request.AdditionalOptions, effectiveModel);

        var root = new JsonObject
        {
            ["model"] = effectiveModel,
            ["max_tokens"] = request.MaxTokens,
            ["stream"] = stream,
        };

        if (request.Temperature is not null)
        {
            root["temperature"] = (decimal)request.Temperature.Value;
        }
        if (request.TopP is not null)
        {
            root["top_p"] = (decimal)request.TopP.Value;
        }
        if (request.TopK is not null)
        {
            root["top_k"] = request.TopK.Value;
        }
        if (request.FrequencyPenalty is not null)
        {
            root["frequency_penalty"] = (decimal)request.FrequencyPenalty.Value;
        }
        if (request.PresencePenalty is not null)
        {
            root["presence_penalty"] = (decimal)request.PresencePenalty.Value;
        }
        if (request.RepetitionPenalty is not null)
        {
            root["repetition_penalty"] = (decimal)request.RepetitionPenalty.Value;
        }
        if (request.MinP is not null)
        {
            root["min_p"] = (decimal)request.MinP.Value;
        }
        if (request.Seed is not null)
        {
            root["seed"] = request.Seed.Value;
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
                var msgObj = BuildMessageJson(msg, addReasoningContent);
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
                        ["parameters"] = BuildToolParametersJson(tool.InputSchema, stripEmptyRequired),
                    },
                });
            }
            root["tools"] = tools;
        }

        if (stream)
        {
            root["stream_options"] = new JsonObject { ["include_usage"] = true };
        }

        ApplyAdditionalRequestOptions(root, request.AdditionalOptions);

        return root.ToJsonString();
    }

    private static JsonNode? BuildToolParametersJson(JsonElement schema, bool stripEmptyRequired)
    {
        var node = JsonNode.Parse(schema.GetRawText());
        if (stripEmptyRequired
            && node is JsonObject obj
            && obj["required"] is JsonArray required
            && required.Count == 0)
        {
            obj.Remove("required");
        }

        return node;
    }

    private static JsonObject BuildMessageJson(CompletionMessage msg, bool addReasoningContent)
    {
        // If we have a typed replay envelope from a previous response, rebuild the
        // provider-shaped JSON locally inside the adapter.
        if (msg.ProviderReplay is ReasoningReplayEnvelope reasoningReplay)
        {
            return BuildReasoningReplayJson(msg, reasoningReplay);
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
            if (addReasoningContent)
            {
                msgObj["reasoning_content"] = "";
            }
        }
        else
        {
            // Regular text message
            msgObj["content"] = msg.Content.GetText();
        }

        return msgObj;
    }

    private static void ApplyAdditionalRequestOptions(
        JsonObject root,
        IReadOnlyDictionary<string, JsonElement>? additionalOptions)
    {
        if (additionalOptions is null)
        {
            return;
        }

        TryApplyRawOption(root, additionalOptions, "top_p");
        TryApplyRawOption(root, additionalOptions, "top_k");
        TryApplyRawOption(root, additionalOptions, "frequency_penalty");
        TryApplyRawOption(root, additionalOptions, "presence_penalty");
        TryApplyRawOption(root, additionalOptions, "repetition_penalty");
        TryApplyRawOption(root, additionalOptions, "min_p");
        TryApplyRawOption(root, additionalOptions, "seed");
        TryApplyRawOption(root, additionalOptions, "num_ctx");

        if (TryGetOption(additionalOptions, "extra_body", out var extraBody)
            && extraBody.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in extraBody.EnumerateObject())
            {
                root[property.Name] = CloneJsonNode(property.Value);
            }
        }
    }

    private static void TryApplyRawOption(
        JsonObject root,
        IReadOnlyDictionary<string, JsonElement> additionalOptions,
        string key)
    {
        if (root.ContainsKey(key) || !TryGetOption(additionalOptions, key, out var value))
        {
            return;
        }

        root[key] = CloneJsonNode(value);
    }

    private static JsonNode? CloneJsonNode(JsonElement value)
    {
        return JsonNode.Parse(value.GetRawText());
    }

    private static bool ShouldAddReasoningContent(
        IReadOnlyDictionary<string, JsonElement>? additionalOptions,
        string model)
    {
        if (additionalOptions is null || !TryGetOption(additionalOptions, "reasoning_content", out var option))
        {
            return true;
        }

        if (option.ValueKind == JsonValueKind.True) return true;
        if (option.ValueKind == JsonValueKind.False) return false;
        if (option.ValueKind == JsonValueKind.String
            && string.Equals(option.GetString(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            return model.Contains("-reasoner", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static bool ShouldStripEmptyRequired(
        IReadOnlyDictionary<string, JsonElement>? additionalOptions,
        string model)
    {
        if (additionalOptions is null || !TryGetOption(additionalOptions, "strip_empty_required", out var option))
        {
            return false;
        }

        if (option.ValueKind == JsonValueKind.True) return true;
        if (option.ValueKind == JsonValueKind.False) return false;
        if (option.ValueKind == JsonValueKind.String
            && string.Equals(option.GetString(), "auto", StringComparison.OrdinalIgnoreCase))
        {
            return model.Contains("deepseek", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static bool TryGetOption(
        IReadOnlyDictionary<string, JsonElement> additionalOptions,
        string key,
        out JsonElement value)
    {
        return additionalOptions.TryGetValue(key, out value);
    }

    private CompletionResponse ParseResponse(string responseBody)
    {
        using var doc = JsonDocument.Parse(responseBody);
        var root = doc.RootElement;

        if (!root.TryGetProperty("choices", out var choices) || choices.GetArrayLength() == 0)
        {
            _logger.LogWarning("Provider response missing 'choices' or returned empty choices array");
            return new CompletionResponse
            {
                Content = new MessageContent(""),
                StopReason = StopReason.EndTurn,
                Usage = new TokenUsage(0, 0),
            };
        }

        var choice = choices[0];
        if (!choice.TryGetProperty("message", out var message))
        {
            _logger.LogWarning("Provider response choice missing 'message' property");
            return new CompletionResponse
            {
                Content = new MessageContent(""),
                StopReason = StopReason.EndTurn,
                Usage = new TokenUsage(0, 0),
            };
        }

        var finishReason = StopReasonExtensions.ParseStopReason(
            choice.TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null);

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
                if (!tc.TryGetProperty("id", out var tcIdEl) || tcIdEl.GetString() is not { } tcId)
                {
                    _logger.LogWarning("Skipping tool call in response: missing 'id'");
                    continue;
                }
                if (!tc.TryGetProperty("function", out var fn))
                {
                    _logger.LogWarning("Skipping tool call '{Id}' in response: missing 'function'", tcId);
                    continue;
                }
                if (!fn.TryGetProperty("name", out var fnNameEl) || fnNameEl.GetString() is not { } tcName)
                {
                    _logger.LogWarning("Skipping tool call '{Id}' in response: missing 'function.name'", tcId);
                    continue;
                }
                var argsStr = GetRequiredToolArgumentsJson(fn, tcName);
                var args = JsonDocument.Parse(argsStr).RootElement.Clone();
                contentBlocks.Add(new ToolUseBlock(tcId, tcName, new ToolInput(args)));
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
            if (usage.TryGetProperty("prompt_tokens", out var pt) && pt.TryGetInt32(out var ptVal))
                inputTokens = ptVal;
            if (usage.TryGetProperty("completion_tokens", out var ct2) && ct2.TryGetInt32(out var ctVal))
                outputTokens = ctVal;
        }

        ProviderReplayEnvelope? providerReplay = null;
        JsonElement replayToolCallsElement = default;
        var hasReplayToolCalls = message.TryGetProperty("tool_calls", out replayToolCallsElement);
        if (reasoning is not null || hasReplayToolCalls)
        {
            var replayToolCalls = new List<ReasoningReplayToolCall>();
            if (hasReplayToolCalls && replayToolCallsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var tc in replayToolCallsElement.EnumerateArray())
                {
                    if (!tc.TryGetProperty("id", out var rpIdEl) || rpIdEl.GetString() is not { } rpId)
                    {
                        _logger.LogWarning("Skipping replay tool call: missing 'id'");
                        continue;
                    }
                    if (!tc.TryGetProperty("function", out var rpFn))
                    {
                        _logger.LogWarning("Skipping replay tool call '{Id}': missing 'function'", rpId);
                        continue;
                    }
                    if (!rpFn.TryGetProperty("name", out var rpNameEl) || rpNameEl.GetString() is not { } rpName)
                    {
                        _logger.LogWarning("Skipping replay tool call '{Id}': missing 'function.name'", rpId);
                        continue;
                    }
                    var argsJson = GetRequiredToolArgumentsJson(rpFn, rpName);
                    replayToolCalls.Add(new ReasoningReplayToolCall(rpId, rpName, argsJson));
                }
            }

            providerReplay = new ReasoningReplayEnvelope(
                message.TryGetProperty("content", out var replayContentEl) && replayContentEl.ValueKind == JsonValueKind.String
                    ? replayContentEl.GetString()
                    : null,
                reasoning,
                replayToolCalls);
        }

        return new CompletionResponse
        {
            Content = new MessageContent(contentBlocks),
            StopReason = finishReason,
            Usage = new TokenUsage(inputTokens, outputTokens),
            Reasoning = reasoning,
            ProviderReplay = providerReplay,
        };
    }

    private static JsonObject BuildReasoningReplayJson(
        CompletionMessage message,
        ReasoningReplayEnvelope replay)
    {
        var msgObj = new JsonObject
        {
            ["role"] = message.Role.ToLowerInvariant(),
            ["content"] = replay.Content is not null ? (JsonNode)replay.Content : null,
        };

        if (replay.ReasoningContent is not null)
        {
            msgObj["reasoning_content"] = replay.ReasoningContent;
        }

        if (replay.ToolCalls.Count > 0)
        {
            var toolCalls = new JsonArray();
            foreach (var toolCall in replay.ToolCalls)
            {
                toolCalls.Add(new JsonObject
                {
                    ["id"] = toolCall.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = toolCall.Name,
                        ["arguments"] = toolCall.ArgumentsJson,
                    },
                });
            }

            msgObj["tool_calls"] = toolCalls;
        }

        return msgObj;
    }
}
