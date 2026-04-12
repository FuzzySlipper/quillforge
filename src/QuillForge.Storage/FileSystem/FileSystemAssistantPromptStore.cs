using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Loads assistant style/personality prompt layers from the file system.
/// Missing files resolve to an empty layer because the Assistant base prompt is app-owned.
/// </summary>
public sealed class FileSystemAssistantPromptStore : IAssistantPromptStore
{
    private readonly string _promptsPath;
    private readonly ILogger<FileSystemAssistantPromptStore> _logger;

    public FileSystemAssistantPromptStore(
        string promptsPath,
        ILogger<FileSystemAssistantPromptStore> logger)
    {
        _promptsPath = promptsPath;
        _logger = logger;
    }

    public async Task<string> LoadAsync(string promptName, CancellationToken ct = default)
    {
        var filePath = ResolvePromptPath(promptName);
        if (filePath is null)
        {
            _logger.LogWarning(
                "Path traversal blocked for assistant prompt: {Name}; returning empty layer",
                promptName);
            return string.Empty;
        }

        if (!File.Exists(filePath))
        {
            _logger.LogInformation(
                "Assistant prompt file not found: {Path}; using Assistant base prompt only",
                filePath);
            return string.Empty;
        }

        var content = await File.ReadAllTextAsync(filePath, ct);
        _logger.LogDebug("Loaded assistant prompt: {Name} ({Length} chars)", promptName, content.Length);
        return content.Trim();
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_promptsPath))
        {
            _logger.LogWarning("Assistant prompts directory does not exist: {Path}", _promptsPath);
            return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
        }

        var names = Directory.GetFiles(_promptsPath, "*.md")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n is not null)
            .Select(n => n!)
            .OrderBy(n => n)
            .ToList();

        _logger.LogDebug("Listed {Count} assistant prompts from {Path}", names.Count, _promptsPath);
        return Task.FromResult<IReadOnlyList<string>>(names);
    }

    private string? ResolvePromptPath(string promptName)
    {
        if (!PathBoundaryGuard.TryResolvePath(_promptsPath, promptName + ".md", out var resolved))
        {
            return null;
        }

        return resolved;
    }
}
