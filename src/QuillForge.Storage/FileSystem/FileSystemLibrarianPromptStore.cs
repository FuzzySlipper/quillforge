using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Loads librarian instruction prompts from the file system.
/// Each prompt is a single .md file in the librarian-prompts directory.
/// </summary>
public sealed class FileSystemLibrarianPromptStore : ILibrarianPromptStore
{
    private readonly string _promptsPath;
    private readonly ILogger<FileSystemLibrarianPromptStore> _logger;

    public FileSystemLibrarianPromptStore(
        string promptsPath,
        ILogger<FileSystemLibrarianPromptStore> logger)
    {
        _promptsPath = promptsPath;
        _logger = logger;
    }

    public async Task<string> LoadAsync(string promptName, CancellationToken ct = default)
    {
        var filePath = Path.Combine(_promptsPath, promptName + ".md");
        if (!File.Exists(filePath))
        {
            _logger.LogWarning("Librarian prompt file not found: {Path}, using empty instructions", filePath);
            return "";
        }

        var content = await File.ReadAllTextAsync(filePath, ct);
        _logger.LogDebug("Loaded librarian prompt: {Name} ({Length} chars)", promptName, content.Length);
        return content.Trim();
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_promptsPath))
        {
            _logger.LogWarning("Librarian prompts directory does not exist: {Path}", _promptsPath);
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var names = Directory.GetFiles(_promptsPath, "*.md")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null)
            .Select(n => n!)
            .OrderBy(n => n)
            .ToList();

        _logger.LogDebug("Listed {Count} librarian prompts from {Path}", names.Count, _promptsPath);
        return Task.FromResult<IReadOnlyList<string>>(names);
    }
}
