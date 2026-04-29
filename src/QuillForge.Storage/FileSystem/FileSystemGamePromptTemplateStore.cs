using Microsoft.Extensions.Logging;
using QuillForge.Core;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Persists user-owned game agent prompt templates under user/game-prompts/{moduleId}.
/// Bundled module defaults are resolved from registered game modules, not this store.
/// </summary>
public sealed class FileSystemGamePromptTemplateStore : IGamePromptTemplateStore
{
    private readonly string _promptsPath;
    private readonly AtomicFileWriter _writer;
    private readonly ILogger<FileSystemGamePromptTemplateStore> _logger;

    public FileSystemGamePromptTemplateStore(
        string contentRoot,
        AtomicFileWriter writer,
        ILogger<FileSystemGamePromptTemplateStore> logger)
    {
        _promptsPath = Path.Combine(contentRoot, ContentPaths.GamePrompts);
        _writer = writer;
        _logger = logger;
        Directory.CreateDirectory(_promptsPath);
    }

    public Task<IReadOnlyList<GameUserPromptTemplateInfo>> ListAsync(string moduleId, CancellationToken ct = default)
    {
        var moduleDirectory = ResolveModuleDirectory(moduleId, create: false);
        if (!Directory.Exists(moduleDirectory))
        {
            return Task.FromResult<IReadOnlyList<GameUserPromptTemplateInfo>>([]);
        }

        var prompts = Directory.GetFiles(moduleDirectory, "*.md", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                var contentLength = new FileInfo(path).Length;
                var relativePath = Path.GetRelativePath(_promptsPath, path).Replace(Path.DirectorySeparatorChar, '/');
                var name = Path.GetFileNameWithoutExtension(path);
                return new GameUserPromptTemplateInfo
                {
                    ModuleId = NormalizeSegment(moduleId, nameof(moduleId)),
                    Name = name,
                    FileName = Path.GetFileName(path),
                    RelativePath = $"{ContentPaths.GamePrompts}/{relativePath}",
                    Tokens = (int)(contentLength / 4),
                    Size = contentLength,
                };
            })
            .OrderBy(prompt => prompt.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult<IReadOnlyList<GameUserPromptTemplateInfo>>(prompts);
    }

    public async Task<GameUserPromptTemplateDocument?> TryLoadAsync(string moduleId, string promptName, CancellationToken ct = default)
    {
        string path;
        try
        {
            path = ResolvePromptPath(moduleId, promptName, createModuleDirectory: false);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid game prompt template path: module={ModuleId} prompt={PromptName}", moduleId, promptName);
            return null;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(path, ct);
        return ToDocument(moduleId, promptName, path, content);
    }

    public async Task SaveAsync(string moduleId, string promptName, string content, CancellationToken ct = default)
    {
        var path = ResolvePromptPath(moduleId, promptName, createModuleDirectory: true);
        await _writer.WriteAsync(path, content, ct);
        _logger.LogInformation("Saved game prompt template {ModuleId}/{PromptName} to {Path}", moduleId, promptName, path);
    }

    public async Task<GameUserPromptTemplateDocument> CreateUniqueAsync(
        string moduleId,
        string basePromptName,
        string content,
        CancellationToken ct = default)
    {
        var normalizedBase = NormalizeSegment(basePromptName, nameof(basePromptName));
        for (var index = 0; index < 10_000; index++)
        {
            var candidate = index == 0 ? normalizedBase : $"{normalizedBase}-{index + 1}";
            var path = ResolvePromptPath(moduleId, candidate, createModuleDirectory: true);
            if (File.Exists(path))
            {
                continue;
            }

            await _writer.WriteAsync(path, content, ct);
            _logger.LogInformation("Copied default game prompt template to {Path}", path);
            return ToDocument(moduleId, candidate, path, content);
        }

        throw new InvalidOperationException($"Could not create a unique game prompt template for module '{moduleId}'.");
    }

    private GameUserPromptTemplateDocument ToDocument(string moduleId, string promptName, string path, string content)
    {
        var relativeToPromptRoot = Path.GetRelativePath(_promptsPath, path).Replace(Path.DirectorySeparatorChar, '/');
        return new GameUserPromptTemplateDocument
        {
            ModuleId = NormalizeSegment(moduleId, nameof(moduleId)),
            Name = NormalizeSegment(promptName, nameof(promptName)),
            FileName = Path.GetFileName(path),
            RelativePath = $"{ContentPaths.GamePrompts}/{relativeToPromptRoot}",
            Content = content,
        };
    }

    private string ResolveModuleDirectory(string moduleId, bool create)
    {
        var normalized = NormalizeSegment(moduleId, nameof(moduleId));
        var path = PathBoundaryGuard.ResolvePathOrThrow(_promptsPath, normalized);
        if (create)
        {
            Directory.CreateDirectory(path);
        }

        return path;
    }

    private string ResolvePromptPath(string moduleId, string promptName, bool createModuleDirectory)
    {
        var moduleDirectory = ResolveModuleDirectory(moduleId, createModuleDirectory);
        var normalizedName = NormalizeSegment(promptName, nameof(promptName));
        return PathBoundaryGuard.ResolvePathOrThrow(moduleDirectory, normalizedName + ".md");
    }

    private static string NormalizeSegment(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{parameterName} is required.", parameterName);
        }

        var trimmed = value.Trim();
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || trimmed.Contains(Path.DirectorySeparatorChar)
            || trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException($"Invalid game prompt template segment: {value}", parameterName);
        }

        return trimmed;
    }
}
