using Microsoft.Extensions.Logging;
using QuillForge.Core;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.Utilities;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Persists one YAML file per reusable profile under build/profiles.
/// </summary>
public sealed class FileSystemProfileConfigStore : IProfileConfigStore
{
    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private readonly string _profilesPath;
    private readonly AtomicFileWriter _writer;
    private readonly ILogger<FileSystemProfileConfigStore> _logger;

    public FileSystemProfileConfigStore(
        string contentRoot,
        AtomicFileWriter writer,
        ILogger<FileSystemProfileConfigStore> logger)
    {
        _profilesPath = Path.Combine(contentRoot, ContentPaths.Profiles);
        _writer = writer;
        _logger = logger;
        Directory.CreateDirectory(_profilesPath);
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        var profiles = Directory.GetFiles(_profilesPath, "*.yaml", SearchOption.TopDirectoryOnly)
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation("Listed {Count} profile config files from {Path}", profiles.Count, _profilesPath);
        return Task.FromResult<IReadOnlyList<string>>(profiles);
    }

    public Task<bool> ExistsAsync(string profileId, CancellationToken ct = default)
    {
        var path = GetProfilePath(profileId);
        return Task.FromResult(File.Exists(path));
    }

    public async Task<ProfileConfig> LoadAsync(string profileId, CancellationToken ct = default)
    {
        var path = GetProfilePath(profileId);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Profile config {profileId} not found", path);
        }

        var yaml = await File.ReadAllTextAsync(path, ct);
        var config = YamlDeserializer.Deserialize<ProfileConfig>(yaml) ?? new ProfileConfig();

        _logger.LogInformation(
            "Loaded profile config {ProfileId} from {Path}: conductor={Conductor} lore={LoreSet} narrativeRules={NarrativeRules} writingStyle={WritingStyle}",
            profileId,
            path,
            config.Conductor,
            config.LoreSet,
            config.NarrativeRules,
            config.WritingStyle);

        return config;
    }

    public async Task SaveAsync(string profileId, ProfileConfig config, CancellationToken ct = default)
    {
        var path = GetProfilePath(profileId);
        var yaml = YamlSerializer.Serialize(config);
        await _writer.WriteAsync(path, yaml, ct);

        _logger.LogInformation(
            "Saved profile config {ProfileId} to {Path}: conductor={Conductor} lore={LoreSet} narrativeRules={NarrativeRules} writingStyle={WritingStyle}",
            profileId,
            path,
            config.Conductor,
            config.LoreSet,
            config.NarrativeRules,
            config.WritingStyle);
    }

    public Task DeleteAsync(string profileId, CancellationToken ct = default)
    {
        var path = GetProfilePath(profileId);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogInformation("Deleted profile config {ProfileId} at {Path}", profileId, path);
        }

        return Task.CompletedTask;
    }

    private string GetProfilePath(string profileId)
    {
        var normalized = NormalizeProfileId(profileId);
        return Path.Combine(_profilesPath, $"{normalized}.yaml");
    }

    private static string NormalizeProfileId(string profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
        {
            throw new ArgumentException("Profile id is required.", nameof(profileId));
        }

        var trimmed = profileId.Trim();
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || trimmed.Contains(Path.DirectorySeparatorChar)
            || trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            throw new ArgumentException($"Invalid profile id: {profileId}", nameof(profileId));
        }

        return trimmed;
    }
}
