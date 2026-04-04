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
/// SessionId == null is a transient pre-session view and is not persisted.
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
        if (!sessionId.HasValue)
        {
            _logger.LogDebug("Loading transient pre-session runtime view");
            return Task.FromResult(new SessionRuntimeState());
        }

        var path = GetPath(sessionId.Value);

        if (!File.Exists(path))
        {
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
        if (!state.SessionId.HasValue)
        {
            throw new InvalidOperationException(
                "Cannot persist runtime state without a session ID. Create a real session before mutating runtime state.");
        }

        state.LastModified = DateTimeOffset.UtcNow;
        var path = GetPath(state.SessionId.Value);
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

    public Task<IReadOnlyList<Guid>> FindSessionIdsByProfileIdAsync(string profileId, CancellationToken ct = default)
    {
        var matches = new List<Guid>();
        foreach (var path in Directory.GetFiles(_basePath, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                var json = File.ReadAllText(path);
                var state = JsonSerializer.Deserialize<SessionRuntimeState>(json, JsonOptions);
                if (state?.SessionId is not Guid sessionId)
                {
                    continue;
                }

                if (string.Equals(state.Profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
                {
                    matches.Add(sessionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to inspect session runtime state while checking profile references: path={Path}",
                    path);
            }
        }

        _logger.LogDebug(
            "Found {Count} persisted sessions referencing profile {ProfileId}",
            matches.Count,
            profileId);

        return Task.FromResult<IReadOnlyList<Guid>>(matches);
    }

    private string GetPath(Guid sessionId)
    {
        return Path.Combine(_basePath, $"{sessionId}.json");
    }
}
