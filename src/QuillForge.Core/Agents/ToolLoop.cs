using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.Logging;
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

    public ToolLoop(
        ICompletionService completionService,
        ContinuationStrategy continuationStrategy,
        ILogger<ToolLoop> logger)
    {
        _completionService = completionService;
        _continuationStrategy = continuationStrategy;
        _logger = logger;
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
            };

            _logger.LogDebug("ToolLoop round {Round}: calling completion service", round);

            var response = await _completionService.CompleteAsync(request, ct);
            totalUsage = _continuationStrategy.AggregateUsage(totalUsage, response.Usage);

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

            // Append assistant message with tool_use blocks
            messages.Add(new CompletionMessage("assistant", response.Content));

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
            };

            // For intermediate rounds (tool dispatch), use non-streaming
            // For the final round (no more tools), we stream to caller
            // We don't know in advance which round is final, so we always try streaming
            // and collect tool calls if they appear.

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

            // If no tool calls, we're done
            if (collectedToolCalls.Count == 0 ||
                string.Equals(stopReason, "end_turn", StringComparison.OrdinalIgnoreCase))
            {
                yield return new DoneEvent(stopReason ?? "end_turn", usage ?? new TokenUsage(0, 0));
                yield break;
            }

            // Tool dispatch round
            round++;
            if (round > config.MaxToolRounds)
            {
                _logger.LogWarning("ToolLoop (streaming) hit max rounds ({MaxRounds})", config.MaxToolRounds);
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
                var fakeToolUse = new ToolUseBlock(tc.ToolId, tc.ToolName, tc.Input);
                var result = await DispatchToolAsync(toolMap, fakeToolUse, context, ct);
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
            var result = await handler.HandleAsync(toolCall.Input, context, ct);
            _logger.LogDebug(
                "Tool {ToolName} completed: success={Success}, content length={Length}",
                toolCall.Name, result.Success, result.Success ? result.Content.Length : 0);
            return result;
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
