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

    private ToolResult(bool success, string content, string? error)
    {
        Success = success;
        Content = content;
        Error = error;
    }

    public static ToolResult Ok(string content) => new(true, content, null);
    public static ToolResult Fail(string error) => new(false, string.Empty, error);
}
