using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// Tracks cumulative token usage per session. Thread-safe.
/// </summary>
public interface ITokenUsageTracker
{
    /// <summary>
    /// Records a single LLM call's token usage for a session/agent pair.
    /// </summary>
    void Record(Guid sessionId, string agentName, TokenUsage usage);

    /// <summary>
    /// Returns accumulated usage for a session across all agents.
    /// </summary>
    SessionUsageSummary GetSessionUsage(Guid sessionId);

    /// <summary>
    /// Clears tracked usage for a session (e.g. on session delete).
    /// </summary>
    void ClearSession(Guid sessionId);
}
