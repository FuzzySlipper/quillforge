using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;
using QuillForge.Storage.Utilities;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Manages .state.yaml companion files for tracking plot threads, character conditions,
/// tension levels, and event counters across sessions.
/// Uses atomic writes for safety.
/// </summary>
public sealed class FileSystemStoryStateService : IStoryStateService
{
    private readonly string _basePath;
    private readonly AtomicFileWriter _writer;
    private readonly ILogger<FileSystemStoryStateService> _logger;

    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    private static readonly ISerializer Serializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public FileSystemStoryStateService(
        string basePath,
        AtomicFileWriter writer,
        ILogger<FileSystemStoryStateService> logger)
    {
        _basePath = basePath;
        _writer = writer;
        _logger = logger;
    }

    public Task<IReadOnlyDictionary<string, object>> LoadAsync(string stateFilePath, CancellationToken ct = default)
    {
        var fullPath = ResolvePath(stateFilePath);
        _logger.LogDebug("Loading story state from {Path}", fullPath);

        if (!File.Exists(fullPath))
        {
            _logger.LogDebug("State file not found: {Path}, returning empty", fullPath);
            return Task.FromResult<IReadOnlyDictionary<string, object>>(
                new Dictionary<string, object>());
        }

        var yaml = File.ReadAllText(fullPath);
        var state = Deserializer.Deserialize<Dictionary<string, object>>(yaml)
            ?? new Dictionary<string, object>();

        _logger.LogDebug("Loaded story state: {KeyCount} keys", state.Count);
        return Task.FromResult<IReadOnlyDictionary<string, object>>(state);
    }

    public async Task SaveAsync(
        string stateFilePath,
        IReadOnlyDictionary<string, object> state,
        CancellationToken ct = default)
    {
        var fullPath = ResolvePath(stateFilePath);
        var yaml = Serializer.Serialize(new Dictionary<string, object>(state));

        _logger.LogDebug("Saving story state to {Path} ({KeyCount} keys)", fullPath, state.Count);
        await _writer.WriteAsync(fullPath, yaml, ct);
    }

    public async Task<IReadOnlyDictionary<string, object>> MergeAsync(
        string stateFilePath,
        IReadOnlyDictionary<string, object> updates,
        CancellationToken ct = default)
    {
        var existing = await LoadAsync(stateFilePath, ct);
        var merged = new Dictionary<string, object>(existing);

        foreach (var (key, value) in updates)
        {
            merged[key] = value;
        }

        _logger.LogDebug("Merging {UpdateCount} keys into state at {Path}", updates.Count, stateFilePath);
        await SaveAsync(stateFilePath, merged, ct);
        return merged;
    }

    public async Task IncrementCounterAsync(
        string stateFilePath, string counterKey, CancellationToken ct = default)
    {
        var existing = await LoadAsync(stateFilePath, ct);
        var merged = new Dictionary<string, object>(existing);

        var currentValue = 0;
        if (merged.TryGetValue(counterKey, out var val))
        {
            currentValue = val switch
            {
                int i => i,
                long l => (int)l,
                string s when int.TryParse(s, out var parsed) => parsed,
                _ => 0,
            };
        }

        merged[counterKey] = currentValue + 1;

        _logger.LogDebug("Incremented {Key}: {Old} → {New}", counterKey, currentValue, currentValue + 1);
        await SaveAsync(stateFilePath, merged, ct);
    }

    public async Task RemoveKeyAsync(
        string stateFilePath, string key, CancellationToken ct = default)
    {
        var existing = await LoadAsync(stateFilePath, ct);
        var merged = new Dictionary<string, object>(existing);

        if (merged.Remove(key))
        {
            _logger.LogDebug("Removed key {Key} from state at {Path}", key, stateFilePath);
            await SaveAsync(stateFilePath, merged, ct);
        }
    }

    private string ResolvePath(string stateFilePath)
    {
        var resolved = Path.GetFullPath(Path.Combine(_basePath, stateFilePath));
        if (!resolved.StartsWith(_basePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Path traversal detected: {stateFilePath}");
        }
        return resolved;
    }
}
