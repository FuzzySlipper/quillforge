namespace QuillForge.Core.Services;

/// <summary>
/// Access to reusable plot arc markdown files stored under the plots content
/// directory.
/// </summary>
public interface IPlotStore
{
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);
    Task<string> LoadAsync(string plotName, CancellationToken ct = default);
    Task SaveAsync(string plotName, string content, CancellationToken ct = default);
    Task<bool> ExistsAsync(string plotName, CancellationToken ct = default);
}
