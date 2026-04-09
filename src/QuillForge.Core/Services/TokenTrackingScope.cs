namespace QuillForge.Core.Services;

/// <summary>
/// AsyncLocal scope that carries session ID and agent name through the call chain.
/// Used by the UsageTrackingCompletionService decorator to associate LLM calls
/// with the correct session and agent without requiring changes to ICompletionService.
/// </summary>
public static class TokenTrackingScope
{
    private static readonly AsyncLocal<ScopeData?> s_current = new();

    public static ScopeData? Current => s_current.Value;

    /// <summary>
    /// Begins a new tracking scope. Returns a disposable that restores the previous scope.
    /// Scopes nest: a sub-agent can override the agent name while preserving the session ID.
    /// </summary>
    public static IDisposable Begin(Guid sessionId, string agentName)
    {
        var previous = s_current.Value;
        s_current.Value = new ScopeData(sessionId, agentName);
        return new ScopeHandle(previous);
    }

    public sealed record ScopeData(Guid SessionId, string AgentName);

    private sealed class ScopeHandle(ScopeData? previous) : IDisposable
    {
        public void Dispose() => s_current.Value = previous;
    }
}
