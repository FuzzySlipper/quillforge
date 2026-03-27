namespace QuillForge.Core.Diagnostics;

/// <summary>
/// Logs all LLM interactions to a debug file for diagnostics.
/// </summary>
public interface ILlmDebugLogger
{
    void LogRequest(string agent, string model, int maxTokens, string systemPreview, int messagesCount, int toolsCount);
    void LogResponse(string agent, string model, string? stopReason, string contentPreview, int inputTokens = 0, int outputTokens = 0, string? error = null);
    void LogError(string agent, string model, string error);
}
