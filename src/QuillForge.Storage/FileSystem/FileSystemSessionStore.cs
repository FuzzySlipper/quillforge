using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Persists ConversationTree instances as JSON files with atomic writes.
/// Supports loading legacy flat-array session files from the Python version.
/// </summary>
public sealed class FileSystemSessionStore : ISessionStore
{
    private readonly string _sessionsPath;
    private readonly AtomicFileWriter _writer;
    private readonly ILogger<FileSystemSessionStore> _logger;
    private readonly ILoggerFactory _loggerFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public FileSystemSessionStore(
        string sessionsPath,
        AtomicFileWriter writer,
        ILogger<FileSystemSessionStore> logger,
        ILoggerFactory loggerFactory)
    {
        _sessionsPath = sessionsPath;
        _writer = writer;
        _logger = logger;
        _loggerFactory = loggerFactory;

        Directory.CreateDirectory(sessionsPath);
        writer.CleanupTempFiles(sessionsPath);
    }

    public async Task<ConversationTree> LoadAsync(Guid sessionId, CancellationToken ct = default)
    {
        var path = GetSessionPath(sessionId);
        _logger.LogDebug("Loading session {SessionId} from {Path}", sessionId, path);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Session not found: {sessionId}", path);
        }

        var json = await File.ReadAllTextAsync(path, ct);
        var dto = JsonSerializer.Deserialize<SessionDto>(json, JsonOptions)
            ?? throw new JsonException($"Failed to deserialize session {sessionId}");

        // Check if this is a legacy format
        if (dto.Format == "legacy" || dto.Nodes is null || dto.Nodes.Count == 0)
        {
            _logger.LogInformation("Detected legacy session format for {SessionId}, migrating", sessionId);
            return MigrateLegacySession(sessionId, dto);
        }

