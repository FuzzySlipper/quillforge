using QuillForge.Core.Services;

namespace QuillForge.Core.Tests.Fakes;

/// <summary>
/// In-memory file service for testing. Stores files in a dictionary.
/// </summary>
public sealed class FakeContentFileService : IContentFileService
{
    private readonly Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

    public void SeedFile(string path, string content) => _files[path] = content;

    public IReadOnlyDictionary<string, string> Files => _files;

    public Task<string> ReadAsync(string relativePath, CancellationToken ct = default)
    {
        if (!_files.TryGetValue(relativePath, out var content))
            throw new FileNotFoundException($"File not found: {relativePath}");
        return Task.FromResult(content);
    }

    public Task WriteAsync(string relativePath, string content, CancellationToken ct = default)
    {
        _files[relativePath] = content;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListAsync(string directory, string? pattern = null, CancellationToken ct = default)
    {
        var matches = _files.Keys
            .Where(k => k.StartsWith(directory, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(matches);
    }

    public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
    {
        return Task.FromResult(_files.ContainsKey(relativePath));
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        _files.Remove(relativePath);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<(string FilePath, string Snippet)>> SearchAsync(
        string directory, string query, CancellationToken ct = default)
    {
        var results = _files
            .Where(kv => kv.Key.StartsWith(directory, StringComparison.OrdinalIgnoreCase)
                && kv.Value.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(kv =>
            {
                var line = kv.Value.Split('\n')
                    .FirstOrDefault(l => l.Contains(query, StringComparison.OrdinalIgnoreCase)) ?? "";
                return (kv.Key, line.Trim());
            })
            .ToList();
        return Task.FromResult<IReadOnlyList<(string, string)>>(results);
    }
}
