using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Diagnostics;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents;

/// <summary>
/// The single reusable engine for the call-LLM → check-tools → dispatch → repeat pattern.
/// All agents configure this; none reimplement it.
/// </summary>
public sealed class ToolLoop
{
    private readonly ICompletionService _completionService;
    private readonly ContinuationStrategy _continuationStrategy;
    private readonly ILogger<ToolLoop> _logger;
    private readonly ILlmDebugLogger? _debugLogger;
    private readonly bool _diagnosticsEnabled;
    private readonly int _toolTimeoutSeconds;

    public ToolLoop(
        ICompletionService completionService,
        ContinuationStrategy continuationStrategy,
        ILogger<ToolLoop> logger,
        AppConfig appConfig,
        ILlmDebugLogger? debugLogger = null)
    {
        _completionService = completionService;
        _continuationStrategy = continuationStrategy;
        _logger = logger;
        _debugLogger = debugLogger;
        _diagnosticsEnabled = appConfig.Diagnostics.LivePanel;
        _toolTimeoutSeconds = appConfig.Timeouts.ToolExecutionSeconds;
    }

    /// <summary>
    /// Runs the tool loop to completion, returning the final response.
    /// </summary>
    public async Task<AgentResponse> RunAsync(
        AgentConfig config,
        IReadOnlyList<IToolHandler> tools,
        List<CompletionMessage> messages,
        AgentContext context,
        CancellationToken ct = default)
    {
        var toolMap = BuildToolMap(tools);
        var toolDefs = tools.Select(t => t.Definition).ToList();
        var totalUsage = new TokenUsage(0, 0);
        var round = 0;

        _logger.LogInformation(
            "ToolLoop starting for session {SessionId}, model {Model}, {ToolCount} tools, max {MaxRounds} rounds",
            context.SessionId, config.Model, tools.Count, config.MaxToolRounds);

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var request = new CompletionRequest
            {
                Model = config.Model,
                MaxTokens = config.MaxTokens,
                SystemPrompt = config.SystemPrompt,
                Messages = messages,
                Tools = toolDefs.Count > 0 ? toolDefs : null,
                Temperature = config.Temperature,
                CacheSystemPrompt = config.CacheSystemPrompt,
            };

            _logger.LogDebug("ToolLoop round {Round}: calling completion service", round);

            _debugLogger?.LogRequest(
                agent: "ToolLoop",
                model: config.Model,
                maxTokens: config.MaxTokens,
                systemPreview: config.SystemPrompt ?? "",
                messagesCount: messages.Count,
                toolsCount: toolDefs.Count);

            CompletionResponse response;
            try
            {
                response = await _completionService.CompleteAsync(request, ct);
            }
            catch (Exception ex)
            {
                _debugLogger?.LogError("ToolLoop", config.Model, ex.Message);
                throw;
            }

            totalUsage = _continuationStrategy.AggregateUsage(totalUsage, response.Usage);

            _debugLogger?.LogResponse(
                agent: "ToolLoop",
                model: config.Model,
                stopReason: response.StopReason,
                contentPreview: response.Content.GetText(),
                inputTokens: response.Usage.InputTokens,
                outputTokens: response.Usage.OutputTokens);

            _logger.LogDebug(
                "ToolLoop round {Round}: stop_reason={StopReason}, usage={InputTokens}in/{OutputTokens}out",
                round, response.StopReason, response.Usage.InputTokens, response.Usage.OutputTokens);

            // Handle max_tokens with auto-continuation
            if (_continuationStrategy.ShouldContinue(response))
            {
                _logger.LogInformation("ToolLoop round {Round}: max_tokens hit, auto-continuing", round);

                messages.Add(new CompletionMessage("assistant", response.Content));
                var contMsg = _continuationStrategy.BuildContinuationMessage(response);
                messages.Add(contMsg);

                round++;
                if (round >= config.MaxToolRounds)
                {
                    _logger.LogWarning("ToolLoop hit max rounds ({MaxRounds}) during continuation", config.MaxToolRounds);
                    return BuildResponse(response.Content, "max_rounds", totalUsage, round);
                }
                continue;
            }

            // Check for tool calls
            var toolCalls = response.Content.GetToolCalls().ToList();
            if (toolCalls.Count == 0 || string.Equals(response.StopReason, "end_turn", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInformation(
                    "ToolLoop completed after {Rounds} rounds, stop_reason={StopReason}",
                    round, response.StopReason);
                return BuildResponse(response.Content, response.StopReason, totalUsage, round);
            }

            // Dispatch tool calls
            round++;
            if (round > config.MaxToolRounds)
            {
                _logger.LogWarning(
                    "ToolLoop hit max rounds ({MaxRounds}), returning last response",
                    config.MaxToolRounds);
                return BuildResponse(response.Content, "max_rounds", totalUsage, round);
            }

            // Append assistant message with tool_use blocks.
            // RawProviderMessage carries provider-specific data (e.g., reasoning_content JSON)
            // for adapters that need lossless round-tripping. Ignored by the default adapter.
            messages.Add(new CompletionMessage("assistant", response.Content)
            {
                RawProviderMessage = response.RawProviderMessage,
            });

            // Execute all tool calls and build results
            var resultBlocks = new List<ContentBlock>();
            foreach (var toolCall in toolCalls)
            {
                var result = await DispatchToolAsync(toolMap, toolCall, context, ct);
                resultBlocks.Add(new ToolResultBlock(
                    toolCall.Id,
                    result.Success ? result.Content : result.Error ?? "Unknown error",
                    isError: !result.Success));
            }

            // Append tool results as a user message
            messages.Add(new CompletionMessage("user", new MessageContent(resultBlocks)));

            _logger.LogDebug("ToolLoop round {Round}: dispatched {Count} tool calls", round, toolCalls.Count);
        }
    }

