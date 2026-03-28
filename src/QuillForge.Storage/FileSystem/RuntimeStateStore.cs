using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Persists lightweight runtime state (last mode, last session, etc.)
/// to a small JSON file. Separate from config.yaml since this changes frequently.
/// </summary>
public sealed class RuntimeStateStore
{
    private readonly string _statePath;
    private readonly AtomicFileWriter _writer;
    private readonly ILogger<RuntimeStateStore> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public RuntimeStateStore(string contentRoot, AtomicFileWriter writer, ILogger<RuntimeStateStore> logger)
    {
        _statePath = Path.Combine(contentRoot, "data", "runtime-state.json");
        _writer = writer;
        _logger = logger;
    }

    public RuntimeState Load()
    {
        if (!File.Exists(_statePath))
        {
            return new RuntimeState();
        }

        try
        {
            var json = File.ReadAllText(_statePath);
            return JsonSerializer.Deserialize<RuntimeState>(json, JsonOptions) ?? new RuntimeState();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load runtime state, using defaults");
            return new RuntimeState();
        }
    }

    public async Task SaveAsync(RuntimeState state, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await _writer.WriteAsync(_statePath, json, ct);
        _logger.LogDebug("Saved runtime state: mode={Mode}", state.LastMode);
    }
}

public sealed class RuntimeState
{
    public string? LastMode { get; set; }
    public string? LastProject { get; set; }
    public string? LastFile { get; set; }
    public string? LastCharacter { get; set; }
    public Guid? LastSessionId { get; set; }
}
