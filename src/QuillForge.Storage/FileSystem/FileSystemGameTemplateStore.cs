using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using QuillForge.Core;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Persists one JSON file per reusable game template under user/game-templates.
/// </summary>
public sealed class FileSystemGameTemplateStore : IGameTemplateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _templatesPath;
    private readonly AtomicFileWriter _writer;
    private readonly ILogger<FileSystemGameTemplateStore> _logger;

    public FileSystemGameTemplateStore(
        string contentRoot,
        AtomicFileWriter writer,
        ILogger<FileSystemGameTemplateStore> logger)
    {
        _templatesPath = Path.Combine(contentRoot, ContentPaths.GameTemplates);
        _writer = writer;
        _logger = logger;
        Directory.CreateDirectory(_templatesPath);
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        var templates = Directory.GetFiles(_templatesPath, "*.json", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return Task.FromResult<IReadOnlyList<string>>(templates);
    }

    public Task<bool> ExistsAsync(string templateId, CancellationToken ct = default)
    {
        var path = GetTemplatePath(templateId);
        return Task.FromResult(File.Exists(path));
    }

    public async Task<GameTemplate> LoadAsync(string templateId, CancellationToken ct = default)
    {
        var path = GetTemplatePath(templateId);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Game template {templateId} not found", path);
        }

        var json = await File.ReadAllTextAsync(path, ct);
        var template = JsonSerializer.Deserialize<GameTemplate>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Game template {templateId} could not be deserialized.");

        _logger.LogInformation("Loaded game template {TemplateId} from {Path}", templateId, path);
        return template;
    }

    public async Task SaveAsync(string templateId, GameTemplate template, CancellationToken ct = default)
    {
        var path = GetTemplatePath(templateId);
        var json = JsonSerializer.Serialize(template, JsonOptions);
        await _writer.WriteAsync(path, json, ct);
        _logger.LogInformation("Saved game template {TemplateId} to {Path}", templateId, path);
    }

    public Task DeleteAsync(string templateId, CancellationToken ct = default)
    {
        var path = GetTemplatePath(templateId);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogInformation("Deleted game template {TemplateId} at {Path}", templateId, path);
        }

        return Task.CompletedTask;
    }

    private string GetTemplatePath(string templateId)
    {
        var normalized = NormalizeTemplateId(templateId);
        return Path.Combine(_templatesPath, $"{normalized}.json");
    }

    private static string NormalizeTemplateId(string templateId)
    {
        if (string.IsNullOrWhiteSpace(templateId))
        {
            throw new ArgumentException("Template id is required.", nameof(templateId));
        }

        var trimmed = templateId.Trim();
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || trimmed.Contains(Path.DirectorySeparatorChar)
            || trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException($"Invalid game template id: {templateId}", nameof(templateId));
        }

        return trimmed;
    }
}
