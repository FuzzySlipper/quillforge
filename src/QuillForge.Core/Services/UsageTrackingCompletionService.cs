using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// Decorator that intercepts all LLM calls at the ICompletionService boundary and
/// records token usage to the ITokenUsageTracker. Reads session/agent context from
/// the ambient TokenTrackingScope (AsyncLocal).
///
/// This is intentionally placed at the outermost ICompletionService layer so that
/// every call — orchestrator, sub-agents, forge, artifacts — is captured. If a call
/// reaches the LLM without a tracking scope set, it is logged as a warning (indicates
/// a code path that isn't flowing through the expected event boundary).
/// </summary>
public sealed class UsageTrackingCompletionService : ICompletionService
{
    private readonly ICompletionService _inner;
    private readonly ITokenUsageTracker _tracker;
    private readonly ILogger<UsageTrackingCompletionService> _logger;

    public UsageTrackingCompletionService(
        ICompletionService inner,
        ITokenUsageTracker tracker,
        ILogger<UsageTrackingCompletionService> logger)
    {
        _inner = inner;
        _tracker = tracker;
        _logger = logger;
    }

    public async Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
    {
        var response = await _inner.CompleteAsync(request, ct);
        RecordUsage(response.Usage);
        return response;
    }

    public IAsyncEnumerable<StreamEvent> StreamAsync(CompletionRequest request, CancellationToken ct = default)
    {
        return StreamWithTrackingAsync(request, ct);
    }

    private async IAsyncEnumerable<StreamEvent> StreamWithTrackingAsync(
        CompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var evt in _inner.StreamAsync(request, ct))
        {
            if (evt is DoneEvent done)
            {
                RecordUsage(done.Usage);
            }
            yield return evt;
        }
    }

    private void RecordUsage(TokenUsage usage)
    {
        var scope = TokenTrackingScope.Current;
        if (scope is null)
        {
            _logger.LogWarning(
                "LLM call completed without a TokenTrackingScope — {Input}in/{Output}out tokens untracked. " +
                "Set TokenTrackingScope.Begin() at the call site to ensure usage is captured.",
                usage.InputTokens, usage.OutputTokens);
            return;
        }

        _tracker.Record(scope.SessionId, scope.AgentName, usage);
    }
}
