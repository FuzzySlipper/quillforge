namespace QuillForge.Core.Models;

/// <summary>
/// Result of a tool handler invocation. Constructed via static factory methods
/// to prevent inconsistent state.
/// </summary>
public sealed record ToolResult
{
    public bool Success { get; }
    public string Content { get; }
    public string? Error { get; }
    public bool Retryable { get; }

    private ToolResult(bool success, string content, string? error, bool retryable)
    {
        Success = success;
        Content = content;
        Error = error;
        Retryable = retryable;
    }

    public static ToolResult Ok(string content) => new(true, content, null, retryable: true);
    public static ToolResult Fail(string error, bool retryable = true) => new(false, string.Empty, error, retryable);
    public static ToolResult FailNonRetryable(string error) => Fail(error, retryable: false);
}
