using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;
using QuillForge.Core.Utilities;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Loads persona/character definitions from the file system.
/// A persona can be either a single .md file or a directory containing multiple .md files
/// (which are concatenated in alphabetical order).
/// </summary>
public sealed class FileSystemPersonaStore : IPersonaStore
{
    private readonly string _personaPath;
    private readonly ILogger<FileSystemPersonaStore> _logger;

    public FileSystemPersonaStore(string personaPath, ILogger<FileSystemPersonaStore> logger)
    {
        _personaPath = personaPath;
        _logger = logger;
    }

    public async Task<string> LoadAsync(string personaName, int? maxTokens = null, CancellationToken ct = default)
    {
        _logger.LogDebug("Loading persona: {Name}, budget: {Budget} tokens", personaName, maxTokens ?? -1);

        var dirPath = Path.Combine(_personaPath, personaName);
        if (Directory.Exists(dirPath))
        {
            // If budget is set, load files in tier order with budget enforcement
            if (maxTokens.HasValue)
            {
                return await LoadTieredAsync(dirPath, maxTokens.Value, ct);
            }

            // No budget — load all files alphabetically (existing behavior)
            var files = Directory.GetFiles(dirPath, "*.md", SearchOption.TopDirectoryOnly)
                .OrderBy(f => f)
                .ToList();

            if (files.Count > 0)
            {
                var parts = new List<string>();
                foreach (var file in files)
                {
                    parts.Add(await File.ReadAllTextAsync(file, ct));
                }
                _logger.LogDebug("Loaded persona {Name} from {Count} files in directory", personaName, files.Count);
                return string.Join("\n\n", parts);
            }
        }

        // Fall back to single file
        var filePath = Path.Combine(_personaPath, personaName + ".md");
        if (File.Exists(filePath))
        {
            return await File.ReadAllTextAsync(filePath, ct);
        }

        _logger.LogWarning("Persona not found: {Name}, returning empty", personaName);
        return "";
    }

    private async Task<string> LoadTieredAsync(string dirPath, int budget, CancellationToken ct)
    {
        // Priority tiers — loaded in order, stop when budget exceeded
        var tiers = new[] { "core.md", "quirks.md", "references.md", "extended.md" };

        var parts = new List<string>();
        var totalTokens = 0;

        // First pass: load tiered files in priority order
        foreach (var tierFile in tiers)
        {
            var path = Path.Combine(dirPath, tierFile);
            if (!File.Exists(path)) continue;

            var content = await File.ReadAllTextAsync(path, ct);
            var tokens = TokenEstimator.Estimate(content);

            if (totalTokens + tokens > budget)
            {
                _logger.LogDebug(
                    "Persona tier {Tier} skipped: would exceed budget ({Current} + {FileTokens} > {Budget})",
                    tierFile, totalTokens, tokens, budget);
                break;
            }

            parts.Add(content);
            totalTokens += tokens;
            _logger.LogDebug("Loaded persona tier {Tier}: {Tokens} tokens (total: {Total}/{Budget})",
                tierFile, tokens, totalTokens, budget);
        }

        // Second pass: load any non-tier files alphabetically if budget remains
        var allFiles = Directory.GetFiles(dirPath, "*.md", SearchOption.TopDirectoryOnly)
            .OrderBy(f => f);
        var tierSet = new HashSet<string>(tiers, StringComparer.OrdinalIgnoreCase);

        foreach (var file in allFiles)
        {
            var fileName = Path.GetFileName(file);
            if (tierSet.Contains(fileName)) continue;

            var content = await File.ReadAllTextAsync(file, ct);
            var tokens = TokenEstimator.Estimate(content);

            if (totalTokens + tokens > budget)
            {
                _logger.LogDebug("Persona file {File} skipped: would exceed budget", fileName);
                continue;
            }

            parts.Add(content);
            totalTokens += tokens;
        }

        _logger.LogDebug("Loaded persona with {Parts} parts, {Tokens}/{Budget} tokens", parts.Count, totalTokens, budget);
        return string.Join("\n\n", parts);
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_personaPath))
        {
            return Task.FromResult<IReadOnlyList<string>>([]);
        }

        var personas = new HashSet<string>();

        // Add directory-based personas
        foreach (var dir in Directory.GetDirectories(_personaPath))
        {
            personas.Add(Path.GetFileName(dir));
        }

        // Add single-file personas
        foreach (var file in Directory.GetFiles(_personaPath, "*.md"))
        {
            personas.Add(Path.GetFileNameWithoutExtension(file));
        }

        var sorted = personas.OrderBy(n => n).ToList();
        return Task.FromResult<IReadOnlyList<string>>(sorted);
    }
}
