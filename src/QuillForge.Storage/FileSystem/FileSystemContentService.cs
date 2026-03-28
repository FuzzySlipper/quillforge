using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Generic file operations within content directories.
/// All writes are atomic (write-to-temp-then-rename).
/// </summary>
public sealed class FileSystemContentService : IContentFileService
{
    private readonly string _basePath;
    private readonly AtomicFileWriter _writer;
    private readonly ILogger<FileSystemContentService> _logger;

    public FileSystemContentService(string basePath, AtomicFileWriter writer, ILogger<FileSystemContentService> logger)
    {
        _basePath = basePath;
        _writer = writer;
        _logger = logger;
    }

    public async Task<string> ReadAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(relativePath);
        _logger.LogDebug("Reading file: {Path}", fullPath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"Content file not found: {relativePath}", fullPath);
        }

        return await File.ReadAllTextAsync(fullPath, ct);
    }

    public async Task WriteAsync(string relativePath, string content, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(relativePath);
        _logger.LogDebug("Writing file: {Path} ({Length} chars)", fullPath, content.Length);
        await _writer.WriteAsync(fullPath, content, ct);
    }

    public Task<IReadOnlyList<string>> ListAsync(string directory, string? pattern = null, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(directory);
        _logger.LogDebug("Listing files in: {Path}, pattern={Pattern}", fullPath, pattern);

        if (!Directory.Exists(fullPath))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var searchPattern = pattern ?? "*";
        var files = Directory.GetFiles(fullPath, searchPattern, SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(_basePath, f))
            .OrderBy(f => f)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(relativePath);
        return Task.FromResult(File.Exists(fullPath));
    }

    public Task DeleteAsync(string relativePath, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(relativePath);
        _logger.LogDebug("Deleting file: {Path}", fullPath);

        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private string ResolvePath(string relativePath)
    {
        // Prevent path traversal — use separator-aware check to block sibling-prefix escapes
        var resolved = Path.GetFullPath(Path.Combine(_basePath, relativePath));
        var root = _basePath.EndsWith(Path.DirectorySeparatorChar)
            ? _basePath
            : _basePath + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Path traversal detected: {relativePath}");
        }
        return resolved;
    }
}
