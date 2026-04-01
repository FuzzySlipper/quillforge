using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using QuillForge.Core;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Persists SessionRuntimeState as JSON files, one per session.
/// The default/global state is stored as "default.json".
/// </summary>
public sealed class FileSystemSessionRuntimeStore : ISessionRuntimeStore
{
    private readonly string _basePath;
    private readonly AtomicFileWriter _writer;
    private readonly ILogger<FileSystemSessionRuntimeStore> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public FileSystemSessionRuntimeStore(
        string contentRoot,
        AtomicFileWriter writer,
        ILogger<FileSystemSessionRuntimeStore> logger)
    {
        _basePath = Path.Combine(contentRoot, ContentPaths.DataSessionState);
        _writer = writer;
        _logger = logger;
        Directory.CreateDirectory(_basePath);
    }

    public Task<SessionRuntimeState> LoadAsync(Guid? sessionId, CancellationToken ct = default)
    {
        var path = GetPath(sessionId);

        if (!File.Exists(path))
        {
            // For session-specific requests where no state has been saved yet,
            // inherit from the default/global state so new sessions pick up the
            // current mode, profile, etc. instead of reverting to "general".
            if (sessionId.HasValue)
            {
                var defaultPath = GetPath(null);
                if (File.Exists(defaultPath))
                {
                    try
                    {
                        var defaultJson = File.ReadAllText(defaultPath);
                        var inherited = JsonSerializer.Deserialize<SessionRuntimeState>(defaultJson, JsonOptions)
                            ?? new SessionRuntimeState();
                        inherited.SessionId = sessionId;
                        _logger.LogDebug(
                            "Session {SessionId} inheriting default runtime state (mode={Mode})",
                            sessionId, inherited.Mode.ActiveModeName);
                        return Task.FromResult(inherited);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to load default state for inheritance");
                    }
                }
            }

            _logger.LogDebug("No persisted runtime state for session {SessionId}, returning defaults", sessionId);
            return Task.FromResult(new SessionRuntimeState { SessionId = sessionId });
        }

        try
        {
            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<SessionRuntimeState>(json, JsonOptions)
                ?? new SessionRuntimeState { SessionId = sessionId };
            state.SessionId = sessionId;
            _logger.LogDebug("Loaded runtime state for session {SessionId}", sessionId);
            return Task.FromResult(state);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load runtime state for session {SessionId}, returning defaults", sessionId);
            return Task.FromResult(new SessionRuntimeState { SessionId = sessionId });
        }
    }

    public async Task SaveAsync(SessionRuntimeState state, CancellationToken ct = default)
    {
        state.LastModified = DateTimeOffset.UtcNow;
        var path = GetPath(state.SessionId);
        var json = JsonSerializer.Serialize(state, JsonOptions);
        await _writer.WriteAsync(path, json, ct);
        _logger.LogDebug("Saved runtime state for session {SessionId}", state.SessionId);
    }

    public Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
    {
        var path = GetPath(sessionId);
        if (File.Exists(path))
        {
            File.Delete(path);
            _logger.LogDebug("Deleted runtime state for session {SessionId}", sessionId);
        }
        return Task.CompletedTask;
    }

    private string GetPath(Guid? sessionId)
    {
        var fileName = sessionId.HasValue ? $"{sessionId.Value}.json" : "default.json";
        return Path.Combine(_basePath, fileName);
    }
}
