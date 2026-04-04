using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;
using QuillForge.Core.Utilities;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Loads conductor prompt profiles from the file system.
/// A profile can be either a single .md file or a directory containing
/// multiple .md files (concatenated in alphabetical order).
/// </summary>
public sealed class FileSystemConductorStore : IConductorStore
{
    private readonly string _conductorPath;
    private readonly ILogger<FileSystemConductorStore> _logger;

    public FileSystemConductorStore(
        string conductorPath,
        ILogger<FileSystemConductorStore> logger)
    {
        _conductorPath = conductorPath;
        _logger = logger;
    }

    public async Task<string> LoadAsync(string conductorName, int? maxTokens = null, CancellationToken ct = default)
    {
        _logger.LogDebug(
            "Loading conductor profile: {Name}, budget: {Budget} tokens",
            conductorName,
            maxTokens ?? -1);
        var dirPath = Path.Combine(_conductorPath, conductorName);
        if (Directory.Exists(dirPath))
        {
            if (maxTokens.HasValue)
            {
                return await LoadTieredAsync(dirPath, maxTokens.Value, ct);
            }

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

                _logger.LogDebug(
                    "Loaded conductor profile {Name} from {Count} files",
                    conductorName,
                    files.Count);
                return string.Join("\n\n", parts);
            }
        }

        var filePath = Path.Combine(_conductorPath, conductorName + ".md");
        if (File.Exists(filePath))
        {
            _logger.LogDebug("Loaded conductor profile {Name} from file", conductorName);
            return await File.ReadAllTextAsync(filePath, ct);
        }

        _logger.LogWarning("Conductor profile not found: {Name}, returning empty", conductorName);
        return "";
    }

    private async Task<string> LoadTieredAsync(string dirPath, int budget, CancellationToken ct)
    {
        var tiers = new[] { "core.md", "quirks.md", "references.md", "extended.md" };

        var parts = new List<string>();
        var totalTokens = 0;

        foreach (var tierFile in tiers)
        {
            var path = Path.Combine(dirPath, tierFile);
            if (!File.Exists(path)) continue;

            var content = await File.ReadAllTextAsync(path, ct);
            var tokens = TokenEstimator.Estimate(content);

            if (totalTokens + tokens > budget)
            {
                _logger.LogDebug(
                    "Conductor tier {Tier} skipped: would exceed budget ({Current} + {FileTokens} > {Budget})",
                    tierFile, totalTokens, tokens, budget);
                break;
            }

            parts.Add(content);
            totalTokens += tokens;
            _logger.LogDebug("Loaded conductor tier {Tier}: {Tokens} tokens (total: {Total}/{Budget})",
                tierFile, tokens, totalTokens, budget);
        }

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
                _logger.LogDebug("Conductor file {File} skipped: would exceed budget", fileName);
                continue;
            }

            parts.Add(content);
            totalTokens += tokens;
        }

        _logger.LogDebug("Loaded conductor profile with {Parts} parts, {Tokens}/{Budget} tokens", parts.Count, totalTokens, budget);
        return string.Join("\n\n", parts);
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        var conductors = new HashSet<string>();

        if (Directory.Exists(_conductorPath))
        {
            foreach (var dir in Directory.GetDirectories(_conductorPath))
            {
                conductors.Add(Path.GetFileName(dir));
            }

            foreach (var file in Directory.GetFiles(_conductorPath, "*.md"))
            {
                conductors.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        var sorted = conductors.OrderBy(n => n).ToList();
        return Task.FromResult<IReadOnlyList<string>>(sorted);
    }
}
