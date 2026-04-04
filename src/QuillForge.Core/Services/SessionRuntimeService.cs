using Microsoft.Extensions.Logging;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class SessionRuntimeService : ISessionStateService, ISessionRuntimeService
{
    private const string GeneralModeName = "general";
    private const string WriterModeName = "writer";
    private const int PendingContentThreshold = 200;

    private readonly ISessionStateStore _store;
    private readonly ISessionMutationGate _gate;
    private readonly IProfileConfigService _profileService;
    private readonly HashSet<string> _knownModes;
    private readonly ILogger<SessionRuntimeService> _logger;

    public SessionRuntimeService(
        ISessionStateStore store,
        ISessionMutationGate gate,
        IProfileConfigService profileService,
        IEnumerable<IMode> modes,
        ILogger<SessionRuntimeService> logger)
    {
        _store = store;
        _gate = gate;
        _profileService = profileService;
        _logger = logger;

        _knownModes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var mode in modes)
        {
            _knownModes.Add(mode.Name);
        }
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

            var effectiveConductor = ResolveEffectiveChoice(
                command.Conductor,
                currentView.Profile.ActiveConductor,
                targetProfile.Config.Conductor,
                profileChanged);
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

            state.Profile.ProfileId = targetProfile.ProfileId;
            state.Profile.ActiveConductor = ToSparseOverride(effectiveConductor, targetProfile.Config.Conductor);
            state.Profile.ActiveLoreSet = ToSparseOverride(effectiveLoreSet, targetProfile.Config.LoreSet);
            state.Profile.ActiveNarrativeRules = ToSparseOverride(effectiveNarrativeRules, targetProfile.Config.NarrativeRules);
            state.Profile.ActiveWritingStyle = ToSparseOverride(effectiveWritingStyle, targetProfile.Config.WritingStyle);
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

            if (string.Equals(state.Mode.ActiveModeName, RoleplayMode.NameConst, StringComparison.OrdinalIgnoreCase))
            {
                state.Mode.Character = state.Roleplay.ActiveAiCharacter;
            }

            await _store.SaveAsync(state, ct);

            var hydrated = await HydrateProfileViewAsync(state, ct);
            _logger.LogInformation(
                "Session profile updated: session={SessionId} profileId={ProfileId} conductor={Conductor} lore={LoreSet} narrativeRules={NarrativeRules} writingStyle={WritingStyle} aiCharacter={AiCharacter} userCharacter={UserCharacter}",
                sessionId,
                hydrated.Profile.ProfileId,
                hydrated.Profile.ActiveConductor,
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

        if (string.Equals(state.Mode.ActiveModeName, RoleplayMode.NameConst, StringComparison.OrdinalIgnoreCase)
            && command.HasAiCharacterSelection)
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

        if (!_knownModes.Contains(command.Mode))
        {
            _logger.LogWarning(
                "Session mode update rejected: session={SessionId} invalidMode={Mode}",
                sessionId,
                command.Mode);
            return SessionMutationResult<SessionState>.Invalid($"Unknown mode: {command.Mode}");
        }

        var state = await LoadStateAsync(sessionId, ct);
        var resolvedProfile = await LoadProfileForViewAsync(state.Profile.ProfileId, ct);
        var oldMode = state.Mode.ActiveModeName;

        if (string.Equals(oldMode, WriterModeName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(command.Mode, WriterModeName, StringComparison.OrdinalIgnoreCase))
        {
            state.Writer.PendingContent = null;
            state.Writer.State = WriterState.Idle;
            _logger.LogInformation("Writer pending state reset during mode change for session {SessionId}", sessionId);
        }

        state.Mode.ActiveModeName = command.Mode;
        state.Mode.ProjectName = command.Project;
        state.Mode.CurrentFile = command.File;

        if (string.Equals(command.Mode, RoleplayMode.NameConst, StringComparison.OrdinalIgnoreCase))
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
            hydrated.Mode.ActiveModeName,
            hydrated.Mode.ProjectName,
            hydrated.Mode.CurrentFile,
            hydrated.Mode.Character);

        return SessionMutationResult<SessionState>.Success(hydrated);
    }

    public async Task<SessionMutationResult<SessionState>> CaptureWriterPendingAsync(
        Guid? sessionId,
        CaptureWriterPendingCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "capture_writer_pending";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<SessionState>.Busy(
                "Another mutating operation is already running for this session.");
        }

        var state = await LoadStateAsync(sessionId, ct);
        if (!string.Equals(state.Mode.ActiveModeName, WriterModeName, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Writer pending capture skipped: session={SessionId} currentMode={CurrentMode} sourceMode={SourceMode}",
                sessionId,
                state.Mode.ActiveModeName,
                command.SourceMode);
            return SessionMutationResult<SessionState>.Success(await HydrateProfileViewAsync(state, ct));
        }

        if (state.Writer.State != WriterState.Idle)
        {
            _logger.LogInformation(
                "Writer pending capture skipped: session={SessionId} writerState={WriterState}",
                sessionId,
                state.Writer.State);
            return SessionMutationResult<SessionState>.Success(await HydrateProfileViewAsync(state, ct));
        }

        if (string.IsNullOrWhiteSpace(command.Content) || command.Content.Length <= PendingContentThreshold)
        {
            _logger.LogInformation(
                "Writer pending capture skipped: session={SessionId} contentLength={Length}",
                sessionId,
                command.Content.Length);
            return SessionMutationResult<SessionState>.Success(await HydrateProfileViewAsync(state, ct));
        }

        state.Writer.PendingContent = command.Content;
        state.Writer.State = WriterState.PendingReview;
        await _store.SaveAsync(state, ct);
        var hydrated = await HydrateProfileViewAsync(state, ct);

        _logger.LogInformation(
            "Writer pending content captured: session={SessionId} contentLength={Length}",
            sessionId,
            command.Content.Length);

        return SessionMutationResult<SessionState>.Success(hydrated);
    }

    public async Task<SessionMutationResult<WriterPendingDecisionResult>> AcceptWriterPendingAsync(
        Guid? sessionId,
        CancellationToken ct = default)
    {
        const string operationName = "accept_writer_pending";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<WriterPendingDecisionResult>.Busy(
                "Another mutating operation is already running for this session.");
        }

        var state = await LoadStateAsync(sessionId, ct);
        if (state.Writer.State != WriterState.PendingReview || state.Writer.PendingContent is null)
        {
            _logger.LogWarning("Writer pending accept rejected: session={SessionId} no content pending", sessionId);
            return SessionMutationResult<WriterPendingDecisionResult>.Invalid("No pending writer content to accept.");
        }

        var accepted = state.Writer.PendingContent;
        state.Writer.PendingContent = null;
        state.Writer.State = WriterState.Idle;
        await _store.SaveAsync(state, ct);

        _logger.LogInformation(
            "Writer pending content accepted: session={SessionId} contentLength={Length}",
            sessionId,
            accepted.Length);

        return SessionMutationResult<WriterPendingDecisionResult>.Success(
            new WriterPendingDecisionResult(accepted));
    }

    public async Task<SessionMutationResult<SessionState>> RejectWriterPendingAsync(
        Guid? sessionId,
        CancellationToken ct = default)
    {
        const string operationName = "reject_writer_pending";

        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<SessionState>.Busy(
                "Another mutating operation is already running for this session.");
        }

        var state = await LoadStateAsync(sessionId, ct);
        if (state.Writer.State != WriterState.PendingReview)
        {
            _logger.LogWarning("Writer pending reject rejected: session={SessionId} no content pending", sessionId);
            return SessionMutationResult<SessionState>.Invalid("No pending writer content to reject.");
        }

        state.Writer.PendingContent = null;
        state.Writer.State = WriterState.Idle;
        await _store.SaveAsync(state, ct);

        _logger.LogInformation("Writer pending content rejected: session={SessionId}", sessionId);

        return SessionMutationResult<SessionState>.Success(await HydrateProfileViewAsync(state, ct));
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
                ActiveConductor = null,
                ActiveLoreSet = null,
                ActiveNarrativeRules = null,
                ActiveWritingStyle = null,
            }
            : new ProfileState
            {
                ProfileId = state.Profile.ProfileId,
                ActiveConductor = NormalizeStoredOverride(state.Profile.ActiveConductor, resolved.Config.Conductor),
                ActiveLoreSet = NormalizeStoredOverride(state.Profile.ActiveLoreSet, resolved.Config.LoreSet),
                ActiveNarrativeRules = NormalizeStoredOverride(state.Profile.ActiveNarrativeRules, resolved.Config.NarrativeRules),
                ActiveWritingStyle = NormalizeStoredOverride(state.Profile.ActiveWritingStyle, resolved.Config.WritingStyle),
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
                ActiveModeName = state.Mode.ActiveModeName,
                ProjectName = state.Mode.ProjectName,
                CurrentFile = state.Mode.CurrentFile,
                Character = string.Equals(state.Mode.ActiveModeName, RoleplayMode.NameConst, StringComparison.OrdinalIgnoreCase)
                    ? NormalizeChoice(state.Mode.Character) ?? activeAiCharacter
                    : state.Mode.Character,
            },
            Profile = new ProfileState
            {
                ProfileId = resolved.ProfileId,
                ActiveConductor = NormalizeChoice(state.Profile.ActiveConductor) ?? resolved.Config.Conductor,
                ActiveLoreSet = NormalizeChoice(state.Profile.ActiveLoreSet) ?? resolved.Config.LoreSet,
                ActiveNarrativeRules = NormalizeChoice(state.Profile.ActiveNarrativeRules) ?? resolved.Config.NarrativeRules,
                ActiveWritingStyle = NormalizeChoice(state.Profile.ActiveWritingStyle) ?? resolved.Config.WritingStyle,
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
        return NormalizeChoice(profile.ActiveConductor) is not null
            && NormalizeChoice(profile.ActiveLoreSet) is not null
            && NormalizeChoice(profile.ActiveNarrativeRules) is not null
            && NormalizeChoice(profile.ActiveWritingStyle) is not null;
    }

    private static bool IsUntouchedSessionState(SessionState state)
    {
        return string.Equals(state.Mode.ActiveModeName, GeneralModeName, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(state.Mode.ProjectName)
            && string.IsNullOrWhiteSpace(state.Mode.CurrentFile)
            && string.IsNullOrWhiteSpace(state.Mode.Character)
            && !state.Roleplay.HasExplicitAiCharacterSelection
            && !state.Roleplay.HasExplicitUserCharacterSelection
            && state.Writer.State == WriterState.Idle
            && string.IsNullOrWhiteSpace(state.Writer.PendingContent)
            && string.IsNullOrWhiteSpace(state.Narrative.DirectorNotes)
            && string.IsNullOrWhiteSpace(state.Narrative.ActivePlotFile)
            && string.IsNullOrWhiteSpace(state.Narrative.PlotProgress.CurrentBeat)
            && state.Narrative.PlotProgress.CompletedBeats.Count == 0
            && state.Narrative.PlotProgress.Deviations.Count == 0;
    }

    private static bool ProfileStatesEqual(ProfileState left, ProfileState right)
    {
        return string.Equals(left.ProfileId, right.ProfileId, StringComparison.Ordinal)
            && string.Equals(left.ActiveConductor, right.ActiveConductor, StringComparison.Ordinal)
            && string.Equals(left.ActiveLoreSet, right.ActiveLoreSet, StringComparison.Ordinal)
            && string.Equals(left.ActiveNarrativeRules, right.ActiveNarrativeRules, StringComparison.Ordinal)
            && string.Equals(left.ActiveWritingStyle, right.ActiveWritingStyle, StringComparison.Ordinal);
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
}
