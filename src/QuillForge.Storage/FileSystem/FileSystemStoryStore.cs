using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// File-system backed story/chapter storage. Supports read, write, and append.
/// </summary>
public sealed class FileSystemStoryStore : IStoryStore
{
    private readonly string _storyPath;
    private readonly AtomicFileWriter _writer;
    private readonly ILogger<FileSystemStoryStore> _logger;

    public FileSystemStoryStore(string storyPath, AtomicFileWriter writer, ILogger<FileSystemStoryStore> logger)
    {
        _storyPath = storyPath;
        _writer = writer;
        _logger = logger;
    }

    public async Task<string> ReadAsync(string projectName, string fileName, CancellationToken ct = default)
    {
        var path = Path.Combine(_storyPath, projectName, fileName);
        _logger.LogDebug("Reading story file: {Path}", path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Story file not found: {projectName}/{fileName}", path);
        }

        return await File.ReadAllTextAsync(path, ct);
    }

    public async Task AppendAsync(string projectName, string fileName, string content, CancellationToken ct = default)
    {
        var path = Path.Combine(_storyPath, projectName, fileName);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        _logger.LogDebug("Appending to story file: {Path} ({Length} chars)", path, content.Length);

        // For append, read existing + write new content atomically
        var existing = File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : "";
        await _writer.WriteAsync(path, existing + content, ct);
    }

    public async Task WriteAsync(string projectName, string fileName, string content, CancellationToken ct = default)
    {
        var path = Path.Combine(_storyPath, projectName, fileName);
        _logger.LogDebug("Writing story file: {Path} ({Length} chars)", path, content.Length);
        await _writer.WriteAsync(path, content, ct);
    }

    public Task<IReadOnlyList<string>> ListProjectsAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_storyPath))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var projects = Directory.GetDirectories(_storyPath)
            .Select(Path.GetFileName)
            .Where(n => n is not null)
            .Select(n => n!)
            .OrderBy(n => n)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(projects);
    }

    public Task<IReadOnlyList<string>> ListFilesAsync(string projectName, CancellationToken ct = default)
    {
        var projectPath = Path.Combine(_storyPath, projectName);
        if (!Directory.Exists(projectPath))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var files = Directory.GetFiles(projectPath, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(projectPath, f))
            .OrderBy(f => f)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(files);
    }
}
