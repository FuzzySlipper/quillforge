using Microsoft.Extensions.Logging;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class SessionRuntimeService : ISessionStateService
{
    private const int PendingContentThreshold = 200;

    private readonly ISessionStateStore _store;
    private readonly ISessionMutationGate _gate;
    private readonly IProfileConfigService _profileService;
    private readonly IStoryStore _storyStore;
    private readonly ILogger<SessionRuntimeService> _logger;

    public SessionRuntimeService(
        ISessionStateStore store,
        ISessionMutationGate gate,
        IProfileConfigService profileService,
        IStoryStore storyStore,
        IEnumerable<IMode> _,
        ILogger<SessionRuntimeService> logger)
    {
        _store = store;
        _gate = gate;
        _profileService = profileService;
        _storyStore = storyStore;
        _logger = logger;
    }

    public async Task<SessionState> LoadViewAsync(Guid? sessionId, CancellationToken ct = default)
    {
        var state = await LoadStateAsync(sessionId, ct);
        return await HydrateProfileViewAsync(state, ct);
    }

    public async Task<SessionMutationResult<SessionState>> SetProfileAsync(
        Guid? sessionId,
        SetSessionProfileCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "set_profile";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<SessionState>.Busy(
                "Another mutating operation is already running for this session.");
        }

        try
        {
            var state = await LoadStateAsync(sessionId, ct);
            var currentView = await HydrateProfileViewAsync(state, ct);
            var currentProfile = await LoadProfileForViewAsync(state.Profile.ProfileId, ct);
            var targetProfileId = NormalizeChoice(command.ProfileId) ?? currentView.Profile.ProfileId;
            var targetProfile = await _profileService.LoadResolvedAsync(targetProfileId, ct);
            var profileChanged = !string.Equals(
                currentView.Profile.ProfileId,
                targetProfile.ProfileId,
                StringComparison.OrdinalIgnoreCase);
            var effectiveLoreSet = ResolveEffectiveChoice(
                command.LoreSet,
                currentView.Profile.ActiveLoreSet,
                targetProfile.Config.LoreSet,
                profileChanged);
            var effectiveNarrativeRules = ResolveEffectiveChoice(
                command.NarrativeRules,
                currentView.Profile.ActiveNarrativeRules,
                targetProfile.Config.NarrativeRules,
                profileChanged);
            var effectiveWritingStyle = ResolveEffectiveChoice(
                command.WritingStyle,
                currentView.Profile.ActiveWritingStyle,
                targetProfile.Config.WritingStyle,
                profileChanged);
            var effectiveLibrarianPrompt = ResolveEffectiveChoice(
                command.LibrarianPrompt,
                currentView.Profile.ActiveLibrarianPrompt,
                targetProfile.Config.LibrarianPrompt,
                profileChanged);

            state.Profile.ProfileId = targetProfile.ProfileId;
            state.Profile.ActiveLoreSet = ToSparseOverride(effectiveLoreSet, targetProfile.Config.LoreSet);
            state.Profile.ActiveNarrativeRules = ToSparseOverride(effectiveNarrativeRules, targetProfile.Config.NarrativeRules);
            state.Profile.ActiveWritingStyle = ToSparseOverride(effectiveWritingStyle, targetProfile.Config.WritingStyle);
            state.Profile.ActiveLibrarianPrompt = ToSparseOverride(effectiveLibrarianPrompt, targetProfile.Config.LibrarianPrompt);
            state.Roleplay.ActiveAiCharacter = ResolveRoleplaySelectionForProfileChange(
                state.Roleplay.ActiveAiCharacter,
                state.Roleplay.HasExplicitAiCharacterSelection,
                currentProfile.Config.Roleplay.AiCharacter,
                targetProfile.Config.Roleplay.AiCharacter,
                profileChanged);
            state.Roleplay.ActiveUserCharacter = ResolveRoleplaySelectionForProfileChange(
                state.Roleplay.ActiveUserCharacter,
                state.Roleplay.HasExplicitUserCharacterSelection,
                currentProfile.Config.Roleplay.UserCharacter,
                targetProfile.Config.Roleplay.UserCharacter,
                profileChanged);

            if (state.Mode.ActiveMode == Mode.Roleplay)
            {
                state.Mode.Character = state.Roleplay.ActiveAiCharacter;
            }

            await _store.SaveAsync(state, ct);

            var hydrated = await HydrateProfileViewAsync(state, ct);
            _logger.LogInformation(
                "Session profile updated: session={SessionId} profileId={ProfileId} lore={LoreSet} narrativeRules={NarrativeRules} writingStyle={WritingStyle} aiCharacter={AiCharacter} userCharacter={UserCharacter}",
                sessionId,
                hydrated.Profile.ProfileId,
                hydrated.Profile.ActiveLoreSet,
                hydrated.Profile.ActiveNarrativeRules,
                hydrated.Profile.ActiveWritingStyle,
                hydrated.Roleplay.ActiveAiCharacter,
                hydrated.Roleplay.ActiveUserCharacter);

            return SessionMutationResult<SessionState>.Success(hydrated);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(ex, "Session profile update rejected: session={SessionId} profile not found", sessionId);
            return SessionMutationResult<SessionState>.Invalid(ex.Message);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(ex, "Session profile update rejected: session={SessionId} invalid request", sessionId);
            return SessionMutationResult<SessionState>.Invalid(ex.Message);
        }
    }

    public async Task<SessionMutationResult<SessionState>> SetRoleplayAsync(
        Guid? sessionId,
        SetSessionRoleplayCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "set_roleplay";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<SessionState>.Busy(
                "Another mutating operation is already running for this session.");
        }

        var state = await LoadStateAsync(sessionId, ct);

        if (command.HasAiCharacterSelection)
        {
            state.Roleplay.ActiveAiCharacter = NormalizeChoice(command.AiCharacter);
            state.Roleplay.HasExplicitAiCharacterSelection = true;
        }

        if (command.HasUserCharacterSelection)
        {
            state.Roleplay.ActiveUserCharacter = NormalizeChoice(command.UserCharacter);
            state.Roleplay.HasExplicitUserCharacterSelection = true;
        }

        if (state.Mode.ActiveMode == Mode.Roleplay && command.HasAiCharacterSelection)
        {
            state.Mode.Character = state.Roleplay.ActiveAiCharacter;
        }

        await _store.SaveAsync(state, ct);
        var hydrated = await HydrateProfileViewAsync(state, ct);

        _logger.LogInformation(
            "Session roleplay updated: session={SessionId} aiCharacter={AiCharacter} userCharacter={UserCharacter} explicitAi={ExplicitAi} explicitUser={ExplicitUser}",
            sessionId,
            hydrated.Roleplay.ActiveAiCharacter,
            hydrated.Roleplay.ActiveUserCharacter,
            state.Roleplay.HasExplicitAiCharacterSelection,
            state.Roleplay.HasExplicitUserCharacterSelection);

        return SessionMutationResult<SessionState>.Success(hydrated);
    }

    public async Task<SessionMutationResult<SessionState>> SetModeAsync(
        Guid? sessionId,
        SetSessionModeCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "set_mode";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<SessionState>.Busy(
                "Another mutating operation is already running for this session.");
        }

        var parsedMode = ModeExtensions.TryParseMode(command.Mode);
        if (!parsedMode.HasValue)
        {
            _logger.LogWarning(
                "Session mode update rejected: session={SessionId} invalidMode={Mode}",
                sessionId,
                command.Mode);
            return SessionMutationResult<SessionState>.Invalid($"Unknown mode: {command.Mode}");
        }

        var state = await LoadStateAsync(sessionId, ct);
        var resolvedProfile = await LoadProfileForViewAsync(state.Profile.ProfileId, ct);
        var oldMode = state.Mode.ActiveMode;
        var targetMode = parsedMode.Value;
        var oldProjectName = NormalizeChoice(state.Mode.ProjectName);
        var oldFileName = NormalizeChoice(state.Mode.CurrentFile);
        var newProjectName = NormalizeChoice(command.Project);
        var newFileName = NormalizeChoice(command.File);

        if (targetMode == Mode.Roleplay)
        {
            if (newProjectName is null)
            {
                newProjectName = oldMode == Mode.Roleplay
                    ? oldProjectName
                    : BuildDefaultRoleplayProjectName(sessionId);
            }

            if (newFileName is null)
            {
                newFileName = oldMode == Mode.Roleplay
                    ? oldFileName
                    : DefaultRoleplayFileName;
            }
        }

        if (oldMode == Mode.Writer && targetMode != Mode.Writer)
        {
            ClearWriterPendingState(state.Writer);
            _logger.LogInformation("Writer pending state reset during mode change for session {SessionId}", sessionId);
        }
        else if (oldMode == Mode.Writer
            && targetMode == Mode.Writer
            && state.Writer.State == WriterState.PendingReview
            && state.Writer.PendingContent is not null
            && (!string.Equals(oldProjectName, newProjectName, StringComparison.Ordinal)
                || !string.Equals(oldFileName, newFileName, StringComparison.Ordinal)))
        {
            _logger.LogInformation(
                "Writer mode target changed while pending content exists: session={SessionId} oldProject={OldProject} oldFile={OldFile} newProject={NewProject} newFile={NewFile} pendingProject={PendingProject} pendingFile={PendingFile}",
                sessionId,
                oldProjectName,
                oldFileName,
                newProjectName,
                newFileName,
                state.Writer.PendingProjectName,
                state.Writer.PendingFileName);
        }

        state.Mode.ActiveMode = targetMode;
        state.Mode.ProjectName = newProjectName;
        state.Mode.CurrentFile = newFileName;

        if (targetMode == Mode.Roleplay)
        {
            var requestedCharacter = NormalizeChoice(command.Character);
            if (requestedCharacter is not null)
            {
                state.Roleplay.ActiveAiCharacter = requestedCharacter;
                state.Roleplay.HasExplicitAiCharacterSelection = true;
                state.Mode.Character = requestedCharacter;
            }
            else
            {
                state.Mode.Character = ResolveRoleplayViewChoice(
                    state.Roleplay.ActiveAiCharacter,
                    state.Roleplay.HasExplicitAiCharacterSelection,
                    resolvedProfile.Config.Roleplay.AiCharacter);
            }
        }
        else
        {
            state.Mode.Character = command.Character;
        }

        await _store.SaveAsync(state, ct);
        var hydrated = await HydrateProfileViewAsync(state, ct);

        _logger.LogInformation(
            "Session mode updated: session={SessionId} oldMode={OldMode} newMode={NewMode} project={Project} file={File} character={Character}",
            sessionId,
            oldMode,
            hydrated.Mode.ActiveMode,
            hydrated.Mode.ProjectName,
            hydrated.Mode.CurrentFile,
            hydrated.Mode.Character);

        return SessionMutationResult<SessionState>.Success(hydrated);
    }

    public async Task<SessionMutationResult<WriterPendingCaptureEvent>> CaptureWriterPendingAsync(
        Guid? sessionId,
        CaptureWriterPendingCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "capture_writer_pending";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<WriterPendingCaptureEvent>.Busy(
                "Another mutating operation is already running for this session.");
        }

        var state = await LoadStateAsync(sessionId, ct);
        if (state.Mode.ActiveMode != Mode.Writer)
        {
            _logger.LogInformation(
                "Writer pending capture skipped: session={SessionId} currentMode={CurrentMode} sourceMode={SourceMode}",
                sessionId,
                state.Mode.ActiveMode,
                command.SourceMode);
            return SessionMutationResult<WriterPendingCaptureEvent>.Success(
                new WriterPendingCaptureSkippedEvent(
                    await HydrateProfileViewAsync(state, ct),
                    "mode_mismatch"));
        }

        if (string.IsNullOrWhiteSpace(command.Content) || command.Content.Length <= PendingContentThreshold)
        {
            _logger.LogInformation(
                "Writer pending capture skipped: session={SessionId} contentLength={Length}",
                sessionId,
                command.Content.Length);
            return SessionMutationResult<WriterPendingCaptureEvent>.Success(
                new WriterPendingCaptureSkippedEvent(
                    await HydrateProfileViewAsync(state, ct),
                    "content_below_threshold"));
        }

        state.Writer.PendingContent = command.Content;
        state.Writer.PendingProjectName = NormalizeChoice(state.Mode.ProjectName);
        state.Writer.PendingFileName = NormalizeChoice(state.Mode.CurrentFile);
        state.Writer.State = WriterState.PendingReview;
        await _store.SaveAsync(state, ct);
        var hydrated = await HydrateProfileViewAsync(state, ct);

        _logger.LogInformation(
            "Writer pending content captured: session={SessionId} contentLength={Length} project={Project} file={File}",
            sessionId,
            command.Content.Length,
            state.Writer.PendingProjectName,
            state.Writer.PendingFileName);

        return SessionMutationResult<WriterPendingCaptureEvent>.Success(
            new WriterPendingContentCapturedEvent(hydrated, command.Content.Length, command.SourceMode));
    }

    public async Task<SessionMutationResult<WriterPendingContentAcceptedEvent>> AcceptWriterPendingAsync(
        Guid? sessionId,
        CancellationToken ct = default)
    {
        const string operationName = "accept_writer_pending";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<WriterPendingContentAcceptedEvent>.Busy(
                "Another mutating operation is already running for this session.");
        }

        var state = await LoadStateAsync(sessionId, ct);
        if (state.Writer.State != WriterState.PendingReview || state.Writer.PendingContent is null)
        {
            _logger.LogWarning("Writer pending accept rejected: session={SessionId} no content pending", sessionId);
            return SessionMutationResult<WriterPendingContentAcceptedEvent>.Invalid("No pending writer content to accept.");
        }

        if (state.Mode.ActiveMode != Mode.Writer)
        {
            _logger.LogWarning(
                "Writer pending accept rejected: session={SessionId} mode={Mode}",
                sessionId,
                state.Mode.ActiveMode);
            return SessionMutationResult<WriterPendingContentAcceptedEvent>.Invalid(
                "Writer pending content can only be accepted while Writer mode is active.");
        }

        var target = ResolveWriterPendingTarget(state);
        if (target.HasIncompleteCapturedTarget)
        {
            _logger.LogWarning(
                "Writer pending accept rejected: session={SessionId} incomplete captured target pendingProject={PendingProject} pendingFile={PendingFile} currentProject={CurrentProject} currentFile={CurrentFile}",
                sessionId,
                state.Writer.PendingProjectName,
                state.Writer.PendingFileName,
                state.Mode.ProjectName,
                state.Mode.CurrentFile);
            return SessionMutationResult<WriterPendingContentAcceptedEvent>.Invalid(
                "Writer pending content has an incomplete saved target and cannot be accepted safely.");
        }

        var projectName = target.ProjectName;
        var fileName = target.FileName;
        if (!IsSafeRelativePath(projectName) || !IsSafeRelativePath(fileName))
        {
            _logger.LogWarning(
                "Writer pending accept rejected: session={SessionId} invalid target project={Project} file={File} capturedProject={CapturedProject} capturedFile={CapturedFile} currentProject={CurrentProject} currentFile={CurrentFile}",
                sessionId,
                projectName,
                fileName,
                state.Writer.PendingProjectName,
                state.Writer.PendingFileName,
                state.Mode.ProjectName,
                state.Mode.CurrentFile);
            return SessionMutationResult<WriterPendingContentAcceptedEvent>.Invalid(
                "Writer pending content requires an active project and file before it can be accepted.");
        }

        var accepted = state.Writer.PendingContent;
        var savedPath = BuildWriterSavedPath(projectName!, fileName!);

        await _storyStore.WriteAsync(projectName!, fileName!, accepted, ct);
        ClearWriterPendingState(state.Writer);
        await _store.SaveAsync(state, ct);

        _logger.LogInformation(
            "Writer pending content accepted: session={SessionId} project={Project} file={File} contentLength={Length} usedCapturedTarget={UsedCapturedTarget}",
            sessionId,
            projectName,
            fileName,
            accepted.Length,
            target.UsedCapturedTarget);

        return SessionMutationResult<WriterPendingContentAcceptedEvent>.Success(
            new WriterPendingContentAcceptedEvent(sessionId, accepted, savedPath));
    }

    public async Task<SessionMutationResult<WriterPendingContentRejectedEvent>> RejectWriterPendingAsync(
        Guid? sessionId,
        CancellationToken ct = default)
    {
        const string operationName = "reject_writer_pending";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<WriterPendingContentRejectedEvent>.Busy(
                "Another mutating operation is already running for this session.");
        }

        var state = await LoadStateAsync(sessionId, ct);
        if (state.Writer.State != WriterState.PendingReview)
        {
            _logger.LogWarning("Writer pending reject rejected: session={SessionId} no content pending", sessionId);
            return SessionMutationResult<WriterPendingContentRejectedEvent>.Invalid("No pending writer content to reject.");
        }

        ClearWriterPendingState(state.Writer);
        await _store.SaveAsync(state, ct);

        _logger.LogInformation("Writer pending content rejected: session={SessionId}", sessionId);

        return SessionMutationResult<WriterPendingContentRejectedEvent>.Success(
            new WriterPendingContentRejectedEvent(await HydrateProfileViewAsync(state, ct)));
    }

    public async Task<SessionMutationResult<SessionState>> UpdateNarrativeStateAsync(
        Guid? sessionId,
        UpdateNarrativeStateCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "update_narrative_state";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<SessionState>.Busy(
                "Another mutating operation is already running for this session.");
        }

        if (string.IsNullOrWhiteSpace(command.DirectorNotes))
        {
            _logger.LogWarning(
                "Narrative state update rejected: session={SessionId} empty director notes",
                sessionId);
            return SessionMutationResult<SessionState>.Invalid("Director notes are required.");
        }

        var state = await LoadStateAsync(sessionId, ct);
        state.Narrative.DirectorNotes = command.DirectorNotes;
        if (command.ActivePlotFile is not null)
        {
            state.Narrative.ActivePlotFile = command.ActivePlotFile;
        }
        if (command.PlotProgress is not null)
        {
            state.Narrative.PlotProgress.CurrentBeat = command.PlotProgress.CurrentBeat;
            state.Narrative.PlotProgress.CompletedBeats = command.PlotProgress.CompletedBeats?.ToList() ?? [];
            state.Narrative.PlotProgress.Deviations = command.PlotProgress.Deviations?.ToList() ?? [];
        }

        await _store.SaveAsync(state, ct);
        var hydrated = await HydrateProfileViewAsync(state, ct);

        _logger.LogInformation(
            "Narrative state updated: session={SessionId} notesLength={Length} activePlot={ActivePlot}",
            sessionId,
            command.DirectorNotes.Length,
            hydrated.Narrative.ActivePlotFile);

        return SessionMutationResult<SessionState>.Success(hydrated);
    }

    public async Task<SessionMutationResult<SessionState>> SetActivePlotAsync(
        Guid? sessionId,
        SetActivePlotCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "set_active_plot";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<SessionState>.Busy(
                "Another mutating operation is already running for this session.");
        }

        if (string.IsNullOrWhiteSpace(command.PlotFileName))
        {
            _logger.LogWarning("Active plot update rejected: session={SessionId} empty plot file", sessionId);
            return SessionMutationResult<SessionState>.Invalid("Plot file name is required.");
        }

        var state = await LoadStateAsync(sessionId, ct);
        state.Narrative.ActivePlotFile = command.PlotFileName;
        state.Narrative.PlotProgress = new PlotProgressState();
        await _store.SaveAsync(state, ct);
        var hydrated = await HydrateProfileViewAsync(state, ct);

        _logger.LogInformation(
            "Active plot set: session={SessionId} plot={Plot}",
            sessionId,
            hydrated.Narrative.ActivePlotFile);

        return SessionMutationResult<SessionState>.Success(hydrated);
    }

    public async Task<SessionMutationResult<SessionState>> ClearActivePlotAsync(
        Guid? sessionId,
        CancellationToken ct = default)
    {
        const string operationName = "clear_active_plot";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<SessionState>.Busy(
                "Another mutating operation is already running for this session.");
        }

        var state = await LoadStateAsync(sessionId, ct);
        state.Narrative.ActivePlotFile = null;
        state.Narrative.PlotProgress = new PlotProgressState();
        await _store.SaveAsync(state, ct);

        _logger.LogInformation("Active plot cleared: session={SessionId}", sessionId);

        return SessionMutationResult<SessionState>.Success(await HydrateProfileViewAsync(state, ct));
    }

    private async Task<SessionState> LoadStateAsync(Guid? sessionId, CancellationToken ct)
    {
        var state = await _store.LoadAsync(sessionId, ct);
        return await NormalizeStoredProfileStateAsync(state, ct);
    }

    private async Task<SessionState> NormalizeStoredProfileStateAsync(
        SessionState state,
        CancellationToken ct)
    {
        var resolved = await LoadProfileForViewAsync(state.Profile.ProfileId, ct);
        var normalizeLegacyHydratedDefaults = LooksLikeLegacyHydratedProfileState(state.Profile)
            && IsUntouchedSessionState(state);

        var normalizedProfile = normalizeLegacyHydratedDefaults
            ? new ProfileState
            {
                ProfileId = state.Profile.ProfileId,
                ActiveLoreSet = null,
                ActiveNarrativeRules = null,
                ActiveWritingStyle = null,
                ActiveLibrarianPrompt = null,
            }
            : new ProfileState
            {
                ProfileId = state.Profile.ProfileId,
                ActiveLoreSet = NormalizeStoredOverride(state.Profile.ActiveLoreSet, resolved.Config.LoreSet),
                ActiveNarrativeRules = NormalizeStoredOverride(state.Profile.ActiveNarrativeRules, resolved.Config.NarrativeRules),
                ActiveWritingStyle = NormalizeStoredOverride(state.Profile.ActiveWritingStyle, resolved.Config.WritingStyle),
                ActiveLibrarianPrompt = NormalizeStoredOverride(state.Profile.ActiveLibrarianPrompt, resolved.Config.LibrarianPrompt),
            };
        var normalizedRoleplay = new RoleplayRuntimeState
        {
            HasExplicitAiCharacterSelection = state.Roleplay.HasExplicitAiCharacterSelection,
            ActiveAiCharacter = NormalizeChoice(state.Roleplay.ActiveAiCharacter),
            HasExplicitUserCharacterSelection = state.Roleplay.HasExplicitUserCharacterSelection,
            ActiveUserCharacter = NormalizeChoice(state.Roleplay.ActiveUserCharacter),
        };

        if (ProfileStatesEqual(state.Profile, normalizedProfile)
            && RoleplayStatesEqual(state.Roleplay, normalizedRoleplay))
        {
            return state;
        }

        state.Profile = normalizedProfile;
        state.Roleplay = normalizedRoleplay;
        await _store.SaveAsync(state, ct);

        _logger.LogInformation(
            "Normalized stored session profile state for session {SessionId}: profileId={ProfileId} legacyHydratedDefaults={LegacyHydratedDefaults} explicitAi={ExplicitAi} explicitUser={ExplicitUser}",
            state.SessionId,
            state.Profile.ProfileId,
            normalizeLegacyHydratedDefaults,
            state.Roleplay.HasExplicitAiCharacterSelection,
            state.Roleplay.HasExplicitUserCharacterSelection);

        return state;
    }

    private async Task<SessionState> HydrateProfileViewAsync(
        SessionState state,
        CancellationToken ct)
    {
        var resolved = await LoadProfileForViewAsync(state.Profile.ProfileId, ct);
        var activeAiCharacter = ResolveRoleplayViewChoice(
            state.Roleplay.ActiveAiCharacter,
            state.Roleplay.HasExplicitAiCharacterSelection,
            resolved.Config.Roleplay.AiCharacter);
        var activeUserCharacter = ResolveRoleplayViewChoice(
            state.Roleplay.ActiveUserCharacter,
            state.Roleplay.HasExplicitUserCharacterSelection,
            resolved.Config.Roleplay.UserCharacter);

        return new SessionState
        {
            SessionId = state.SessionId,
            LastModified = state.LastModified,
            Mode = new ModeSelectionState
            {
                ActiveMode = state.Mode.ActiveMode,
                ProjectName = state.Mode.ProjectName,
                CurrentFile = state.Mode.CurrentFile,
                Character = state.Mode.ActiveMode == Mode.Roleplay
                    ? NormalizeChoice(state.Mode.Character) ?? activeAiCharacter
                    : state.Mode.Character,
            },
            Profile = new ProfileState
            {
                ProfileId = resolved.ProfileId,
                ActiveLoreSet = NormalizeChoice(state.Profile.ActiveLoreSet) ?? resolved.Config.LoreSet,
                ActiveNarrativeRules = NormalizeChoice(state.Profile.ActiveNarrativeRules) ?? resolved.Config.NarrativeRules,
                ActiveWritingStyle = NormalizeChoice(state.Profile.ActiveWritingStyle) ?? resolved.Config.WritingStyle,
                ActiveLibrarianPrompt = NormalizeChoice(state.Profile.ActiveLibrarianPrompt) ?? resolved.Config.LibrarianPrompt,
            },
            Roleplay = new RoleplayRuntimeState
            {
                HasExplicitAiCharacterSelection = state.Roleplay.HasExplicitAiCharacterSelection,
                ActiveAiCharacter = activeAiCharacter,
                HasExplicitUserCharacterSelection = state.Roleplay.HasExplicitUserCharacterSelection,
                ActiveUserCharacter = activeUserCharacter,
            },
            Writer = new WriterRuntimeState
            {
                PendingContent = state.Writer.PendingContent,
                PendingProjectName = state.Writer.PendingProjectName,
                PendingFileName = state.Writer.PendingFileName,
                State = state.Writer.State,
            },
            Narrative = new NarrativeRuntimeState
            {
                DirectorNotes = state.Narrative.DirectorNotes,
                ActivePlotFile = state.Narrative.ActivePlotFile,
                PlotProgress = new PlotProgressState
                {
                    CurrentBeat = state.Narrative.PlotProgress.CurrentBeat,
                    CompletedBeats = [.. state.Narrative.PlotProgress.CompletedBeats],
                    Deviations = [.. state.Narrative.PlotProgress.Deviations],
                },
            },
        };
    }

    private async Task<ResolvedProfileConfig> LoadProfileForViewAsync(string? profileId, CancellationToken ct)
    {
        try
        {
            return await _profileService.LoadResolvedAsync(profileId, ct);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogWarning(
                ex,
                "Stored session profile {ProfileId} was missing; falling back to the default profile",
                profileId);
            return await _profileService.LoadResolvedAsync(ct: ct);
        }
        catch (ArgumentException ex)
        {
            _logger.LogWarning(
                ex,
                "Stored session profile {ProfileId} was invalid; falling back to the default profile",
                profileId);
            return await _profileService.LoadResolvedAsync(ct: ct);
        }
    }

    private static string ResolveEffectiveChoice(
        string? requestedValue,
        string? currentValue,
        string profileDefault,
        bool profileChanged)
    {
        var explicitValue = NormalizeChoice(requestedValue);
        if (explicitValue is not null)
        {
            return explicitValue;
        }

        if (profileChanged)
        {
            return profileDefault;
        }

        return NormalizeChoice(currentValue) ?? profileDefault;
    }

    private static string? ToSparseOverride(string value, string profileDefault)
    {
        return string.Equals(value, profileDefault, StringComparison.OrdinalIgnoreCase)
            ? null
            : value;
    }

    private static string? NormalizeStoredOverride(string? value, string profileDefault)
    {
        var normalized = NormalizeChoice(value);
        if (normalized is null)
        {
            return null;
        }

        return ToSparseOverride(normalized, profileDefault);
    }

    private static bool LooksLikeLegacyHydratedProfileState(ProfileState profile)
    {
        return NormalizeChoice(profile.ActiveLoreSet) is not null
            && NormalizeChoice(profile.ActiveNarrativeRules) is not null
            && NormalizeChoice(profile.ActiveWritingStyle) is not null;
    }

    private static bool IsUntouchedSessionState(SessionState state)
    {
        return state.Mode.ActiveMode == Mode.Guide
            && string.IsNullOrWhiteSpace(state.Mode.ProjectName)
            && string.IsNullOrWhiteSpace(state.Mode.CurrentFile)
            && string.IsNullOrWhiteSpace(state.Mode.Character)
            && !state.Roleplay.HasExplicitAiCharacterSelection
            && !state.Roleplay.HasExplicitUserCharacterSelection
            && state.Writer.State == WriterState.Idle
            && string.IsNullOrWhiteSpace(state.Writer.PendingContent)
            && string.IsNullOrWhiteSpace(state.Writer.PendingProjectName)
            && string.IsNullOrWhiteSpace(state.Writer.PendingFileName)
            && string.IsNullOrWhiteSpace(state.Narrative.DirectorNotes)
            && string.IsNullOrWhiteSpace(state.Narrative.ActivePlotFile)
            && string.IsNullOrWhiteSpace(state.Narrative.PlotProgress.CurrentBeat)
            && state.Narrative.PlotProgress.CompletedBeats.Count == 0
            && state.Narrative.PlotProgress.Deviations.Count == 0;
    }

    private static bool ProfileStatesEqual(ProfileState left, ProfileState right)
    {
        return string.Equals(left.ProfileId, right.ProfileId, StringComparison.Ordinal)
            && string.Equals(left.ActiveLoreSet, right.ActiveLoreSet, StringComparison.Ordinal)
            && string.Equals(left.ActiveNarrativeRules, right.ActiveNarrativeRules, StringComparison.Ordinal)
            && string.Equals(left.ActiveWritingStyle, right.ActiveWritingStyle, StringComparison.Ordinal)
            && string.Equals(left.ActiveLibrarianPrompt, right.ActiveLibrarianPrompt, StringComparison.Ordinal);
    }

    private static bool RoleplayStatesEqual(RoleplayRuntimeState left, RoleplayRuntimeState right)
    {
        return left.HasExplicitAiCharacterSelection == right.HasExplicitAiCharacterSelection
            && string.Equals(left.ActiveAiCharacter, right.ActiveAiCharacter, StringComparison.Ordinal)
            && left.HasExplicitUserCharacterSelection == right.HasExplicitUserCharacterSelection
            && string.Equals(left.ActiveUserCharacter, right.ActiveUserCharacter, StringComparison.Ordinal);
    }

    private static string? ResolveRoleplaySelectionForProfileChange(
        string? currentValue,
        bool hasExplicitSelection,
        string? currentProfileDefault,
        string? targetProfileDefault,
        bool profileChanged)
    {
        var normalizedCurrentValue = NormalizeChoice(currentValue);
        var effectiveCurrentValue = normalizedCurrentValue ?? NormalizeChoice(currentProfileDefault);
        if (hasExplicitSelection)
        {
            return normalizedCurrentValue;
        }

        if (!profileChanged)
        {
            return effectiveCurrentValue;
        }

        if (string.Equals(effectiveCurrentValue, NormalizeChoice(currentProfileDefault), StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeChoice(targetProfileDefault);
        }

        return effectiveCurrentValue;
    }

    private static string? ResolveRoleplayViewChoice(
        string? currentValue,
        bool hasExplicitSelection,
        string? profileDefault)
    {
        var normalizedCurrentValue = NormalizeChoice(currentValue);
        if (hasExplicitSelection)
        {
            return normalizedCurrentValue;
        }

        return normalizedCurrentValue ?? NormalizeChoice(profileDefault);
    }

    private static string? NormalizeChoice(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private const string DefaultRoleplayFileName = "scene-01.md";

    private static string BuildDefaultRoleplayProjectName(Guid? sessionId)
    {
        if (!sessionId.HasValue)
        {
            return "roleplay-session";
        }

        var compactId = sessionId.Value.ToString("N")[..12];
        return $"roleplay-{compactId}";
    }

    private static bool IsSafeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        if (Path.IsPathRooted(normalized))
        {
            return false;
        }

        var segments = normalized.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return false;
        }

        foreach (var segment in segments)
        {
            if (segment == "." || segment == "..")
            {
                return false;
            }
        }

        return true;
    }

    private static void ClearWriterPendingState(WriterRuntimeState writer)
    {
        writer.PendingContent = null;
        writer.PendingProjectName = null;
        writer.PendingFileName = null;
        writer.State = WriterState.Idle;
    }

    private static (string? ProjectName, string? FileName, bool UsedCapturedTarget, bool HasIncompleteCapturedTarget) ResolveWriterPendingTarget(SessionState state)
    {
        var pendingProjectName = NormalizeChoice(state.Writer.PendingProjectName);
        var pendingFileName = NormalizeChoice(state.Writer.PendingFileName);
        if ((pendingProjectName is null) != (pendingFileName is null))
        {
            return (pendingProjectName, pendingFileName, false, true);
        }

        if (pendingProjectName is not null)
        {
            return (pendingProjectName, pendingFileName, true, false);
        }

        return (
            NormalizeChoice(state.Mode.ProjectName),
            NormalizeChoice(state.Mode.CurrentFile),
            false,
            false);
    }

    private static string BuildWriterSavedPath(string projectName, string fileName)
    {
        return $"{ContentPaths.Story}/{projectName}/{fileName.Replace('\\', '/')}";
    }
}