    /// <summary>
    /// Runs the tool loop with streaming. Yields events as they arrive.
    /// Tool dispatch rounds still use non-streaming completion for simplicity.
    /// The final round streams to the caller.
    /// </summary>
    public async IAsyncEnumerable<StreamEvent> RunStreamAsync(
        AgentConfig config,
        IReadOnlyList<IToolHandler> tools,
        List<CompletionMessage> messages,
        AgentContext context,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var toolMap = BuildToolMap(tools);
        var toolDefs = tools.Select(t => t.Definition).ToList();
        var round = 0;

        _logger.LogInformation(
            "ToolLoop (streaming) starting for session {SessionId}, model {Model}",
            context.SessionId, config.Model);

        if (_diagnosticsEnabled)
            yield return new DiagnosticEvent("stream",
                $"Starting stream: model={config.Model}, tools={toolDefs.Count}, maxRounds={config.MaxToolRounds}");

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            var request = new CompletionRequest
            {
                Model = config.Model,
                MaxTokens = config.MaxTokens,
                SystemPrompt = config.SystemPrompt,
                Messages = messages,
                Tools = toolDefs.Count > 0 ? toolDefs : null,
                Temperature = config.Temperature,
                CacheSystemPrompt = config.CacheSystemPrompt,
            };

            if (_diagnosticsEnabled)
                yield return new DiagnosticEvent("llm",
                    $"Round {round}: calling {config.Model} ({messages.Count} messages, {toolDefs.Count} tools)");

            _debugLogger?.LogRequest(
                agent: "ToolLoop.Stream",
                model: config.Model,
                maxTokens: config.MaxTokens,
                systemPreview: config.SystemPrompt ?? "",
                messagesCount: messages.Count,
                toolsCount: toolDefs.Count);

            var collectedText = new List<string>();
            var collectedToolCalls = new List<ToolCallEvent>();
            string? stopReason = null;
            TokenUsage? usage = null;

            await foreach (var evt in _completionService.StreamAsync(request, ct))
            {
                switch (evt)
                {
                    case TextDeltaEvent textDelta:
                        collectedText.Add(textDelta.Text);
                        yield return textDelta;
                        break;

                    case ToolCallEvent toolCall:
                        collectedToolCalls.Add(toolCall);
                        yield return toolCall;
                        break;

                    case DoneEvent done:
                        stopReason = done.StopReason;
                        usage = done.Usage;
                        break;

                    default:
                        yield return evt;
                        break;
                }
            }

            _debugLogger?.LogResponse(
                agent: "ToolLoop.Stream",
                model: config.Model,
                stopReason: stopReason,
                contentPreview: string.Join("", collectedText),
                inputTokens: usage?.InputTokens ?? 0,
                outputTokens: usage?.OutputTokens ?? 0);

            if (collectedToolCalls.Count == 0 &&
                string.Equals(stopReason, "tool_use", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "ToolLoop (streaming) received stop_reason=tool_use without streamed tool calls; retrying non-streaming recovery");

                if (_diagnosticsEnabled)
                {
                    yield return new DiagnosticEvent(
                        "warning",
                        "Stream ended with tool_use but no tool call payloads; retrying non-streaming recovery",
                        DiagnosticLevel.Warning);
                }

                CompletionResponse? recoveryResponse = null;
                Exception? recoveryException = null;
                try
                {
                    recoveryResponse = await _completionService.CompleteAsync(request, ct);
                }
                catch (Exception ex)
                {
                    recoveryException = ex;
                    _logger.LogError(ex,
                        "ToolLoop (streaming) recovery request failed after missing streamed tool calls");
                }

                if (recoveryException is not null || recoveryResponse is null)
                {
                    if (_diagnosticsEnabled)
                    {
                        yield return new DiagnosticEvent(
                            "warning",
                            "Tool recovery failed after an incomplete streamed tool call response",
                            DiagnosticLevel.Error);
                    }

                    yield return new DoneEvent("error", usage ?? new TokenUsage(0, 0));
                    yield break;
                }

                stopReason = recoveryResponse.StopReason;
                usage = recoveryResponse.Usage;
                collectedText.Clear();
                collectedToolCalls.Clear();

                foreach (var toolCall in recoveryResponse.Content.GetToolCalls())
                {
                    collectedToolCalls.Add(new ToolCallEvent(toolCall.Name, toolCall.Id, toolCall.Input));
                }

                var recoveredText = recoveryResponse.Content.GetText();
                if (!string.IsNullOrEmpty(recoveredText))
                {
                    collectedText.Add(recoveredText);
                }

                if (_diagnosticsEnabled)
                {
                    yield return new DiagnosticEvent(
                        "tool",
                        collectedToolCalls.Count > 0
                            ? $"Recovered {collectedToolCalls.Count} tool call(s) via non-streaming retry"
                            : "Non-streaming retry also returned no tool calls",
                        collectedToolCalls.Count > 0 ? DiagnosticLevel.Info : DiagnosticLevel.Warning);
                }
            }

            // If no tool calls, we're done
            if (collectedToolCalls.Count == 0 ||
                string.Equals(stopReason, "end_turn", StringComparison.OrdinalIgnoreCase))
            {
                if (_diagnosticsEnabled)
                {
                    if (collectedText.Count == 0)
                        yield return new DiagnosticEvent("warning",
                            "Stream completed with no text content", DiagnosticLevel.Warning);

                    yield return new DiagnosticEvent("stream",
                        $"Stream complete: stop={stopReason ?? "end_turn"}, tokens={usage?.InputTokens ?? 0}in/{usage?.OutputTokens ?? 0}out");
                }

                yield return new DoneEvent(stopReason ?? "end_turn", usage ?? new TokenUsage(0, 0));
                yield break;
            }

            // Tool dispatch round
            round++;
            if (round > config.MaxToolRounds)
            {
                _logger.LogWarning("ToolLoop (streaming) hit max rounds ({MaxRounds})", config.MaxToolRounds);
                if (_diagnosticsEnabled)
                    yield return new DiagnosticEvent("warning",
                        $"Hit max tool rounds ({config.MaxToolRounds})", DiagnosticLevel.Warning);
                yield return new DoneEvent("max_rounds", usage ?? new TokenUsage(0, 0));
                yield break;
            }

            // Build assistant message from collected content
            var assistantBlocks = new List<ContentBlock>();
            if (collectedText.Count > 0)
            {
                assistantBlocks.Add(new TextBlock(string.Join("", collectedText)));
            }
            foreach (var tc in collectedToolCalls)
            {
                assistantBlocks.Add(new ToolUseBlock(tc.ToolId, tc.ToolName, tc.Input));
            }
            messages.Add(new CompletionMessage("assistant", new MessageContent(assistantBlocks)));

            // Execute tools
            var resultBlocks = new List<ContentBlock>();
            foreach (var tc in collectedToolCalls)
            {
                if (_diagnosticsEnabled)
                    yield return new DiagnosticEvent("tool", $"Dispatching {tc.ToolName}");

                var fakeToolUse = new ToolUseBlock(tc.ToolId, tc.ToolName, tc.Input);
                var result = await DispatchToolAsync(toolMap, fakeToolUse, context, ct);

                if (_diagnosticsEnabled)
                    yield return new DiagnosticEvent("tool",
                        result.Success
                            ? $"{tc.ToolName} completed ({result.Content.Length} chars)"
                            : $"{tc.ToolName} failed: {result.Error}",
                        result.Success ? DiagnosticLevel.Info : DiagnosticLevel.Error);

                resultBlocks.Add(new ToolResultBlock(
                    tc.ToolId,
                    result.Success ? result.Content : result.Error ?? "Unknown error",
                    isError: !result.Success));
            }

            messages.Add(new CompletionMessage("user", new MessageContent(resultBlocks)));
            collectedText.Clear();
            collectedToolCalls.Clear();
        }
    }

    private async Task<ToolResult> DispatchToolAsync(
        Dictionary<string, IToolHandler> toolMap,
        ToolUseBlock toolCall,
        AgentContext context,
        CancellationToken ct)
    {
        if (!toolMap.TryGetValue(toolCall.Name, out var handler))
        {
            _logger.LogWarning("Tool not found: {ToolName}", toolCall.Name);
            return ToolResult.Fail($"Tool '{toolCall.Name}' not found.");
        }

        try
        {
            _logger.LogDebug("Dispatching tool {ToolName} (id={ToolId})", toolCall.Name, toolCall.Id);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_toolTimeoutSeconds));

            var result = await handler.HandleAsync(toolCall.Input, context, timeoutCts.Token);
            _logger.LogDebug(
                "Tool {ToolName} completed: success={Success}, content length={Length}",
                toolCall.Name, result.Success, result.Success ? result.Content.Length : 0);
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("Tool {ToolName} timed out after {Timeout}s", toolCall.Name, _toolTimeoutSeconds);
            return ToolResult.Fail($"Tool '{toolCall.Name}' timed out after {_toolTimeoutSeconds} seconds.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tool {ToolName} threw an exception", toolCall.Name);
            return ToolResult.Fail($"Tool '{toolCall.Name}' failed: {ex.Message}");
        }
    }

    private static Dictionary<string, IToolHandler> BuildToolMap(IReadOnlyList<IToolHandler> tools)
    {
        var map = new Dictionary<string, IToolHandler>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in tools)
        {
            map[tool.Name] = tool;
        }
        return map;
    }

    private static AgentResponse BuildResponse(
        MessageContent content, string stopReason, TokenUsage usage, int rounds)
    {
        return new AgentResponse
        {
            Content = content,
            StopReason = stopReason,
            Usage = usage,
            ToolRoundsUsed = rounds,
        };
    }
}
