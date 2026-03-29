namespace QuillForge.Core.Services;

/// <summary>
/// Generic file operations within content directories (layouts, council prompts, etc.).
/// </summary>
public interface IContentFileService
{
    Task<string> ReadAsync(string relativePath, CancellationToken ct = default);
    Task WriteAsync(string relativePath, string content, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListAsync(string directory, string? pattern = null, CancellationToken ct = default);
    Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default);
    Task DeleteAsync(string relativePath, CancellationToken ct = default);
    Task<IReadOnlyList<(string FilePath, string Snippet)>> SearchAsync(
        string directory, string query, CancellationToken ct = default);
}
