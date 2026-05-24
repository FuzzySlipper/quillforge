namespace QuillForge.Core.Models;

/// <summary>
/// Per-agent token usage and latency accumulator entry.
/// </summary>
public sealed record AgentUsageEntry
{
    public required string AgentName { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int RequestCount { get; init; }

    /// <summary>Total wall-clock latency across all requests, in milliseconds.</summary>
    public long TotalLatencyMs { get; init; }

    /// <summary>Average request latency in milliseconds (0 when no requests).</summary>
    public double AverageLatencyMs => RequestCount > 0 ? (double)TotalLatencyMs / RequestCount : 0;
}

/// <summary>
/// Aggregated token usage for a session across all agents.
/// </summary>
public sealed record SessionUsageSummary
{
    public int TotalInputTokens { get; init; }
    public int TotalOutputTokens { get; init; }
    public int TotalRequests { get; init; }

    /// <summary>Total wall-clock latency across all requests, in milliseconds.</summary>
    public long TotalLatencyMs { get; init; }

    /// <summary>Average request latency in milliseconds (0 when no requests).</summary>
    public double AverageLatencyMs => TotalRequests > 0 ? (double)TotalLatencyMs / TotalRequests : 0;

    public IReadOnlyList<AgentUsageEntry> ByAgent { get; init; } = [];
}
