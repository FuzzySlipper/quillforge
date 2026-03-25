using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Loads lore content from the file system. Lore sets are subdirectories of the lore root.
/// </summary>
public sealed class FileSystemLoreStore : ILoreStore
{
    private readonly string _lorePath;
    private readonly ILogger<FileSystemLoreStore> _logger;

    public FileSystemLoreStore(string lorePath, ILogger<FileSystemLoreStore> logger)
    {
        _lorePath = lorePath;
        _logger = logger;
    }

    public Task<IReadOnlyDictionary<string, string>> LoadLoreSetAsync(string loreSetName, CancellationToken ct = default)
    {
        var setPath = Path.Combine(_lorePath, loreSetName);
        _logger.LogDebug("Loading lore set: {Path}", setPath);

        if (!Directory.Exists(setPath))
        {
            _logger.LogWarning("Lore set directory not found: {Path}", setPath);
            return Task.FromResult<IReadOnlyDictionary<string, string>>(
                new Dictionary<string, string>());
        }

        var result = new Dictionary<string, string>();
        foreach (var file in Directory.GetFiles(setPath, "*.md", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(setPath, file);
            var content = File.ReadAllText(file);
            result[relativePath] = content;
        }

        _logger.LogInformation("Loaded lore set \"{Name}\": {Count} files", loreSetName, result.Count);
        return Task.FromResult<IReadOnlyDictionary<string, string>>(result);
    }

    public Task<IReadOnlyList<string>> ListLoreSetsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_lorePath))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var sets = Directory.GetDirectories(_lorePath)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => n!)
            .OrderBy(n => n)
            .ToList();

        _logger.LogDebug("Found {Count} lore sets", sets.Count);
        return Task.FromResult<IReadOnlyList<string>>(sets);
    }

    public Task<IReadOnlyList<(string FilePath, string Snippet)>> SearchAsync(
        string loreSetName, string query, CancellationToken ct = default)
    {
        var setPath = Path.Combine(_lorePath, loreSetName);
        _logger.LogDebug("Searching lore set \"{Name}\" for \"{Query}\"", loreSetName, query);

        if (!Directory.Exists(setPath))
        {
            return Task.FromResult<IReadOnlyList<(string, string)>>([]);
        }

        var results = new List<(string FilePath, string Snippet)>();
        foreach (var file in Directory.GetFiles(setPath, "*.md", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);
            var idx = content.IndexOf(query, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
            {
                var snippetStart = Math.Max(0, idx - 50);
                var snippetEnd = Math.Min(content.Length, idx + query.Length + 50);
                var snippet = content[snippetStart..snippetEnd].Trim();
                var relativePath = Path.GetRelativePath(setPath, file);
                results.Add((relativePath, snippet));
            }
        }

        _logger.LogDebug("Search found {Count} matches", results.Count);
        return Task.FromResult<IReadOnlyList<(string, string)>>(results);
    }
}
