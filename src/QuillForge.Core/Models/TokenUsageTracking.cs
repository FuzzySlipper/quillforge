namespace QuillForge.Core.Models;

/// <summary>
/// Per-agent token usage accumulator entry.
/// </summary>
public sealed record AgentUsageEntry
{
    public required string AgentName { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
    public int RequestCount { get; init; }
}

/// <summary>
/// Aggregated token usage for a session across all agents.
/// </summary>
public sealed record SessionUsageSummary
{
    public int TotalInputTokens { get; init; }
    public int TotalOutputTokens { get; init; }
    public int TotalRequests { get; init; }
    public IReadOnlyList<AgentUsageEntry> ByAgent { get; init; } = [];
}
