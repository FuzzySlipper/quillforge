namespace QuillForge.Core.Services;

/// <summary>
/// Implemented by services that want to expose internal state for debugging.
/// The /debug tool handler collects from all registered sources.
/// </summary>
public interface IDiagnosticSource
{
    /// <summary>
    /// Category name for grouping diagnostics (e.g., "session", "forge", "providers").
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Returns a snapshot of current diagnostic state.
    /// </summary>
    Task<IReadOnlyDictionary<string, object>> GetDiagnosticsAsync(CancellationToken ct = default);
}
