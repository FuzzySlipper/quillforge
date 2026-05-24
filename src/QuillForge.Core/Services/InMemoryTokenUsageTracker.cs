using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// Thread-safe in-memory token usage and latency tracker. Accumulates per-session, per-agent usage
/// for the lifetime of the process. Resets on app restart.
/// </summary>
public sealed class InMemoryTokenUsageTracker : ITokenUsageTracker
{
    private readonly ConcurrentDictionary<Guid, SessionAccumulator> _sessions = new();
    private readonly ILogger<InMemoryTokenUsageTracker> _logger;

    public InMemoryTokenUsageTracker(ILogger<InMemoryTokenUsageTracker> logger)
    {
        _logger = logger;
    }

    public void Record(Guid sessionId, string agentName, TokenUsage usage, long latencyMs)
    {
        var accumulator = _sessions.GetOrAdd(sessionId, _ => new SessionAccumulator());
        accumulator.Add(agentName, usage.InputTokens, usage.OutputTokens, latencyMs);

        _logger.LogDebug(
            "Token usage recorded: session={SessionId}, agent={Agent}, input={Input}, output={Output}, latency={LatencyMs}ms",
            sessionId, agentName, usage.InputTokens, usage.OutputTokens, latencyMs);
    }

    public SessionUsageSummary GetSessionUsage(Guid sessionId)
    {
        if (!_sessions.TryGetValue(sessionId, out var accumulator))
        {
            return new SessionUsageSummary();
        }

        return accumulator.ToSummary();
    }

    public void ClearSession(Guid sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
    }

    private sealed class SessionAccumulator
    {
        private readonly object _lock = new();
        private readonly Dictionary<string, AgentCounter> _agents = new(StringComparer.OrdinalIgnoreCase);
        private int _totalInput;
        private int _totalOutput;
        private int _totalRequests;
        private long _totalLatencyMs;

        public void Add(string agentName, int inputTokens, int outputTokens, long latencyMs)
        {
            lock (_lock)
            {
                _totalInput += inputTokens;
                _totalOutput += outputTokens;
                _totalRequests++;
                _totalLatencyMs += latencyMs;

                if (!_agents.TryGetValue(agentName, out var counter))
                {
                    counter = new AgentCounter();
                    _agents[agentName] = counter;
                }
                counter.InputTokens += inputTokens;
                counter.OutputTokens += outputTokens;
                counter.RequestCount++;
                counter.TotalLatencyMs += latencyMs;
            }
        }

        public SessionUsageSummary ToSummary()
        {
            lock (_lock)
            {
                var entries = new List<AgentUsageEntry>(_agents.Count);
                foreach (var (name, counter) in _agents)
                {
                    entries.Add(new AgentUsageEntry
                    {
                        AgentName = name,
                        InputTokens = counter.InputTokens,
                        OutputTokens = counter.OutputTokens,
                        RequestCount = counter.RequestCount,
                        TotalLatencyMs = counter.TotalLatencyMs,
                    });
                }

                // Sort by total tokens descending for consistent display
                entries.Sort((a, b) =>
                    (b.InputTokens + b.OutputTokens).CompareTo(a.InputTokens + a.OutputTokens));

                return new SessionUsageSummary
                {
                    TotalInputTokens = _totalInput,
                    TotalOutputTokens = _totalOutput,
                    TotalRequests = _totalRequests,
                    TotalLatencyMs = _totalLatencyMs,
                    ByAgent = entries,
                };
            }
        }
    }

    private sealed class AgentCounter
    {
        public int InputTokens;
        public int OutputTokens;
        public int RequestCount;
        public long TotalLatencyMs;
    }
}
