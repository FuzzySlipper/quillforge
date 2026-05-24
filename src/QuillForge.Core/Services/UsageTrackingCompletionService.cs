using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// Decorator that intercepts all LLM calls at the ICompletionService boundary and
/// records token usage and provider latency to the ITokenUsageTracker. Reads session/agent
/// context from the ambient TokenTrackingScope (AsyncLocal).
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
        var sw = Stopwatch.StartNew();
        var response = await _inner.CompleteAsync(request, ct);
        sw.Stop();
        RecordUsage(response.Usage, sw.ElapsedMilliseconds, TokenTrackingScope.Current);
        return response;
    }

    public IAsyncEnumerable<StreamEvent> StreamAsync(CompletionRequest request, CancellationToken ct = default)
    {
        var scope = TokenTrackingScope.Current;
        var sw = Stopwatch.StartNew();
        return StreamWithTrackingAsync(request, scope, sw, ct);
    }

    private async IAsyncEnumerable<StreamEvent> StreamWithTrackingAsync(
        CompletionRequest request,
        TokenTrackingScope.ScopeData? scope,
        Stopwatch sw,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var evt in _inner.StreamAsync(request, ct))
        {
            if (evt is DoneEvent done)
            {
                sw.Stop();
                RecordUsage(done.Usage, sw.ElapsedMilliseconds, scope);
            }
            yield return evt;
        }
    }

    private void RecordUsage(TokenUsage usage, long latencyMs, TokenTrackingScope.ScopeData? scope)
    {
        if (scope is null)
        {
            _logger.LogWarning(
                "LLM call completed without a TokenTrackingScope — {Input}in/{Output}out tokens, {Latency}ms untracked. " +
                "Set TokenTrackingScope.Begin() at the call site to ensure usage is captured.",
                usage.InputTokens, usage.OutputTokens, latencyMs);
            return;
        }

        _tracker.Record(scope.SessionId, scope.AgentName, usage, latencyMs);
    }
}
