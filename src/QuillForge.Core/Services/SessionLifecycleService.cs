using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class SessionLifecycleService : ISessionLifecycleService
{
    private readonly ISessionStore _sessionStore;
    private readonly ISessionRuntimeStore _runtimeStore;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SessionLifecycleService> _logger;

    public SessionLifecycleService(
        ISessionStore sessionStore,
        ISessionRuntimeStore runtimeStore,
        ILoggerFactory loggerFactory,
        ILogger<SessionLifecycleService> logger)
    {
        _sessionStore = sessionStore;
        _runtimeStore = runtimeStore;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public async Task<ConversationTree> ForkAsync(Guid sourceSessionId, Guid? messageId = null, CancellationToken ct = default)
    {
        var sourceTree = await _sessionStore.LoadAsync(sourceSessionId, ct);
        var sourceRuntime = await _runtimeStore.LoadAsync(sourceSessionId, ct);

        var thread = messageId.HasValue
            ? sourceTree.GetThread(messageId.Value)
            : sourceTree.GetThread();

        var forkedTree = new ConversationTree(
            Guid.CreateVersion7(),
            $"Fork of {sourceTree.Name}",
            _loggerFactory.CreateLogger<ConversationTree>());

        foreach (var node in thread.Skip(1))
        {
            forkedTree.Append(forkedTree.ActiveLeafId, node.Role, node.Content, node.Metadata);
        }

        var forkedRuntime = CloneRuntimeStateForFork(sourceRuntime, forkedTree.SessionId);

        await _sessionStore.SaveAsync(forkedTree, ct);
        await _runtimeStore.SaveAsync(forkedRuntime, ct);

        _logger.LogInformation(
            "Forked session {SourceSessionId} into {ForkedSessionId} at message {MessageId}",
            sourceSessionId,
            forkedTree.SessionId,
            messageId);

        return forkedTree;
    }

    public async Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
    {
        await _sessionStore.DeleteAsync(sessionId, ct);
        await _runtimeStore.DeleteAsync(sessionId, ct);

        _logger.LogInformation("Deleted session unit {SessionId}", sessionId);
    }

    private static SessionRuntimeState CloneRuntimeStateForFork(SessionRuntimeState source, Guid forkedSessionId)
    {
        return new SessionRuntimeState
        {
            SessionId = forkedSessionId,
            Mode = new ModeSelectionState
            {
                ActiveModeName = source.Mode.ActiveModeName,
                ProjectName = source.Mode.ProjectName,
                CurrentFile = source.Mode.CurrentFile,
                Character = source.Mode.Character,
            },
            Profile = new ProfileState
            {
                ProfileId = source.Profile.ProfileId,
                ActivePersona = source.Profile.ActivePersona,
                ActiveLoreSet = source.Profile.ActiveLoreSet,
                ActiveNarrativeRules = source.Profile.ActiveNarrativeRules,
                ActiveWritingStyle = source.Profile.ActiveWritingStyle,
            },
            Writer = new WriterRuntimeState
            {
                PendingContent = null,
                State = WriterState.Idle,
            },
            Narrative = new NarrativeRuntimeState
            {
                DirectorNotes = source.Narrative.DirectorNotes,
                ActivePlotFile = source.Narrative.ActivePlotFile,
                PlotProgress = new PlotProgressState
                {
                    CurrentBeat = source.Narrative.PlotProgress.CurrentBeat,
                    CompletedBeats = [.. source.Narrative.PlotProgress.CompletedBeats],
                    Deviations = [.. source.Narrative.PlotProgress.Deviations],
                },
            },
        };
    }
}
