using System.Text;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class SessionTranscriptService : ISessionTranscriptService
{
    private const string OperationName = "sync_roleplay_transcript";

    private readonly ISessionStore _sessionStore;
    private readonly ISessionStateStore _runtimeStore;
    private readonly ISessionMutationGate _gate;
    private readonly IStoryStore _storyStore;
    private readonly ILogger<SessionTranscriptService> _logger;

    public SessionTranscriptService(
        ISessionStore sessionStore,
        ISessionStateStore runtimeStore,
        ISessionMutationGate gate,
        IStoryStore storyStore,
        ILogger<SessionTranscriptService> logger)
    {
        _sessionStore = sessionStore;
        _runtimeStore = runtimeStore;
        _gate = gate;
        _storyStore = storyStore;
        _logger = logger;
    }

    public async Task SyncRoleplayTranscriptAsync(Guid sessionId, CancellationToken ct = default)
    {
        await using var lease = await _gate.TryAcquireAsync(sessionId, OperationName, ct);
        if (lease is null)
        {
            _logger.LogWarning(
                "Roleplay transcript sync skipped because the session was busy: session={SessionId}",
                sessionId);
            return;
        }

        var state = await _runtimeStore.LoadAsync(sessionId, ct);
        if (state.Mode.ActiveMode != Mode.Roleplay)
        {
            _logger.LogDebug(
                "Roleplay transcript sync skipped outside roleplay mode: session={SessionId} mode={Mode}",
                sessionId,
                state.Mode.ActiveMode);
            return;
        }

        var projectName = NormalizeChoice(state.Mode.ProjectName);
        var fileName = NormalizeChoice(state.Mode.CurrentFile);
        if (projectName is null || fileName is null)
        {
            _logger.LogWarning(
                "Roleplay transcript sync skipped because the target was incomplete: session={SessionId} project={Project} file={File}",
                sessionId,
                state.Mode.ProjectName,
                state.Mode.CurrentFile);
            return;
        }

        var tree = await _sessionStore.LoadAsync(sessionId, ct);
        var transcript = BuildRoleplayTranscript(
            sessionId,
            projectName,
            fileName,
            state.Mode.Character,
            tree.ToFlatThread());

        await _storyStore.WriteAsync(projectName, fileName, transcript, ct);

        _logger.LogInformation(
            "Roleplay transcript synced: session={SessionId} project={Project} file={File} turnCount={TurnCount} contentLength={Length}",
            sessionId,
            projectName,
            fileName,
            CountRoleplayTurns(tree.ToFlatThread()),
            transcript.Length);
    }

    private static string BuildRoleplayTranscript(
        Guid sessionId,
        string projectName,
        string fileName,
        string? characterName,
        IReadOnlyList<MessageNode> thread)
    {
        var assistantLabel = BuildAssistantLabel(characterName);
        var targetPath = $"story/{projectName}/{fileName.Replace('\\', '/')}";
        var entries = BuildTranscriptEntries(thread, assistantLabel);
        var builder = new StringBuilder();

        builder.AppendLine($"<!-- quillforge:roleplay-transcript session={sessionId} -->");
        builder.AppendLine("# Roleplay Transcript");
        builder.AppendLine();
        builder.AppendLine("> This file is app-managed by QuillForge.");
        builder.AppendLine("> It is regenerated from the active roleplay conversation branch on every sync.");
        builder.AppendLine("> Manual edits here will be overwritten. Put notes in a separate story file.");
        builder.AppendLine();
        builder.AppendLine($"Session: `{sessionId}`");
        builder.AppendLine($"Target: `{targetPath}`");

        if (!string.Equals(assistantLabel, "Assistant", StringComparison.Ordinal))
        {
            builder.AppendLine($"Character: `{assistantLabel}`");
        }

        builder.AppendLine();

        if (entries.Count == 0)
        {
            builder.AppendLine("_No roleplay turns have been synced yet._");
            return builder.ToString().TrimEnd();
        }

        foreach (var entry in entries)
        {
            builder.AppendLine($"## Turn {entry.TurnNumber} - {entry.Label}");
            builder.AppendLine();
            builder.AppendLine(entry.Content);
            builder.AppendLine();
        }

        return builder.ToString().TrimEnd();
    }

    private static List<TranscriptEntry> BuildTranscriptEntries(
        IReadOnlyList<MessageNode> thread,
        string assistantLabel)
    {
        var nodesById = new Dictionary<Guid, MessageNode>(thread.Count);
        foreach (var node in thread)
        {
            nodesById[node.Id] = node;
        }

        var includedUserNodes = new HashSet<Guid>();
        var entries = new List<TranscriptEntry>();
        var turnNumber = 0;

        foreach (var node in thread)
        {
            if (!string.Equals(node.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (node.Metadata?.ConversationMode != Mode.Roleplay)
            {
                continue;
            }

            var assistantContent = NormalizeTurnContent(node.Content.GetText());
            if (assistantContent is null)
            {
                continue;
            }

            turnNumber++;

            if (node.ParentId is Guid parentId
                && nodesById.TryGetValue(parentId, out var parentNode)
                && string.Equals(parentNode.Role, "user", StringComparison.OrdinalIgnoreCase)
                && includedUserNodes.Add(parentNode.Id))
            {
                var userContent = NormalizeTurnContent(parentNode.Content.GetText());
                if (userContent is not null)
                {
                    entries.Add(new TranscriptEntry(turnNumber, "User", userContent));
                }
            }

            entries.Add(new TranscriptEntry(turnNumber, assistantLabel, assistantContent));
        }

        return entries;
    }

    private static string BuildAssistantLabel(string? characterName)
    {
        if (string.IsNullOrWhiteSpace(characterName))
        {
            return "Assistant";
        }

        var fileName = Path.GetFileNameWithoutExtension(characterName.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return "Assistant";
        }

        var normalized = fileName
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Trim();

        if (normalized.Length == 0)
        {
            return "Assistant";
        }

        var words = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[i];
            if (word.Length == 1)
            {
                words[i] = word.ToUpperInvariant();
                continue;
            }

            words[i] = char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant();
        }

        return string.Join(' ', words);
    }

    private static string? NormalizeTurnContent(string content)
    {
        var normalized = content.ReplaceLineEndings("\n").Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static int CountRoleplayTurns(IReadOnlyList<MessageNode> thread)
    {
        var count = 0;

        foreach (var node in thread)
        {
            if (!string.Equals(node.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (node.Metadata?.ConversationMode == Mode.Roleplay)
            {
                count++;
            }
        }

        return count;
    }

    private static string? NormalizeChoice(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private sealed record TranscriptEntry(int TurnNumber, string Label, string Content);
}
