using Den.RulesEngine;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class GameBridgeService : IGameBridgeService
{
    private const int MinimumCoordinatorPassLimit = 4;
    private const int MaximumCoordinatorPassLimit = 16;

    private readonly IGameTemplateService _templateService;
    private readonly IGameRuntimeService _runtimeService;
    private readonly GameModuleRegistry _moduleRegistry;
    private readonly IGameIntentTranslationAgent _translationAgent;
    private readonly IGameAgentTurnService _agentTurnService;
    private readonly ParticipantChannelService _channelService;
    private readonly GameVisibilityProjector _visibilityProjector;
    private readonly IGameEventNarrationComposer _narrationComposer;
    private readonly ILogger<GameBridgeService> _logger;

    public GameBridgeService(
        IGameTemplateService templateService,
        IGameRuntimeService runtimeService,
        GameModuleRegistry moduleRegistry,
        IGameIntentTranslationAgent translationAgent,
        IGameAgentTurnService agentTurnService,
        ParticipantChannelService channelService,
        GameVisibilityProjector visibilityProjector,
        IGameEventNarrationComposer narrationComposer,
        ILogger<GameBridgeService> logger)
    {
        _templateService = templateService;
        _runtimeService = runtimeService;
        _moduleRegistry = moduleRegistry;
        _translationAgent = translationAgent;
        _agentTurnService = agentTurnService;
        _channelService = channelService;
        _visibilityProjector = visibilityProjector;
        _narrationComposer = narrationComposer;
        _logger = logger;
    }

    public async Task<GameBridgeView> GetViewAsync(
        Guid sessionId,
        string? participantId = null,
        CancellationToken ct = default)
    {
        var runtime = await _runtimeService.LoadViewAsync(sessionId, ct);
        return ProjectView(runtime, participantId);
    }

    public async Task<SessionMutationResult<GameBridgeMutationResult>> StartFromTemplateAsync(
        Guid sessionId,
        StartGameFromTemplateCommand command,
        CancellationToken ct = default)
    {
        GameTemplateValidationEnvelope envelope;
        try
        {
            envelope = await _templateService.LoadAsync(command.TemplateId, ct);
        }
        catch (FileNotFoundException)
        {
            return SessionMutationResult<GameBridgeMutationResult>.Invalid(
                $"Game template '{command.TemplateId}' was not found.");
        }
        catch (ArgumentException ex)
        {
            return SessionMutationResult<GameBridgeMutationResult>.Invalid(ex.Message);
        }

        if (!envelope.Validation.IsValid)
        {
            var issue = envelope.Validation.Issues[0];
            return SessionMutationResult<GameBridgeMutationResult>.Invalid($"{issue.Code}: {issue.Message}");
        }

        var template = envelope.Template;
        var loadRequest = new GameModuleLoadRequest(
            new GameModuleId(template.Module.ModuleId),
            new GameModuleVersionRange(
                new GameModuleVersion(template.Module.MinimumVersion),
                new GameModuleVersion(template.Module.MaximumVersion)),
            new GameTemplateVersion(template.TemplateVersion));
        var module = _moduleRegistry.FindLoadable(loadRequest);
        if (module is null)
        {
            return SessionMutationResult<GameBridgeMutationResult>.Invalid(
                "No registered game module satisfies this template.");
        }

        var participants = BuildParticipants(template, command.UserDisplayName);
        var bindings = BuildParticipantBindings(template, participants, command.UserDisplayName);
        var gameInstanceId = new GameInstanceId($"game-{Guid.CreateVersion7():N}");
        var runtimeResult = await _runtimeService.StartAsync(
            sessionId,
            new StartGameRuntimeCommand(
                template.TemplateId,
                gameInstanceId,
                module.Descriptor.ModuleId,
                module.Descriptor.ModuleVersion,
                command.Seed ?? Random.Shared.NextInt64(1, long.MaxValue),
                new GameTemplateVersion(template.TemplateVersion),
                ToGameSetup(template.RulesOptions.Values),
                participants,
                bindings,
                template.Memory.TokenBudget,
                command.StartedAt,
                template.Communication.PublicChannelEnabled,
                template.Communication.DirectMessagesEnabled),
            ct);

        return await ToBridgeResultWithCoordinatorAsync(
            sessionId,
            runtimeResult,
            participantId: template.Roster.UserSeatParticipantId,
            command.StartedAt,
            ct);
    }

    public async Task<SessionMutationResult<GameBridgeMutationResult>> SubmitTypedActionAsync(
        Guid sessionId,
        SubmitGameTypedActionCommand command,
        CancellationToken ct = default)
    {
        var runtime = await _runtimeService.LoadViewAsync(sessionId, ct);
        if (runtime?.EngineSnapshot is null || string.IsNullOrWhiteSpace(runtime.GameInstanceId))
        {
            return SessionMutationResult<GameBridgeMutationResult>.Invalid("No game runtime is available for this session.");
        }

        return await SubmitTypedActionCoreAsync(sessionId, runtime, command, ct);
    }

    public async Task<SessionMutationResult<GameBridgeMutationResult>> SubmitTextActionAsync(
        Guid sessionId,
        SubmitGameTextActionCommand command,
        CancellationToken ct = default)
    {
        var runtime = await _runtimeService.LoadViewAsync(sessionId, ct);
        if (runtime?.EngineSnapshot is null || string.IsNullOrWhiteSpace(runtime.GameInstanceId))
        {
            return SessionMutationResult<GameBridgeMutationResult>.Invalid("No game runtime is available for this session.");
        }

        var liveState = runtime.EngineSnapshot.ToState();
        var projectionInput = GameVisibilityProjectionInput.FromState(liveState);
        PlayerGameProjection playerProjection;
        try
        {
            playerProjection = _visibilityProjector.ProjectPlayer(projectionInput, new ParticipantId(command.ParticipantId));
        }
        catch (ArgumentException ex)
        {
            return SessionMutationResult<GameBridgeMutationResult>.Invalid(ex.Message);
        }

        var translation = await _translationAgent.TranslateAsync(
            new GameIntentTranslationRequest(
                runtime.GameInstanceId,
                command.ParticipantId,
                command.Text,
                playerProjection.PendingInputs,
                command.OccurredAt),
            ct);
        if (!translation.IsAccepted)
        {
            _logger.LogInformation(
                "Game text action rejected by translator: session={SessionId} game={GameInstanceId} participant={ParticipantId} reason={ReasonCode}",
                sessionId,
                runtime.GameInstanceId,
                command.ParticipantId,
                translation.ReasonCode);
            return SessionMutationResult<GameBridgeMutationResult>.Invalid(
                $"{translation.ReasonCode}: {translation.Message}");
        }

        var translatedPendingInputId = translation.PendingInputId?.Trim();
        var translatedChoiceName = translation.ChoiceName?.Trim();
        var translatedActionIssue = ValidateTranslatedAction(
            playerProjection.PendingInputs,
            translatedPendingInputId,
            translatedChoiceName);
        if (translatedActionIssue is not null)
        {
            _logger.LogInformation(
                "Game text action rejected after translator returned illegal action: session={SessionId} game={GameInstanceId} participant={ParticipantId} reason={ReasonCode}",
                sessionId,
                runtime.GameInstanceId,
                command.ParticipantId,
                translatedActionIssue.ReasonCode);
            return SessionMutationResult<GameBridgeMutationResult>.Invalid(
                $"{translatedActionIssue.ReasonCode}: {translatedActionIssue.Message}");
        }

        var typed = new SubmitGameTypedActionCommand(
            command.ParticipantId,
            translatedPendingInputId!,
            translatedChoiceName!,
            command.OccurredAt);
        return await SubmitTypedActionCoreAsync(sessionId, runtime, typed, ct);
    }

    public async Task<SessionMutationResult<GameBridgeMutationResult>> PostPublicMessageAsync(
        Guid sessionId,
        PostGameRuntimePublicMessageCommand command,
        CancellationToken ct = default)
    {
        var result = await _runtimeService.PostPublicMessageAsync(sessionId, command, ct);
        return await ToBridgeResultAsync(sessionId, result, command.ParticipantId, ct);
    }

    public async Task<SessionMutationResult<GameBridgeMutationResult>> SendDirectMessageAsync(
        Guid sessionId,
        SendGameRuntimeDirectMessageCommand command,
        CancellationToken ct = default)
    {
        var result = await _runtimeService.SendDirectMessageAsync(sessionId, command, ct);
        return await ToBridgeResultAsync(sessionId, result, command.ParticipantId, ct);
    }

    public async Task<SessionMutationResult<GameBridgeMutationResult>> EndAsync(
        Guid sessionId,
        EndGameBridgeCommand command,
        CancellationToken ct = default)
    {
        var runtime = await _runtimeService.LoadViewAsync(sessionId, ct);
        if (runtime?.EngineSnapshot is null)
        {
            return SessionMutationResult<GameBridgeMutationResult>.Invalid("No game runtime is available for this session.");
        }

        var engineCommand = new EndGameIntentCommand(
            command.CommandId,
            runtime.EngineSnapshot.GameInstanceId,
            string.IsNullOrWhiteSpace(command.OutcomeName) ? "ended_by_host" : command.OutcomeName.Trim());
        var result = await _runtimeService.ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(engineCommand, command.EndedAt),
            ct);
        return await ToBridgeResultAsync(sessionId, result, null, ct);
    }

    public async Task<SessionMutationResult<GameBridgeMutationResult>> AbortAsync(
        Guid sessionId,
        AbortGameRuntimeCommand command,
        CancellationToken ct = default)
    {
        var result = await _runtimeService.AbortAsync(sessionId, command, ct);
        return await ToBridgeResultAsync(sessionId, result, null, ct);
    }

    private async Task<SessionMutationResult<GameBridgeMutationResult>> SubmitTypedActionCoreAsync(
        Guid sessionId,
        GameRuntimeState runtime,
        SubmitGameTypedActionCommand command,
        CancellationToken ct)
    {
        var engineCommand = new SubmitPlayerChoiceIntentCommand(
            GameIntentCommandId.NewId(),
            runtime.EngineSnapshot!.GameInstanceId,
            new PendingInputId(command.PendingInputId),
            new ParticipantId(command.ParticipantId),
            command.ChoiceName);
        var result = await _runtimeService.ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(engineCommand, command.OccurredAt),
            ct);
        return await ToBridgeResultWithCoordinatorAsync(sessionId, result, command.ParticipantId, command.OccurredAt, ct);
    }

    private async Task<SessionMutationResult<GameBridgeMutationResult>> ToBridgeResultAsync(
        Guid sessionId,
        SessionMutationResult<GameRuntimeMutationResult> result,
        string? participantId,
        CancellationToken ct)
    {
        if (result.Status != SessionMutationStatus.Success || result.Value is null)
        {
            return new SessionMutationResult<GameBridgeMutationResult>
            {
                Status = result.Status,
                Error = result.Error,
            };
        }

        var view = ProjectView(await _runtimeService.LoadViewAsync(sessionId, ct), participantId);
        return SessionMutationResult<GameBridgeMutationResult>.Success(
            GameBridgeMutationResult.FromRuntime(view, result.Value));
    }

    private async Task<SessionMutationResult<GameBridgeMutationResult>> ToBridgeResultWithCoordinatorAsync(
        Guid sessionId,
        SessionMutationResult<GameRuntimeMutationResult> result,
        string? participantId,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        if (result.Status != SessionMutationStatus.Success || result.Value is null)
        {
            return new SessionMutationResult<GameBridgeMutationResult>
            {
                Status = result.Status,
                Error = result.Error,
            };
        }

        var runtimeEvents = result.Value.RuntimeEvents.ToList();
        var engineEvents = result.Value.EngineEvents.ToList();
        var coordination = await CoordinatePendingGameWorkAsync(sessionId, occurredAt, ct);
        if (coordination.Status != SessionMutationStatus.Success)
        {
            return new SessionMutationResult<GameBridgeMutationResult>
            {
                Status = coordination.Status,
                Error = coordination.Error,
            };
        }

        if (coordination.Value is not null)
        {
            runtimeEvents.AddRange(coordination.Value.RuntimeEvents);
            engineEvents.AddRange(coordination.Value.EngineEvents);
        }

        var view = ProjectView(await _runtimeService.LoadViewAsync(sessionId, ct), participantId);
        return SessionMutationResult<GameBridgeMutationResult>.Success(new GameBridgeMutationResult(
            view,
            runtimeEvents,
            engineEvents,
            []));
    }

    private async Task<SessionMutationResult<GameBridgeCoordinationResult>> CoordinatePendingGameWorkAsync(
        Guid sessionId,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var runtimeEvents = new List<IGameRuntimeEvent>();
        var engineEvents = new List<IGameEvent>();
        var initialRuntime = await _runtimeService.LoadViewAsync(sessionId, ct);
        var passLimit = EstimateCoordinatorPassLimit(initialRuntime);
        var coordinatorRecords = new CoordinatorHostRecordBuffer(_runtimeService, _logger, sessionId);
        coordinatorRecords.Add(
            occurredAt,
            "coordinator_started",
            $"Game coordinator started after a start or typed player action. Public/direct messages and end/abort intentionally do not trigger coordination. Safety limit: {passLimit} pass(es).");

        for (var iteration = 0; iteration < passLimit; iteration++)
        {
            var changed = false;
            var passNumber = iteration + 1;
            var requestResult = await RequestStageInputsIfNeededAsync(sessionId, occurredAt, ct);
            if (requestResult.Status != SessionMutationStatus.Success)
            {
                await coordinatorRecords.FlushAsync(ct);
                return new SessionMutationResult<GameBridgeCoordinationResult>
                {
                    Status = requestResult.Status,
                    Error = requestResult.Error,
                };
            }

            if (requestResult.Value is not null)
            {
                changed = true;
                runtimeEvents.AddRange(requestResult.Value.RuntimeEvents);
                engineEvents.AddRange(requestResult.Value.EngineEvents);
                coordinatorRecords.Add(
                    occurredAt,
                    "coordinator_requested_pending_inputs",
                    $"Game coordinator pass {passNumber} requested pending input(s) for the current stage.");
            }

            var agentTurns = await _agentTurnService.RunPendingAgentTurnsAsync(
                sessionId,
                new RunGameAgentTurnsCommand(occurredAt),
                ct);
            if (agentTurns.Status != SessionMutationStatus.Success)
            {
                await coordinatorRecords.FlushAsync(ct);
                return new SessionMutationResult<GameBridgeCoordinationResult>
                {
                    Status = agentTurns.Status,
                    Error = agentTurns.Error,
                };
            }

            if (agentTurns.Value?.HasWork == true)
            {
                changed = true;
                runtimeEvents.AddRange(agentTurns.Value.RuntimeEvents);
                engineEvents.AddRange(agentTurns.Value.EngineEvents);
                coordinatorRecords.Add(
                    occurredAt,
                    "coordinator_ran_agent_turns",
                    $"Game coordinator pass {passNumber} ran pending agent turn work.");
            }

            if (!changed)
            {
                coordinatorRecords.Add(
                    occurredAt,
                    "coordinator_converged",
                    $"Game coordinator converged after {passNumber} pass(es) with no additional pending input requests or agent turns.");
                break;
            }

            if (passNumber == passLimit)
            {
                coordinatorRecords.Add(
                    occurredAt,
                    "coordinator_safety_limit_reached",
                    $"Game coordinator stopped after reaching the safety limit of {passLimit} pass(es); no-progress detection did not converge before the limit.");
            }
        }

        await coordinatorRecords.FlushAsync(ct);
        return SessionMutationResult<GameBridgeCoordinationResult>.Success(new GameBridgeCoordinationResult(runtimeEvents, engineEvents));
    }

    private static int EstimateCoordinatorPassLimit(GameRuntimeState? runtime)
    {
        var liveState = runtime?.EngineSnapshot?.ToState();
        if (liveState is null)
        {
            return MinimumCoordinatorPassLimit;
        }

        var activeParticipants = liveState.Participants.Count(participant => participant.IsActive);
        var waitingInputs = liveState.PendingInputs.Count(input => input.Status == PendingInputStatus.Waiting);
        return Math.Clamp(activeParticipants + waitingInputs + 2, MinimumCoordinatorPassLimit, MaximumCoordinatorPassLimit);
    }

    private async Task<SessionMutationResult<GameRuntimeMutationResult?>> RequestStageInputsIfNeededAsync(
        Guid sessionId,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var runtime = await _runtimeService.LoadViewAsync(sessionId, ct);
        if (runtime?.EngineSnapshot is null || string.IsNullOrWhiteSpace(runtime.GameInstanceId) || !runtime.IsActive)
        {
            return SessionMutationResult<GameRuntimeMutationResult?>.Success(null);
        }

        var liveState = runtime.EngineSnapshot.ToState();
        var module = _moduleRegistry.Find(liveState.ModuleId, liveState.ModuleVersion);
        if (module is null)
        {
            return SessionMutationResult<GameRuntimeMutationResult?>.Invalid("Registered game module is not available for this runtime.");
        }

        var form = SelectAutoRequestedActionForm(module, liveState.Stage.StageId);
        if (form is null)
        {
            return SessionMutationResult<GameRuntimeMutationResult?>.Success(null);
        }

        var existingParticipantIds = liveState.PendingInputs
            .Where(input => input.StageId == liveState.Stage.StageId
                && string.Equals(input.IntentName, form.IntentName, StringComparison.Ordinal)
                && input.Status is PendingInputStatus.Waiting or PendingInputStatus.Submitted)
            .Select(input => input.ParticipantId)
            .ToHashSet();

        var requestGroups = liveState.Participants
            .Where(participant => participant.IsActive && !existingParticipantIds.Contains(participant.ParticipantId))
            .Select(participant => new
            {
                participant.ParticipantId,
                LegalOptions = module.GetLegalIntentDescriptors(liveState, participant.ParticipantId)
                    .Where(descriptor => descriptor.StageId == liveState.Stage.StageId)
                    .Select(descriptor => new LegalIntentOption(descriptor.IntentName, descriptor.DisplayName, descriptor.Description))
                    .OrderBy(option => option.IntentName, StringComparer.Ordinal)
                    .ToArray()
            })
            .Where(item => item.LegalOptions.Length > 0)
            .GroupBy(item => LegalOptionsKey(item.LegalOptions))
            .ToArray();

        if (requestGroups.Length == 0)
        {
            return SessionMutationResult<GameRuntimeMutationResult?>.Success(null);
        }

        GameRuntimeState? latestGame = null;
        var runtimeEvents = new List<IGameRuntimeEvent>();
        var engineEvents = new List<IGameEvent>();
        foreach (var group in requestGroups)
        {
            var participantIds = group.Select(item => item.ParticipantId).OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
            var options = group.First().LegalOptions;
            var result = await _runtimeService.ApplyEngineCommandAsync(
                sessionId,
                new ApplyGameRuntimeEngineCommand(
                    new RequestPendingInputIntentCommand(
                        GameIntentCommandId.NewId(),
                        liveState.GameInstanceId,
                        liveState.Stage.StageId,
                        form.IntentName,
                        options,
                        PendingInputAudience.Many(participantIds)),
                    occurredAt),
                ct);
            if (result.Status != SessionMutationStatus.Success || result.Value is null)
            {
                return new SessionMutationResult<GameRuntimeMutationResult?>
                {
                    Status = result.Status,
                    Error = result.Error,
                };
            }

            latestGame = result.Value.Game;
            runtimeEvents.AddRange(result.Value.RuntimeEvents);
            engineEvents.AddRange(result.Value.EngineEvents);
        }

        return latestGame is null
            ? SessionMutationResult<GameRuntimeMutationResult?>.Success(null)
            : SessionMutationResult<GameRuntimeMutationResult?>.Success(new GameRuntimeMutationResult(latestGame, runtimeEvents, engineEvents));
    }

    private GameActionFormDescriptor? SelectAutoRequestedActionForm(IGameModule module, GameStageId stageId)
    {
        // LegalIntentDescriptor is currently stage-scoped rather than form-scoped, so the coordinator
        // intentionally auto-requests one deterministic action form per stage. Additional forms still
        // project to the UI through module authoring metadata, but modules that need multiple concurrent
        // automatic input groups must add form-scoped legal intent descriptors before the coordinator
        // can safely request them without duplicating ambiguous legal options.
        var forms = module.Descriptor.AuthoringHooks.ActionForms
            .Where(candidate => candidate.StageId == stageId)
            .OrderBy(candidate => candidate.IntentName, StringComparer.Ordinal)
            .ToArray();
        if (forms.Length > 1)
        {
            _logger.LogWarning(
                "Game coordinator found multiple action forms for stage {StageId} in module {ModuleId}; auto-requesting deterministic first form {IntentName} only.",
                stageId.Value,
                module.Descriptor.ModuleId.Value,
                forms[0].IntentName);
        }

        return forms.FirstOrDefault();
    }

    private static string LegalOptionsKey(IReadOnlyList<LegalIntentOption> options) =>
        string.Join("|", options.Select(option => $"{option.IntentName}:{option.DisplayName}:{option.Description}"));

    private sealed record GameBridgeCoordinationResult(
        IReadOnlyList<IGameRuntimeEvent> RuntimeEvents,
        IReadOnlyList<IGameEvent> EngineEvents);

    private sealed class CoordinatorHostRecordBuffer
    {
        private readonly IGameRuntimeService _runtimeService;
        private readonly ILogger<GameBridgeService> _logger;
        private readonly Guid _sessionId;
        private readonly List<AppendGameRuntimeHostRecordCommand> _records = [];

        public CoordinatorHostRecordBuffer(
            IGameRuntimeService runtimeService,
            ILogger<GameBridgeService> logger,
            Guid sessionId)
        {
            _runtimeService = runtimeService;
            _logger = logger;
            _sessionId = sessionId;
        }

        public void Add(DateTimeOffset occurredAt, string reasonCode, string summary)
        {
            var normalizedReasonCode = NormalizeRequiredText(reasonCode, nameof(reasonCode));
            var normalizedSummary = NormalizeRequiredText(summary, nameof(summary));
            _records.Add(new AppendGameRuntimeHostRecordCommand(
                GameRuntimeHostRecordKind.Coordinator,
                occurredAt,
                normalizedReasonCode,
                normalizedSummary));
        }

        private static string NormalizeRequiredText(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("Coordinator host records require non-empty text.", parameterName);
            }

            return value.Trim();
        }

        public async Task FlushAsync(CancellationToken ct)
        {
            if (_records.Count == 0)
            {
                return;
            }

            var records = _records.ToArray();
            _records.Clear();
            var result = await _runtimeService.AppendHostRecordsAsync(
                _sessionId,
                new AppendGameRuntimeHostRecordsCommand(records),
                ct);
            if (result.Status != SessionMutationStatus.Success)
            {
                _logger.LogWarning(
                    "Game coordinator host records were not persisted: session={SessionId} recordCount={RecordCount} reasonCodes={ReasonCodes} status={Status} error={Error}",
                    _sessionId,
                    records.Length,
                    string.Join(',', records.Select(record => record.ReasonCode)),
                    result.Status,
                    result.Error);
            }
        }
    }

    private async Task<SessionMutationResult<GameBridgeMutationResult>> ToBridgeResultAsync(
        Guid sessionId,
        SessionMutationResult<GameRuntimeCommunicationMutationResult> result,
        string? participantId,
        CancellationToken ct)
    {
        if (result.Status != SessionMutationStatus.Success || result.Value is null)
        {
            return new SessionMutationResult<GameBridgeMutationResult>
            {
                Status = result.Status,
                Error = result.Error,
            };
        }

        var view = ProjectView(await _runtimeService.LoadViewAsync(sessionId, ct), participantId);
        return SessionMutationResult<GameBridgeMutationResult>.Success(
            GameBridgeMutationResult.FromCommunication(view, result.Value));
    }

    private GameBridgeView ProjectView(GameRuntimeState? runtime, string? participantId)
    {
        if (runtime?.EngineSnapshot is null)
        {
            return new GameBridgeView(
                GameRuntimeStatus.NotStarted,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [],
                new GameBridgePublicView([], []),
                null);
        }

        var liveState = runtime.EngineSnapshot.ToState();
        var module = _moduleRegistry.Find(liveState.ModuleId, liveState.ModuleVersion);
        var moduleAuthoring = module is null ? null : ToModuleAuthoringView(module);
        var projectionInput = GameVisibilityProjectionInput.FromState(liveState);
        var publicProjection = _visibilityProjector.ProjectPublic(projectionInput.EventJournal);
        var publicFeed = _channelService.ProjectPublicFeed(runtime.Communication).Entries;
        var player = string.IsNullOrWhiteSpace(participantId)
            ? null
            : ProjectPlayer(runtime, projectionInput, participantId.Trim(), moduleAuthoring);

        return new GameBridgeView(
            runtime.Status,
            runtime.GameInstanceId,
            runtime.TemplateId,
            runtime.ModuleId,
            runtime.ModuleVersion,
            liveState.Round.RoundNumber,
            liveState.Stage.StageId.Value,
            liveState.Stage.DisplayName,
            BuildRoster(runtime, participantId),
            new GameBridgePublicView(
                publicProjection.Events.Select(eventView => ToNarrationEntry(liveState, eventView)).ToArray(),
                publicFeed),
            player)
        {
            ModuleAuthoring = moduleAuthoring,
        };
    }

    private static IReadOnlyList<GameBridgeParticipantView> BuildRoster(GameRuntimeState runtime, string? currentParticipantId)
    {
        var joinedIds = runtime.Communication.Participants
            .Where(participant => participant.IsJoined)
            .Select(participant => participant.ParticipantId.Value)
            .ToHashSet(StringComparer.Ordinal);
        var current = string.IsNullOrWhiteSpace(currentParticipantId) ? null : currentParticipantId.Trim();

        return runtime.ParticipantBindings
            .Select(binding => new GameBridgeParticipantView(
                binding.ParticipantId,
                binding.DisplayName,
                binding.Kind,
                joinedIds.Contains(binding.ParticipantId),
                current is not null && string.Equals(binding.ParticipantId, current, StringComparison.Ordinal)))
            .OrderBy(participant => participant.ParticipantId, StringComparer.Ordinal)
            .ToArray();
    }

    private GameBridgePlayerView? ProjectPlayer(
        GameRuntimeState runtime,
        GameVisibilityProjectionInput projectionInput,
        string participantId,
        GameBridgeModuleAuthoringView? moduleAuthoring)
    {
        PlayerGameProjection playerProjection;
        try
        {
            playerProjection = _visibilityProjector.ProjectPlayer(projectionInput, new ParticipantId(participantId));
        }
        catch (ArgumentException)
        {
            return null;
        }

        var feed = _channelService.ProjectParticipantFeed(runtime.Communication, new GameParticipantId(participantId));
        var cursor = runtime.EventDeliveryCursors.FirstOrDefault(item =>
            string.Equals(item.ParticipantId, participantId, StringComparison.Ordinal));
        return new GameBridgePlayerView(
            participantId,
            playerProjection.Participant.DisplayName,
            playerProjection.Events,
            playerProjection.PendingInputs,
            feed.Entries,
            cursor)
        {
            ActionForms = MatchActionForms(playerProjection.PendingInputs, moduleAuthoring),
        };
    }

    private GameBridgeNarrationEntry ToNarrationEntry(RulesGameState liveState, VisibleGameEvent gameEvent)
    {
        var typedEvent = liveState.EventJournal.Events.FirstOrDefault(item => item.EventId == gameEvent.EventId);
        var text = typedEvent is null
            ? $"{gameEvent.EventType} occurred."
            : _narrationComposer.ComposeSummary(typedEvent);
        return new GameBridgeNarrationEntry(
            gameEvent.EventId.ToString(),
            gameEvent.Sequence,
            gameEvent.EventType,
            text,
            gameEvent.OccurredAt);
    }

    private static GameBridgeModuleAuthoringView ToModuleAuthoringView(IGameModule module)
    {
        var descriptor = module.Descriptor;
        var requiredAssets = descriptor.RequiredPromptAssets
            .Select(asset => $"{asset.AssetId}:{asset.Kind}")
            .ToHashSet(StringComparer.Ordinal);

        return new GameBridgeModuleAuthoringView(
            descriptor.SetupFields.Select(ToSetupFieldView).ToArray(),
            descriptor.AuthoringHooks.Stages.Select(ToStageHookView).ToArray(),
            descriptor.AuthoringHooks.ActionForms.Select(ToActionFormView).ToArray(),
            module.GetPromptAssets()
                .Select(asset => new GameBridgePromptAssetView(
                    asset.AssetId,
                    asset.Kind.ToString(),
                    requiredAssets.Contains($"{asset.AssetId}:{asset.Kind}")))
                .ToArray(),
            new GameBridgeCommunicationCapabilitiesView(
                descriptor.CommunicationCapabilities.AllowsPublicChannelMessages,
                descriptor.CommunicationCapabilities.AllowsDirectMessages),
            new GameBridgeMemoryExpectationsView(
                descriptor.MemoryExpectations.UsesRoundSummaries,
                descriptor.MemoryExpectations.SuggestedSummaryTokenBudget,
                descriptor.MemoryExpectations.MaximumRetainedRoundSummaries),
            new GameBridgeProjectionCapabilitiesView(
                descriptor.AuthoringHooks.ProjectionCapabilities.SupportsPublicEventProjection,
                descriptor.AuthoringHooks.ProjectionCapabilities.SupportsParticipantPrivateProjection,
                descriptor.AuthoringHooks.ProjectionCapabilities.SupportsHostInspectorProjection));
    }

    private static GameBridgeSetupFieldView ToSetupFieldView(GameSetupFieldDescriptor field) =>
        new(field.Name, field.ValueKind.ToString(), field.IsRequired, field.DisplayName, field.Description);

    private static GameBridgeStageHookView ToStageHookView(GameStageDescriptor stage) =>
        new(
            stage.StageId.Value,
            stage.DisplayName,
            stage.Description,
            stage.Sequence,
            stage.AllowsPublicMessages,
            stage.AllowsDirectMessages);

    private static GameBridgeActionFormView ToActionFormView(GameActionFormDescriptor form) =>
        new(
            form.IntentName,
            form.StageId.Value,
            form.DisplayName,
            form.Description,
            form.Layout.ToString(),
            form.Fields
                .Select(field => new GameBridgeActionFieldView(
                    field.Name,
                    field.ValueKind.ToString(),
                    field.IsRequired,
                    field.DisplayName,
                    field.Description))
                .ToArray());

    private static IReadOnlyList<GameBridgeActionFormView> MatchActionForms(
        IReadOnlyList<PendingInputState> pendingInputs,
        GameBridgeModuleAuthoringView? moduleAuthoring)
    {
        if (moduleAuthoring is null || pendingInputs.Count == 0)
        {
            return [];
        }

        return pendingInputs
            .Select(input => moduleAuthoring.ActionForms.FirstOrDefault(form =>
                string.Equals(form.IntentName, input.IntentName, StringComparison.Ordinal)
                && string.Equals(form.StageId, input.StageId.Value, StringComparison.Ordinal)))
            .OfType<GameBridgeActionFormView>()
            .DistinctBy(form => $"{form.StageId}:{form.IntentName}")
            .ToArray();
    }

    private static IReadOnlyList<ParticipantSetup> BuildParticipants(GameTemplate template, string? userDisplayName)
    {
        var participants = new List<ParticipantSetup>();
        if (!string.IsNullOrWhiteSpace(template.Roster.UserSeatParticipantId))
        {
            participants.Add(new ParticipantSetup(
                new ParticipantId(template.Roster.UserSeatParticipantId.Trim()),
                NormalizeDisplayName(userDisplayName, "User"),
                ParticipantKind.Human));
        }

        foreach (var agent in template.Roster.AgentPlayers)
        {
            participants.Add(new ParticipantSetup(
                new ParticipantId(agent.ParticipantId),
                NormalizeDisplayName(agent.FixedName, agent.ParticipantId),
                ParticipantKind.Agent));
        }

        return participants;
    }

    private static IReadOnlyList<GameRuntimeParticipantBinding> BuildParticipantBindings(
        GameTemplate template,
        IReadOnlyList<ParticipantSetup> participants,
        string? userDisplayName)
    {
        var agentByParticipant = template.Roster.AgentPlayers.ToDictionary(
            agent => agent.ParticipantId,
            StringComparer.Ordinal);
        var bindings = new List<GameRuntimeParticipantBinding>();
        foreach (var participant in participants)
        {
            if (agentByParticipant.TryGetValue(participant.ParticipantId.Value, out var agent))
            {
                bindings.Add(new GameRuntimeParticipantBinding
                {
                    ParticipantId = participant.ParticipantId.Value,
                    DisplayName = participant.DisplayName,
                    Kind = GameRuntimeParticipantKind.Agent,
                    ProviderAlias = agent.ProviderAlias,
                    ModelOverride = agent.ModelOverride,
                    CharacterPrompt = agent.CharacterPrompt,
                    Personality = agent.Personality,
                    PersonaPrompt = agent.PersonaPrompt,
                    SystemPromptTemplate = agent.SystemPromptTemplate,
                });
                continue;
            }

            bindings.Add(new GameRuntimeParticipantBinding
            {
                ParticipantId = participant.ParticipantId.Value,
                DisplayName = NormalizeDisplayName(userDisplayName, participant.DisplayName),
                Kind = GameRuntimeParticipantKind.Human,
                UserSeatId = participant.ParticipantId.Value,
            });
        }

        return bindings;
    }

    private static GameSetup ToGameSetup(IReadOnlyList<GameTemplateRuleOptionValue> values) =>
        new(values.Select(ToGameSetupValue).ToArray());

    private static GameSetupValue ToGameSetupValue(GameTemplateRuleOptionValue value) =>
        value.Kind switch
        {
            GameTemplateRuleOptionValueKind.String => new StringGameSetupValue(value.Name, value.StringValue ?? string.Empty),
            GameTemplateRuleOptionValueKind.Int => new IntGameSetupValue(value.Name, value.IntValue ?? 0),
            GameTemplateRuleOptionValueKind.Bool => new BoolGameSetupValue(value.Name, value.BoolValue ?? false),
            GameTemplateRuleOptionValueKind.ParticipantId => new ParticipantIdGameSetupValue(value.Name, new ParticipantId(value.ParticipantIdValue ?? string.Empty)),
            GameTemplateRuleOptionValueKind.ParticipantSet => new ParticipantSetGameSetupValue(value.Name, value.ParticipantSetValue.Select(item => new ParticipantId(item)).ToArray()),
            _ => throw new ArgumentException($"Unsupported template rule option kind '{value.Kind}'.", nameof(value)),
        };

    private static GameIntentTranslationResult? ValidateTranslatedAction(
        IReadOnlyList<PendingInputState> pendingInputs,
        string? pendingInputId,
        string? choiceName)
    {
        if (string.IsNullOrWhiteSpace(pendingInputId) || string.IsNullOrWhiteSpace(choiceName))
        {
            return GameIntentTranslationResult.Rejected(
                "translator_missing_action",
                "The game input translator omitted the pending input or choice name.");
        }

        var input = pendingInputs.FirstOrDefault(item =>
            string.Equals(item.PendingInputId.Value, pendingInputId, StringComparison.Ordinal));
        if (input is null)
        {
            return GameIntentTranslationResult.Rejected(
                "translator_unknown_pending_input",
                $"The game input translator selected unknown pending input '{pendingInputId}'.");
        }

        if (!input.LegalOptions.Any(option => string.Equals(option.IntentName, choiceName, StringComparison.Ordinal)))
        {
            return GameIntentTranslationResult.Rejected(
                "translator_illegal_choice",
                $"The game input translator selected illegal choice '{choiceName}' for pending input '{pendingInputId}'.");
        }

        return null;
    }

    private static string NormalizeDisplayName(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
