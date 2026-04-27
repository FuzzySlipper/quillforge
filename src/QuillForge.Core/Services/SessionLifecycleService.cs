using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class SessionLifecycleService : ISessionLifecycleService
{
    private readonly ISessionStore _sessionStore;
    private readonly ISessionStateStore _runtimeStore;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<SessionLifecycleService> _logger;

    public SessionLifecycleService(
        ISessionStore sessionStore,
        ISessionStateStore runtimeStore,
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

    private static SessionState CloneRuntimeStateForFork(SessionState source, Guid forkedSessionId)
    {
        var (forkedProjectName, forkedFileName) = ResolveForkedModeTarget(source.Mode, forkedSessionId);

        return new SessionState
        {
            SessionId = forkedSessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = source.Mode.ActiveMode,
                ProjectName = forkedProjectName,
                CurrentFile = forkedFileName,
                Character = source.Mode.Character,
            },
            Profile = new ProfileState
            {
                ProfileId = source.Profile.ProfileId,
                ActiveLoreSet = source.Profile.ActiveLoreSet,
                ActiveNarrativeRules = source.Profile.ActiveNarrativeRules,
                ActiveWritingStyle = source.Profile.ActiveWritingStyle,
                ActiveLibrarianPrompt = source.Profile.ActiveLibrarianPrompt,
            },
            Roleplay = new RoleplayRuntimeState
            {
                HasExplicitAiCharacterSelection = source.Roleplay.HasExplicitAiCharacterSelection,
                ActiveAiCharacter = source.Roleplay.ActiveAiCharacter,
                HasExplicitUserCharacterSelection = source.Roleplay.HasExplicitUserCharacterSelection,
                ActiveUserCharacter = source.Roleplay.ActiveUserCharacter,
            },
            Writer = new WriterRuntimeState
            {
                PendingContent = null,
                PendingProjectName = null,
                PendingFileName = null,
                State = WriterState.Idle,
            },
            Narrative = new NarrativeRuntimeState
            {
                DirectorNotes = source.Narrative.DirectorNotes,
                StickySessionCanon = source.Narrative.StickySessionCanon,
                ActivePlotFile = source.Narrative.ActivePlotFile,
                PlotProgress = new PlotProgressState
                {
                    CurrentBeat = source.Narrative.PlotProgress.CurrentBeat,
                    CompletedBeats = [.. source.Narrative.PlotProgress.CompletedBeats],
                    Deviations = [.. source.Narrative.PlotProgress.Deviations],
                },
            },
            Canonization = null,
            Game = GameRuntimeStateCloner.CloneForFork(
                source.Game,
                source.SessionId ?? Guid.Empty,
                forkedSessionId,
                DateTimeOffset.UtcNow),
        };
    }

    private static (string? ProjectName, string? FileName) ResolveForkedModeTarget(
        ModeSelectionState sourceMode,
        Guid forkedSessionId)
    {
        var projectName = NormalizeChoice(sourceMode.ProjectName);
        var fileName = NormalizeChoice(sourceMode.CurrentFile);
        if (sourceMode.ActiveMode != Mode.Roleplay || projectName is null || fileName is null)
        {
            return (projectName, fileName);
        }

        return (projectName, BuildForkedRoleplayFileName(fileName, forkedSessionId));
    }

    private static string BuildForkedRoleplayFileName(string fileName, Guid forkedSessionId)
    {
        var normalizedPath = fileName.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        var directory = Path.GetDirectoryName(normalizedPath);
        var baseFileName = Path.GetFileName(normalizedPath);
        var fileStem = Path.GetFileNameWithoutExtension(baseFileName);
        var extension = Path.GetExtension(baseFileName);
        var suffix = $"-fork-{forkedSessionId.ToString("N")[..8]}";
        var forkedFileName = string.IsNullOrEmpty(extension)
            ? fileStem + suffix
            : fileStem + suffix + extension;

        if (string.IsNullOrWhiteSpace(directory))
        {
            return forkedFileName;
        }

        return Path.Combine(directory, forkedFileName).Replace(Path.DirectorySeparatorChar, '/');
    }

    private static string? NormalizeChoice(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
