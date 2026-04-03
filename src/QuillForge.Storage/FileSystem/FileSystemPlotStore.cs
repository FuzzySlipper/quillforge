using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Stores reusable plot arc markdown files in the file system.
/// </summary>
public sealed class FileSystemPlotStore : IPlotStore
{
    private readonly string _plotsPath;
    private readonly AtomicFileWriter _writer;
    private readonly ILogger<FileSystemPlotStore> _logger;

    public FileSystemPlotStore(
        string plotsPath,
        AtomicFileWriter writer,
        ILogger<FileSystemPlotStore> logger)
    {
        _plotsPath = plotsPath;
        _writer = writer;
        _logger = logger;
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_plotsPath))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var plots = Directory.GetFiles(_plotsPath, "*.md")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(plots);
    }

    public async Task<string> LoadAsync(string plotName, CancellationToken ct = default)
    {
        var path = ResolvePath(plotName);
        _logger.LogDebug("Loading plot file: {Path}", path);

        if (!File.Exists(path))
        {
            _logger.LogWarning("Plot file not found: {Plot}", plotName);
            return "";
        }

        return await File.ReadAllTextAsync(path, ct);
    }

    public async Task SaveAsync(string plotName, string content, CancellationToken ct = default)
    {
        var path = ResolvePath(plotName);
        _logger.LogInformation("Saving plot file: {Path}", path);
        await _writer.WriteAsync(path, content, ct);
    }

    public Task<bool> ExistsAsync(string plotName, CancellationToken ct = default)
    {
        return Task.FromResult(File.Exists(ResolvePath(plotName)));
    }

    private string ResolvePath(string plotName)
    {
        var normalized = Path.GetFileName(plotName.Trim());
        if (!normalized.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            normalized += ".md";
        }

        return Path.Combine(_plotsPath, normalized);
    }
}
