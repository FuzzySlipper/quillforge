using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Loads writing style templates from the file system.
/// </summary>
public sealed class FileSystemWritingStyleStore : IWritingStyleStore
{
    private readonly string _stylesPath;
    private readonly ILogger<FileSystemWritingStyleStore> _logger;

    public FileSystemWritingStyleStore(string stylesPath, ILogger<FileSystemWritingStyleStore> logger)
    {
        _stylesPath = stylesPath;
        _logger = logger;
    }

    public async Task<string> LoadAsync(string styleName, CancellationToken ct = default)
    {
        var path = FindStyleFile(styleName);
        _logger.LogDebug("Loading writing style: {Path}", path);

        if (!File.Exists(path))
        {
            _logger.LogWarning("Writing style not found: {Name}, returning empty", styleName);
            return "";
        }

        return await File.ReadAllTextAsync(path, ct);
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_stylesPath))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var styles = Directory.GetFiles(_stylesPath, "*.md")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderBy(n => n)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(styles);
    }

    private string FindStyleFile(string styleName)
    {
        // Try exact filename first, then with .md extension
        var exact = Path.Combine(_stylesPath, styleName);
        if (File.Exists(exact)) return exact;

        return Path.Combine(_stylesPath, styleName + ".md");
    }
}