        return RehydrateTree(sessionId, dto);
    }

    public async Task SaveAsync(ConversationTree session, CancellationToken ct = default)
    {
        var dto = DehydrateTree(session);
        var json = JsonSerializer.Serialize(dto, JsonOptions);
        var path = GetSessionPath(session.SessionId);

        _logger.LogDebug("Saving session {SessionId} to {Path} ({Length} chars)", session.SessionId, path, json.Length);
        await _writer.WriteAsync(path, json, ct);
    }

    public Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct = default)
    {
        if (!Directory.Exists(_sessionsPath))
        {
            return Task.FromResult<IReadOnlyList<SessionSummary>>([]);
        }

        var summaries = new List<SessionSummary>();
        foreach (var file in Directory.GetFiles(_sessionsPath, "*.json"))
        {
            try
            {
                var json = File.ReadAllText(file);
                var dto = JsonSerializer.Deserialize<SessionDto>(json, JsonOptions);
                if (dto is null) continue;

                if (!Guid.TryParse(Path.GetFileNameWithoutExtension(file), out var id))
                    continue;

                summaries.Add(new SessionSummary
                {
                    Id = id,
                    Name = dto.Name ?? "Untitled",
                    CreatedAt = dto.CreatedAt ?? DateTimeOffset.MinValue,
                    UpdatedAt = dto.UpdatedAt ?? DateTimeOffset.MinValue,
                    MessageCount = dto.Nodes?.Count ?? dto.Messages?.Count ?? 0,
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read session summary from {Path}", file);
            }
        }

        var sorted = summaries.OrderByDescending(s => s.UpdatedAt).ToList();
        return Task.FromResult<IReadOnlyList<SessionSummary>>(sorted);
    }

    public Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
    {
        var path = GetSessionPath(sessionId);
        _logger.LogInformation("Deleting session {SessionId}", sessionId);

        if (File.Exists(path))
        {
            File.Delete(path);
        }

        return Task.CompletedTask;
    }

    private string GetSessionPath(Guid sessionId)
    {
        return Path.Combine(_sessionsPath, $"{sessionId}.json");
    }

    private ConversationTree RehydrateTree(Guid sessionId, SessionDto dto)
    {
        var nodes = new Dictionary<Guid, MessageNode>();
        foreach (var nodeDto in dto.Nodes!)
        {
            nodes[nodeDto.Id] = new MessageNode
            {
                Id = nodeDto.Id,
                ParentId = nodeDto.ParentId,
                Role = nodeDto.Role,
                Content = new MessageContent(nodeDto.Text ?? ""),
                CreatedAt = nodeDto.CreatedAt,
                ChildIds = nodeDto.ChildIds ?? [],
                Metadata = nodeDto.Metadata is not null ? new MessageMetadata
                {
                    Model = nodeDto.Metadata.Model,
                    InputTokens = nodeDto.Metadata.InputTokens,
                    OutputTokens = nodeDto.Metadata.OutputTokens,
                    StopReason = nodeDto.Metadata.StopReason,
                } : null,
            };
        }

        var tree = new ConversationTree(
            sessionId,
            dto.Name ?? "Untitled",
            dto.RootId ?? nodes.Values.First(n => n.ParentId is null).Id,
            dto.ActiveLeafId ?? nodes.Keys.Last(),
            nodes,
            _loggerFactory.CreateLogger<ConversationTree>());

        _logger.LogDebug("Rehydrated session {SessionId}: {Count} nodes", sessionId, nodes.Count);
        return tree;
    }

    private static SessionDto DehydrateTree(ConversationTree tree)
    {
        var snapshot = tree.GetSnapshot();
        var nodes = snapshot.Values.Select(n => new NodeDto
        {
            Id = n.Id,
            ParentId = n.ParentId,
            Role = n.Role,
            Text = n.Content.GetText(),
            CreatedAt = n.CreatedAt,
            ChildIds = n.ChildIds.ToList(),
            Metadata = n.Metadata is not null ? new MetadataDto
            {
                Model = n.Metadata.Model,
                InputTokens = n.Metadata.InputTokens,
                OutputTokens = n.Metadata.OutputTokens,
                StopReason = n.Metadata.StopReason,
            } : null,
        }).ToList();

        return new SessionDto
        {
            Format = "tree",
            Name = tree.Name,
            RootId = tree.RootId,
            ActiveLeafId = tree.ActiveLeafId,
            CreatedAt = snapshot.Values.Min(n => n.CreatedAt),
            UpdatedAt = snapshot.Values.Max(n => n.CreatedAt),
            Nodes = nodes,
        };
    }

    /// <summary>
    /// Migrates a legacy flat-array session to tree format.
    /// Each message gets a GUID, linked as a linear parent→child chain.
    /// </summary>
    private ConversationTree MigrateLegacySession(Guid sessionId, SessionDto dto)
    {
        var tree = new ConversationTree(sessionId, dto.Name ?? "Migrated Session",
            _loggerFactory.CreateLogger<ConversationTree>());

        if (dto.Messages is not null)
        {
            var parentId = tree.RootId;
            foreach (var msg in dto.Messages)
            {
                var role = msg.Role ?? "user";
                var text = msg.Content ?? msg.Text ?? "";
                var node = tree.Append(parentId, role, new MessageContent(text));
                parentId = node.Id;
            }
        }

        _logger.LogInformation(
            "Migrated legacy session {SessionId}: {Count} messages → tree",
            sessionId, dto.Messages?.Count ?? 0);

        return tree;
    }
}

// ---- DTOs for JSON serialization ----

internal sealed class SessionDto
{
    public string? Format { get; set; }
    public string? Name { get; set; }
    public Guid? RootId { get; set; }
    public Guid? ActiveLeafId { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public List<NodeDto>? Nodes { get; set; }

    // Legacy format fields
    public List<LegacyMessageDto>? Messages { get; set; }
}

internal sealed class NodeDto
{
    public Guid Id { get; set; }
    public Guid? ParentId { get; set; }
    public string Role { get; set; } = "";
    public string? Text { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public List<Guid>? ChildIds { get; set; }
    public MetadataDto? Metadata { get; set; }
}

internal sealed class MetadataDto
{
    public string? Model { get; set; }
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }
    public string? StopReason { get; set; }
}

internal sealed class LegacyMessageDto
{
    public string? Role { get; set; }
    public string? Content { get; set; }
    public string? Text { get; set; }
}
