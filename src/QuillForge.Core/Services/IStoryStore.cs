namespace QuillForge.Core.Services;

/// <summary>
/// Read and write story/chapter files.
/// </summary>
public interface IStoryStore
{
    Task<string> ReadAsync(string projectName, string fileName, CancellationToken ct = default);
    Task AppendAsync(string projectName, string fileName, string content, CancellationToken ct = default);
    Task WriteAsync(string projectName, string fileName, string content, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListProjectsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListFilesAsync(string projectName, CancellationToken ct = default);
}
