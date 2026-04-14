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
        var transcript = BuildRoleplayTranscript(tree.ToFlatThread());

        await _storyStore.WriteAsync(projectName, fileName, transcript, ct);

        _logger.LogInformation(
            "Roleplay transcript synced: session={SessionId} project={Project} file={File} turnCount={TurnCount} contentLength={Length}",
            sessionId,
            projectName,
            fileName,
            CountRoleplayTurns(tree.ToFlatThread()),
            transcript.Length);
    }

    private static string BuildRoleplayTranscript(IReadOnlyList<MessageNode> thread)
    {
        var turns = new List<string>();

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

            var content = node.Content.GetText().ReplaceLineEndings("\n").Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            turns.Add(content);
        }

        return string.Join("\n\n", turns);
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
}
