using Microsoft.Extensions.Logging;
using QuillForge.Core;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Persists generic user-owned game persona prompts under user/game-personas.
/// Persona prompts are reusable across modules and templates.
/// </summary>
public sealed class FileSystemGamePersonaPromptStore : IGamePersonaPromptStore
{
    private readonly string _personasPath;
    private readonly AtomicFileWriter _writer;
    private readonly ILogger<FileSystemGamePersonaPromptStore> _logger;

    public FileSystemGamePersonaPromptStore(
        string contentRoot,
        AtomicFileWriter writer,
        ILogger<FileSystemGamePersonaPromptStore> logger)
    {
        _personasPath = Path.Combine(contentRoot, ContentPaths.GamePersonas);
        _writer = writer;
        _logger = logger;
        Directory.CreateDirectory(_personasPath);
    }

    public Task<IReadOnlyList<GameUserPersonaPromptInfo>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_personasPath))
        {
            return Task.FromResult<IReadOnlyList<GameUserPersonaPromptInfo>>([]);
        }

        var prompts = Directory.GetFiles(_personasPath, "*.md", SearchOption.TopDirectoryOnly)
            .Select(path =>
            {
                var contentLength = new FileInfo(path).Length;
                var name = Path.GetFileNameWithoutExtension(path);
                return new GameUserPersonaPromptInfo
                {
                    Name = name,
                    FileName = Path.GetFileName(path),
                    RelativePath = $"{ContentPaths.GamePersonas}/{Path.GetFileName(path)}",
                    Tokens = (int)(contentLength / 4),
                    Size = contentLength,
                };
            })
            .OrderBy(prompt => prompt.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return Task.FromResult<IReadOnlyList<GameUserPersonaPromptInfo>>(prompts);
    }

    public async Task<GameUserPersonaPromptDocument?> TryLoadAsync(string promptName, CancellationToken ct = default)
    {
        string path;
        try
        {
            path = ResolvePromptPath(promptName);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Invalid game persona prompt path: prompt={PromptName}", promptName);
            return null;
        }

        if (!File.Exists(path))
        {
            return null;
        }

        var content = await File.ReadAllTextAsync(path, ct);
        return ToDocument(promptName, path, content);
    }

    public async Task SaveAsync(string promptName, string content, CancellationToken ct = default)
    {
        var path = ResolvePromptPath(promptName);
        await _writer.WriteAsync(path, content, ct);
        _logger.LogInformation("Saved game persona prompt {PromptName} to {Path}", promptName, path);
    }

    public async Task<GameUserPersonaPromptDocument> CreateUniqueAsync(
        string basePromptName,
        string content,
        CancellationToken ct = default)
    {
        var normalizedBase = NormalizeSegment(basePromptName, nameof(basePromptName));
        for (var index = 0; index < 10_000; index++)
        {
            var candidate = index == 0 ? normalizedBase : $"{normalizedBase}-{index + 1}";
            var path = ResolvePromptPath(candidate);
            if (File.Exists(path))
            {
                continue;
            }

            await _writer.WriteAsync(path, content, ct);
            _logger.LogInformation("Created game persona prompt at {Path}", path);
            return ToDocument(candidate, path, content);
        }

        throw new InvalidOperationException("Could not create a unique game persona prompt.");
    }

    private GameUserPersonaPromptDocument ToDocument(string promptName, string path, string content) =>
        new()
        {
            Name = NormalizeSegment(promptName, nameof(promptName)),
            FileName = Path.GetFileName(path),
            RelativePath = $"{ContentPaths.GamePersonas}/{Path.GetFileName(path)}",
            Content = content,
        };

    private string ResolvePromptPath(string promptName)
    {
        var normalizedName = NormalizeSegment(promptName, nameof(promptName));
        return PathBoundaryGuard.ResolvePathOrThrow(_personasPath, normalizedName + ".md");
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
            throw new ArgumentException($"Invalid game persona prompt segment: {value}", parameterName);
        }

        return trimmed;
    }
}
