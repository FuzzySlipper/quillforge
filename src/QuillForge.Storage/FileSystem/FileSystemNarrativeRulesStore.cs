using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Loads narrative-rules templates from the file system.
/// </summary>
public sealed class FileSystemNarrativeRulesStore : INarrativeRulesStore
{
    private readonly string _rulesPath;
    private readonly ILogger<FileSystemNarrativeRulesStore> _logger;

    public FileSystemNarrativeRulesStore(
        string rulesPath,
        ILogger<FileSystemNarrativeRulesStore> logger)
    {
        _rulesPath = rulesPath;
        _logger = logger;
    }

    public async Task<string> LoadAsync(string rulesName, CancellationToken ct = default)
    {
        var path = FindRulesFile(rulesName);
        _logger.LogDebug("Loading narrative rules: {Path}", path);

        if (!File.Exists(path))
        {
            _logger.LogWarning("Narrative rules not found: {Name}, returning empty", rulesName);
            return "";
        }

        return await File.ReadAllTextAsync(path, ct);
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_rulesPath))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var rules = Directory.GetFiles(_rulesPath, "*.md")
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .OrderBy(n => n)
            .ToList();

        return Task.FromResult<IReadOnlyList<string>>(rules);
    }

    private string FindRulesFile(string rulesName)
    {
        var exact = Path.Combine(_rulesPath, rulesName);
        if (File.Exists(exact))
        {
            return exact;
        }

        return Path.Combine(_rulesPath, rulesName + ".md");
    }
}
