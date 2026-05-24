using QuillForge.Core.Models;

namespace QuillForge.Core.Pipeline;

/// <summary>
/// Centralized, thread-safe stats accounting for a single forge pipeline run.
/// All forge token usage, agent call counts, chapter revisions, and stage timing
/// flow through this tracker. Stages and agents call the Record* methods;
/// the pipeline calls <see cref="ApplyTo"/> before each manifest persist.
///
/// If you are adding a new forge stage, agent, or tool that consumes tokens or
/// introduces timing-sensitive work, you MUST record usage through this tracker.
/// See RecordCompletion for token/call accounting and StageStarted/StageCompleted
/// for timing boundaries.
/// </summary>
public sealed class ForgeStatsTracker
{
    private readonly object _lock = new();
    private long _totalInputTokens;
    private long _totalOutputTokens;
    private int _agentCalls;
    private int _chaptersRevised;
    private long _totalLatencyMs;
    private readonly Dictionary<string, StageTiming> _stageTiming = new();

    /// <summary>
    /// Creates a new tracker, optionally seeded from existing manifest stats
    /// so that resumed runs continue accumulating rather than resetting.
    /// </summary>
    public ForgeStatsTracker(ForgeStats? existingStats = null)
    {
        if (existingStats is null) return;

        _totalInputTokens = existingStats.TotalInputTokens;
        _totalOutputTokens = existingStats.TotalOutputTokens;
        _agentCalls = existingStats.AgentCalls;
        _chaptersRevised = existingStats.ChaptersRevised;
        _totalLatencyMs = existingStats.TotalLatencyMs;

        foreach (var (stage, timing) in existingStats.StageTiming)
        {
            _stageTiming[stage] = timing;
        }
    }

    /// <summary>
    /// Records token usage and increments the agent call count from a single
    /// LLM completion (planner, writer, reviewer, or forge-triggered librarian).
    /// </summary>
    public void RecordCompletion(string agentName, TokenUsage usage)
    {
        lock (_lock)
        {
            _totalInputTokens += usage.InputTokens;
            _totalOutputTokens += usage.OutputTokens;
            _agentCalls++;
        }
    }

    /// <summary>
    /// Records token usage, increments the agent call count, and accumulates provider latency
    /// from a single LLM completion.
    /// </summary>
    public void RecordCompletionWithLatency(string agentName, TokenUsage usage, long latencyMs)
    {
        lock (_lock)
        {
            _totalInputTokens += usage.InputTokens;
            _totalOutputTokens += usage.OutputTokens;
            _agentCalls++;
            _totalLatencyMs += latencyMs;
        }
    }

    /// <summary>
    /// Records that a chapter revision occurred (writer re-drafting after review feedback).
    /// </summary>
    public void RecordChapterRevision(string chapterId)
    {
        lock (_lock)
        {
            _chaptersRevised++;
        }
    }

    /// <summary>
    /// Records the start of a pipeline stage. Called by the pipeline, not by stages.
    /// </summary>
    public void StageStarted(string stageName, DateTimeOffset at)
    {
        lock (_lock)
        {
            _stageTiming[stageName] = new StageTiming(at, null);
        }
    }

    /// <summary>
    /// Records the successful completion of a pipeline stage.
    /// </summary>
    public void StageCompleted(string stageName, DateTimeOffset at)
    {
        lock (_lock)
        {
            if (_stageTiming.TryGetValue(stageName, out var existing))
            {
                _stageTiming[stageName] = existing with { End = at };
            }
            else
            {
                // Stage completed without a recorded start — record end-only
                _stageTiming[stageName] = new StageTiming(at, at);
            }
        }
    }

    /// <summary>
    /// Records a stage failure. The stage gets an end timestamp so timing
    /// reflects how long it ran before failing, rather than disappearing.
    /// </summary>
    public void StageFailed(string stageName, DateTimeOffset at)
    {
        StageCompleted(stageName, at);
    }

    /// <summary>
    /// Returns an immutable snapshot of the current stats.
    /// </summary>
    public ForgeStats Snapshot()
    {
        lock (_lock)
        {
            return new ForgeStats
            {
                TotalInputTokens = _totalInputTokens,
                TotalOutputTokens = _totalOutputTokens,
                AgentCalls = _agentCalls,
                ChaptersRevised = _chaptersRevised,
                TotalLatencyMs = _totalLatencyMs,
                StageTiming = new Dictionary<string, StageTiming>(_stageTiming),
            };
        }
    }

    /// <summary>
    /// Returns a new manifest with the current stats applied.
    /// Use this before persisting the manifest so stats are never lost.
    /// </summary>
    public ForgeManifest ApplyTo(ForgeManifest manifest)
    {
        return manifest with { Stats = Snapshot() };
    }
}
