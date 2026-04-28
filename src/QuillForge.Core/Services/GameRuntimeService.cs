using Den.RulesEngine;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class GameRuntimeService : IGameRuntimeService
{
    private readonly ISessionStateStore _store;
    private readonly ISessionMutationGate _gate;
    private readonly GameModuleRegistry _moduleRegistry;
    private readonly RulesEngineService _rulesEngine;
    private readonly ParticipantChannelService _communicationService;
    private readonly IGameEventNarrationComposer _narrationComposer;
    private readonly ILogger<GameRuntimeService> _logger;

    public GameRuntimeService(
        ISessionStateStore store,
        ISessionMutationGate gate,
        GameModuleRegistry moduleRegistry,
        RulesEngineService rulesEngine,
        ParticipantChannelService communicationService,
        IGameEventNarrationComposer narrationComposer,
        ILogger<GameRuntimeService> logger)
    {
        _store = store;
        _gate = gate;
        _moduleRegistry = moduleRegistry;
        _rulesEngine = rulesEngine;
        _communicationService = communicationService;
        _narrationComposer = narrationComposer;
        _logger = logger;
    }

    public async Task<GameRuntimeState?> LoadViewAsync(Guid sessionId, CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(sessionId, ct);
        return GameRuntimeStateCloner.Clone(state.Game);
    }

    public async Task<SessionMutationResult<GameRuntimeMutationResult>> StartAsync(
        Guid sessionId,
        StartGameRuntimeCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "start_game_runtime";
        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return Busy();
        }

        var state = await _store.LoadAsync(sessionId, ct);
        if (state.Game?.IsActive == true)
        {
            _logger.LogWarning(
                "Game runtime start rejected: session={SessionId} activeGame={GameInstanceId} status={Status}",
                sessionId,
                state.Game.GameInstanceId,
                state.Game.Status);
            return SessionMutationResult<GameRuntimeMutationResult>.Invalid("A game is already active for this session.");
        }

        var bindingIssue = ValidateParticipantBindings(command);
        if (bindingIssue is not null)
        {
            return SessionMutationResult<GameRuntimeMutationResult>.Invalid(bindingIssue);
        }

        var registration = _moduleRegistry.ValidateRegistered(command.ModuleId, command.ModuleVersion);
        if (!registration.IsValid)
        {
            return Invalid(registration.Issues[0]);
        }

        var module = _moduleRegistry.Find(command.ModuleId, command.ModuleVersion);
        if (module is null)
        {
            return SessionMutationResult<GameRuntimeMutationResult>.Invalid("Requested game module is not registered.");
        }

        var setupValidation = new GameSetupValidationService(_moduleRegistry).Validate(
            command.ModuleId,
            command.ModuleVersion,
            command.TemplateVersion,
            command.Setup,
            command.Participants);
        if (!setupValidation.IsValid)
        {
            return Invalid(setupValidation.Issues[0]);
        }

        var participantStates = command.Participants
            .Select(participant => new ParticipantState(
                participant.ParticipantId,
                participant.DisplayName,
                participant.Kind,
                []))
            .ToArray();
        var initialState = RulesGameState.CreateNotStarted(
            command.GameInstanceId,
            module.Descriptor,
            command.Seed,
            participantStates);
        var startIntent = new StartGameIntentCommand(
            GameIntentCommandId.NewId(),
            command.GameInstanceId,
            command.ModuleId,
            command.ModuleVersion,
            command.Seed,
            command.Setup,
            command.Participants);
        var applyResult = _rulesEngine.Apply(initialState, startIntent);
        if (!applyResult.IsAccepted)
        {
            return Invalid(applyResult.Issues[0]);
        }

        var runtime = CreateRuntime(command, applyResult.State, applyResult.Events);
        LinkEngineEventsToCommunication(runtime, applyResult.State, applyResult.Events);
        state.Game = runtime;
        await _store.SaveAsync(state, ct);

        _logger.LogInformation(
            "Game runtime started: session={SessionId} game={GameInstanceId} module={ModuleId} version={ModuleVersion} status={Status}",
            sessionId,
            runtime.GameInstanceId,
            runtime.ModuleId,
            runtime.ModuleVersion,
            runtime.Status);

        var runtimeEvent = new GameRuntimeStartedEvent(
            runtime.GameInstanceId!,
            runtime.TemplateId,
            runtime.ModuleId!,
            runtime.ModuleVersion!,
            runtime.Status,
            command.StartedAt);
        return SessionMutationResult<GameRuntimeMutationResult>.Success(
            new GameRuntimeMutationResult(GameRuntimeStateCloner.Clone(runtime)!, [runtimeEvent], applyResult.Events));
    }

    public async Task<SessionMutationResult<GameRuntimeMutationResult>> ApplyEngineCommandAsync(
        Guid sessionId,
        ApplyGameRuntimeEngineCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "apply_game_engine_command";
        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return Busy();
        }

        var state = await _store.LoadAsync(sessionId, ct);
        var runtime = state.Game;
        if (runtime?.EngineSnapshot is null)
        {
            return SessionMutationResult<GameRuntimeMutationResult>.Invalid("No game runtime is available for this session.");
        }

        if (!string.Equals(runtime.GameInstanceId, command.EngineCommand.GameInstanceId.Value, StringComparison.Ordinal))
        {
            return SessionMutationResult<GameRuntimeMutationResult>.Invalid("Engine command targets a different game instance.");
        }

        var liveState = runtime.EngineSnapshot.ToState();
        var applyResult = _rulesEngine.Apply(liveState, command.EngineCommand);
        if (!applyResult.IsAccepted)
        {
            runtime.EngineSnapshot = RulesGameStateSnapshot.FromState(applyResult.State);
            runtime.Status = ToRuntimeStatus(applyResult.State.Status);
            runtime.LastUpdatedAt = command.OccurredAt;
            GameRuntimeStateCloner.AppendHostRecord(
                runtime,
                GameRuntimeHostRecordKind.EngineCommandApplied,
                command.OccurredAt,
                applyResult.Issues[0].Code,
                $"Engine command rejected: {applyResult.Issues[0].Message}");
            await _store.SaveAsync(state, ct);
            return Invalid(applyResult.Issues[0]);
        }

        UpdateRuntimeFromApplyResult(runtime, applyResult.State, command.OccurredAt);
        LinkEngineEventsToCommunication(runtime, applyResult.State, applyResult.Events);
        var hostRecordKind = runtime.Status == GameRuntimeStatus.Aborted
            ? GameRuntimeHostRecordKind.Aborted
            : GameRuntimeHostRecordKind.EngineCommandApplied;
        GameRuntimeStateCloner.AppendHostRecord(
            runtime,
            hostRecordKind,
            command.OccurredAt,
            "engine_command_applied",
            $"Applied {command.EngineCommand.GetType().Name}.");
        await _store.SaveAsync(state, ct);

        _logger.LogInformation(
            "Game engine command applied: session={SessionId} game={GameInstanceId} command={CommandType} status={Status} eventCount={EventCount}",
            sessionId,
            runtime.GameInstanceId,
            command.EngineCommand.GetType().Name,
            runtime.Status,
            applyResult.Events.Count);

        var runtimeEvent = new GameRuntimeEngineCommandAppliedEvent(
            runtime.GameInstanceId!,
            command.EngineCommand.CommandId,
            command.EngineCommand.GetType().Name,
            runtime.Status,
            applyResult.Events,
            command.OccurredAt);
        return SessionMutationResult<GameRuntimeMutationResult>.Success(
            new GameRuntimeMutationResult(GameRuntimeStateCloner.Clone(runtime)!, [runtimeEvent], applyResult.Events));
    }

    public async Task<SessionMutationResult<GameRuntimeMutationResult>> ResumeAsync(
        Guid sessionId,
        ResumeGameRuntimeCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "resume_game_runtime";
        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return Busy();
        }

        var state = await _store.LoadAsync(sessionId, ct);
        var runtime = state.Game;
        if (runtime?.EngineSnapshot is null || string.IsNullOrWhiteSpace(runtime.GameInstanceId))
        {
            return SessionMutationResult<GameRuntimeMutationResult>.Invalid("No game runtime is available for this session.");
        }

        runtime.LastResumedAt = command.ResumedAt;
        runtime.LastUpdatedAt = command.ResumedAt;
        GameRuntimeStateCloner.AppendHostRecord(
            runtime,
            GameRuntimeHostRecordKind.Resumed,
            command.ResumedAt,
            "resumed_session",
            "Game runtime resumed from persisted session state.");
        await _store.SaveAsync(state, ct);

        var runtimeEvent = new GameRuntimeResumedEvent(runtime.GameInstanceId, runtime.Status, command.ResumedAt);
        return SessionMutationResult<GameRuntimeMutationResult>.Success(
            new GameRuntimeMutationResult(GameRuntimeStateCloner.Clone(runtime)!, [runtimeEvent], []));
    }

    public Task<SessionMutationResult<GameRuntimeMutationResult>> AbortAsync(
        Guid sessionId,
        AbortGameRuntimeCommand command,
        CancellationToken ct = default)
    {
        return AbortAsyncCore(sessionId, command, ct);
    }

    public Task<SessionMutationResult<GameRuntimeCommunicationMutationResult>> PostPublicMessageAsync(
        Guid sessionId,
        PostGameRuntimePublicMessageCommand command,
        CancellationToken ct = default)
    {
        return ApplyCommunicationAsync(
            sessionId,
            "post_game_public_message",
            command.CreatedAt,
            runtime =>
            {
                var participant = FindParticipantBinding(runtime, command.ParticipantId);
                if (participant is null)
                {
                    return ParticipantCommunicationApplyResult.Rejected(new ParticipantCommunicationIssue(
                        "unknown_participant",
                        $"Participant '{command.ParticipantId}' is not part of the active game."));
                }

                return _communicationService.PostPublicMessage(
                    runtime.Communication,
                    new PostParticipantChannelMessageCommand(
                        command.MessageId,
                        new ParticipantMessageAuthor(new GameParticipantId(participant.ParticipantId), command.AuthorKind),
                        command.Text,
                        command.CreatedAt),
                    BuildCommunicationPermissions(runtime));
            },
            ct);
    }

    public Task<SessionMutationResult<GameRuntimeCommunicationMutationResult>> SendDirectMessageAsync(
        Guid sessionId,
        SendGameRuntimeDirectMessageCommand command,
        CancellationToken ct = default)
    {
        return ApplyCommunicationAsync(
            sessionId,
            "send_game_direct_message",
            command.CreatedAt,
            runtime =>
            {
                var participant = FindParticipantBinding(runtime, command.ParticipantId);
                if (participant is null)
                {
                    return ParticipantCommunicationApplyResult.Rejected(new ParticipantCommunicationIssue(
                        "unknown_participant",
                        $"Participant '{command.ParticipantId}' is not part of the active game."));
                }

                return _communicationService.SendDirectMessage(
                    runtime.Communication,
                    new SendParticipantDirectMessageCommand(
                        command.MessageId,
                        new ParticipantMessageAuthor(new GameParticipantId(participant.ParticipantId), command.AuthorKind),
                        command.RecipientParticipantIds.Select(id => new GameParticipantId(id)).ToArray(),
                        command.Text,
                        command.CreatedAt),
                    BuildCommunicationPermissions(runtime));
            },
            ct);
    }

    public async Task<SessionMutationResult<GameRuntimePromptMutationResult>> RecordAgentPromptAsync(
        Guid sessionId,
        RecordGameRuntimeAgentPromptCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "record_game_agent_prompt";
        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<GameRuntimePromptMutationResult>.Busy(
                "Another mutating operation is already running for this session.");
        }

        var state = await _store.LoadAsync(sessionId, ct);
        var runtime = state.Game;
        if (runtime?.EngineSnapshot is null || string.IsNullOrWhiteSpace(runtime.GameInstanceId))
        {
            return SessionMutationResult<GameRuntimePromptMutationResult>.Invalid(
                "No game runtime is available for this session.");
        }

        var participant = FindParticipantBinding(runtime, command.ParticipantId);
        if (participant is null || participant.Kind != GameRuntimeParticipantKind.Agent)
        {
            return SessionMutationResult<GameRuntimePromptMutationResult>.Invalid(
                $"Participant '{command.ParticipantId}' is not an agent participant in this game.");
        }

        var cursor = runtime.PromptCursors.FirstOrDefault(item =>
            string.Equals(item.ParticipantId, command.ParticipantId, StringComparison.Ordinal));
        if (cursor is null)
        {
            cursor = new GameRuntimeAgentPromptDeliveryCursor { ParticipantId = command.ParticipantId };
            runtime.PromptCursors.Add(cursor);
        }

        cursor.LastDeliveredPublicEngineEventSequence = Math.Max(
            cursor.LastDeliveredPublicEngineEventSequence,
            command.EngineCursorSequence);
        cursor.DeliveredPrivateEventIds = cursor.DeliveredPrivateEventIds
            .Concat(command.DeliveredPrivateEventIds)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        cursor.CommunicationDeliveredThroughSequence = Math.Max(
            cursor.CommunicationDeliveredThroughSequence,
            command.CommunicationCursorSequence);
        cursor.MemoryRevision = command.MemoryRevision;
        cursor.LastPromptEnvelopeId = command.EnvelopeId;

        var eventCursor = runtime.EventDeliveryCursors.FirstOrDefault(item =>
            string.Equals(item.ParticipantId, command.ParticipantId, StringComparison.Ordinal));
        if (eventCursor is not null)
        {
            eventCursor.DeliveredThroughEngineEventSequence = Math.Max(
                eventCursor.DeliveredThroughEngineEventSequence,
                command.EngineCursorSequence);
            eventCursor.DeliveredThroughCommunicationSequence = Math.Max(
                eventCursor.DeliveredThroughCommunicationSequence,
                command.CommunicationCursorSequence);
            eventCursor.MemoryRevision = command.MemoryRevision;
            eventCursor.LastPromptEnvelopeId = command.EnvelopeId;
        }

        runtime.PromptEnvelopes.Add(new GameRuntimeAgentPromptEnvelope
        {
            EnvelopeId = command.EnvelopeId,
            ParticipantId = command.ParticipantId,
            CreatedAt = command.CreatedAt,
            EngineCursorSequence = command.EngineCursorSequence,
            CommunicationCursorSequence = command.CommunicationCursorSequence,
            MemoryRevision = command.MemoryRevision,
            ProviderAlias = NormalizeChoice(command.ProviderAlias),
            Model = NormalizeChoice(command.Model),
            PromptTokens = command.PromptTokens,
            ResponseTokens = command.ResponseTokens,
            PromptContentHash = command.PromptContentHash,
            ResponseContentHash = command.ResponseContentHash,
            PromptText = NormalizeChoice(command.PromptText),
            ResponseText = NormalizeChoice(command.ResponseText),
        });
        TrimPromptEnvelopes(runtime, command.ParticipantId, command.MaxPromptEnvelopesPerAgent);
        runtime.LastUpdatedAt = command.CreatedAt;
        GameRuntimeStateCloner.AppendHostRecord(
            runtime,
            GameRuntimeHostRecordKind.AgentPromptRecorded,
            command.CreatedAt,
            "agent_prompt_recorded",
            $"Recorded agent prompt envelope for participant '{command.ParticipantId}'.");
        await _store.SaveAsync(state, ct);

        var runtimeEvent = new GameRuntimeAgentPromptRecordedEvent(
            runtime.GameInstanceId,
            command.EnvelopeId,
            command.ParticipantId,
            NormalizeChoice(command.ProviderAlias),
            NormalizeChoice(command.Model),
            command.PromptTokens,
            command.ResponseTokens,
            command.CreatedAt);
        return SessionMutationResult<GameRuntimePromptMutationResult>.Success(
            new GameRuntimePromptMutationResult(GameRuntimeStateCloner.Clone(runtime)!, [runtimeEvent]));
    }

    public async Task<SessionMutationResult<GameRuntimeMemorySummaryMutationResult>> RecordAgentMemorySummaryAsync(
        Guid sessionId,
        RecordGameRuntimeAgentMemorySummaryCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "record_game_agent_memory_summary";
        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<GameRuntimeMemorySummaryMutationResult>.Busy(
                "Another mutating operation is already running for this session.");
        }

        var state = await _store.LoadAsync(sessionId, ct);
        var runtime = state.Game;
        if (runtime?.EngineSnapshot is null || string.IsNullOrWhiteSpace(runtime.GameInstanceId))
        {
            return SessionMutationResult<GameRuntimeMemorySummaryMutationResult>.Invalid(
                "No game runtime is available for this session.");
        }

        var participant = FindParticipantBinding(runtime, command.ParticipantId);
        if (participant is null || participant.Kind != GameRuntimeParticipantKind.Agent)
        {
            return SessionMutationResult<GameRuntimeMemorySummaryMutationResult>.Invalid(
                $"Participant '{command.ParticipantId}' is not an agent participant in this game.");
        }

        var memory = runtime.AgentMemories.FirstOrDefault(item =>
            string.Equals(item.ParticipantId, command.ParticipantId, StringComparison.Ordinal));
        if (memory is null)
        {
            memory = new GameRuntimeAgentMemoryState { ParticipantId = command.ParticipantId };
            runtime.AgentMemories.Add(memory);
        }

        var recordedSummary = command.Decision.RejectionReason is null && !string.IsNullOrWhiteSpace(command.Summary);
        if (recordedSummary)
        {
            memory.Revision++;
            memory.Summary = command.Summary;
            memory.ContentHash = command.SummaryContentHash;
            memory.LastSummarizedRoundNumber = Math.Max(memory.LastSummarizedRoundNumber, command.Decision.RoundNumber);
            memory.LastSummarizedPublicEngineEventSequence = command.Decision.NewCursor.PublicEngineEventSequence;
            memory.LastSummarizedPrivateEventIds = command.Decision.NewCursor.PrivateEngineEventIds.ToList();
            memory.LastSummarizedCommunicationSequence = command.Decision.NewCursor.CommunicationSequence;
            memory.UpdatedAt = command.CreatedAt;
        }

        var promptCursor = runtime.PromptCursors.FirstOrDefault(item =>
            string.Equals(item.ParticipantId, command.ParticipantId, StringComparison.Ordinal));
        if (promptCursor is null)
        {
            promptCursor = new GameRuntimeAgentPromptDeliveryCursor { ParticipantId = command.ParticipantId };
            runtime.PromptCursors.Add(promptCursor);
        }

        promptCursor.LastDeliveredPublicEngineEventSequence = command.Decision.NewCursor.PublicEngineEventSequence;
        promptCursor.DeliveredPrivateEventIds = command.Decision.NewCursor.PrivateEngineEventIds.ToList();
        promptCursor.CommunicationDeliveredThroughSequence = command.Decision.NewCursor.CommunicationSequence;
        promptCursor.MemoryRevision = memory.Revision;
        promptCursor.LastPromptEnvelopeId = command.EnvelopeId;

        var eventCursor = runtime.EventDeliveryCursors.FirstOrDefault(item =>
            string.Equals(item.ParticipantId, command.ParticipantId, StringComparison.Ordinal));
        if (eventCursor is not null)
        {
            eventCursor.DeliveredThroughEngineEventSequence = command.Decision.NewCursor.PublicEngineEventSequence;
            eventCursor.DeliveredThroughCommunicationSequence = command.Decision.NewCursor.CommunicationSequence;
            eventCursor.MemoryRevision = memory.Revision;
            eventCursor.LastPromptEnvelopeId = command.EnvelopeId;
        }

        var decision = command.Decision with
        {
            NewCursor = command.Decision.NewCursor with { MemoryRevision = memory.Revision },
            SummaryContentHash = command.SummaryContentHash,
        };
        runtime.MemorySummaryDecisions.RemoveAll(item =>
            string.Equals(item.ParticipantId, decision.ParticipantId, StringComparison.Ordinal)
            && item.RoundNumber == decision.RoundNumber);
        runtime.MemorySummaryDecisions.Add(decision);
        runtime.PromptEnvelopes.Add(new GameRuntimeAgentPromptEnvelope
        {
            EnvelopeId = command.EnvelopeId,
            ParticipantId = command.ParticipantId,
            CreatedAt = command.CreatedAt,
            EngineCursorSequence = decision.NewCursor.PublicEngineEventSequence,
            CommunicationCursorSequence = decision.NewCursor.CommunicationSequence,
            MemoryRevision = memory.Revision,
            ProviderAlias = NormalizeChoice(command.ProviderAlias),
            Model = NormalizeChoice(command.Model),
            PromptTokens = command.PromptTokens,
            ResponseTokens = command.ResponseTokens,
            PromptContentHash = command.PromptContentHash,
            ResponseContentHash = command.ResponseContentHash,
            PromptText = command.PromptText,
            ResponseText = command.ResponseText,
        });
        TrimPromptEnvelopes(runtime, command.ParticipantId, command.MaxPromptEnvelopesPerAgent);
        runtime.LastUpdatedAt = command.CreatedAt;
        GameRuntimeStateCloner.AppendHostRecord(
            runtime,
            GameRuntimeHostRecordKind.AgentMemorySummaryRecorded,
            command.CreatedAt,
            recordedSummary ? "agent_memory_summary_recorded" : "agent_memory_summary_rejected",
            recordedSummary
                ? $"Recorded agent memory summary for participant '{command.ParticipantId}' round {decision.RoundNumber}."
                : $"Recorded rejected agent memory summary decision for participant '{command.ParticipantId}' round {decision.RoundNumber}.");
        await _store.SaveAsync(state, ct);

        var runtimeEvent = new GameRuntimeAgentMemorySummaryRecordedEvent(
            runtime.GameInstanceId,
            decision.DecisionId,
            command.ParticipantId,
            decision.RoundNumber,
            memory.Revision,
            NormalizeChoice(command.ProviderAlias),
            NormalizeChoice(command.Model),
            command.PromptTokens,
            command.ResponseTokens,
            command.CreatedAt);
        return SessionMutationResult<GameRuntimeMemorySummaryMutationResult>.Success(
            new GameRuntimeMemorySummaryMutationResult(GameRuntimeStateCloner.Clone(runtime)!, [runtimeEvent]));
    }

    private async Task<SessionMutationResult<GameRuntimeMutationResult>> AbortAsyncCore(
        Guid sessionId,
        AbortGameRuntimeCommand command,
        CancellationToken ct)
    {
        var state = await _store.LoadAsync(sessionId, ct);
        var runtime = state.Game;
        if (runtime?.EngineSnapshot is null || string.IsNullOrWhiteSpace(runtime.GameInstanceId))
        {
            return SessionMutationResult<GameRuntimeMutationResult>.Invalid("No game runtime is available for this session.");
        }

        var abortIntent = new AbortGameIntentCommand(
            command.CommandId,
            runtime.EngineSnapshot.GameInstanceId,
            NormalizeReasonCode(command.ReasonCode));
        var result = await ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(abortIntent, command.AbortedAt),
            ct);
        if (result.Status != SessionMutationStatus.Success || result.Value is null)
        {
            return result;
        }

        var runtimeEvent = new GameRuntimeAbortedEvent(
            result.Value.Game.GameInstanceId!,
            abortIntent.ReasonCode,
            result.Value.Game.Status,
            command.AbortedAt);
        return SessionMutationResult<GameRuntimeMutationResult>.Success(
            result.Value with
            {
                RuntimeEvents = result.Value.RuntimeEvents.Concat([runtimeEvent]).ToArray()
            });
    }

    private async Task<SessionMutationResult<GameRuntimeCommunicationMutationResult>> ApplyCommunicationAsync(
        Guid sessionId,
        string operationName,
        DateTimeOffset occurredAt,
        Func<GameRuntimeState, ParticipantCommunicationApplyResult> apply,
        CancellationToken ct)
    {
        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return SessionMutationResult<GameRuntimeCommunicationMutationResult>.Busy(
                "Another mutating operation is already running for this session.");
        }

        var state = await _store.LoadAsync(sessionId, ct);
        var runtime = state.Game;
        if (runtime?.EngineSnapshot is null || string.IsNullOrWhiteSpace(runtime.GameInstanceId))
        {
            return SessionMutationResult<GameRuntimeCommunicationMutationResult>.Invalid(
                "No game runtime is available for this session.");
        }

        if (!runtime.IsActive)
        {
            return SessionMutationResult<GameRuntimeCommunicationMutationResult>.Invalid(
                "The game runtime is not active.");
        }

        var communicationResult = apply(runtime);
        if (!communicationResult.IsAccepted)
        {
            return SessionMutationResult<GameRuntimeCommunicationMutationResult>.Invalid(
                communicationResult.Issues[0].Message);
        }

        runtime.LastUpdatedAt = occurredAt;
        await _store.SaveAsync(state, ct);

        return SessionMutationResult<GameRuntimeCommunicationMutationResult>.Success(
            new GameRuntimeCommunicationMutationResult(
                GameRuntimeStateCloner.Clone(runtime)!,
                communicationResult.Events));
    }

    private static GameRuntimeState CreateRuntime(
        StartGameRuntimeCommand command,
        RulesGameState startedState,
        IReadOnlyList<IGameEvent> events)
    {
        var runtime = new GameRuntimeState
        {
            Status = ToRuntimeStatus(startedState.Status),
            GameInstanceId = command.GameInstanceId.Value,
            TemplateId = NormalizeChoice(command.TemplateId),
            ModuleId = command.ModuleId.Value,
            ModuleVersion = command.ModuleVersion.Value,
            Seed = command.Seed,
            StartedAt = command.StartedAt,
            LastUpdatedAt = command.StartedAt,
            EngineSnapshot = RulesGameStateSnapshot.FromState(startedState),
            ParticipantBindings = command.ParticipantBindings.Select(CloneBinding).ToList(),
            Communication = CreateCommunicationState(command.ParticipantBindings),
            HostAllowsPublicMessages = command.HostAllowsPublicMessages,
            HostAllowsDirectMessages = command.HostAllowsDirectMessages,
            EventDeliveryCursors = command.ParticipantBindings.Select(binding => new GameRuntimeEventDeliveryCursor
            {
                ParticipantId = binding.ParticipantId,
            }).ToList(),
            PromptCursors = command.ParticipantBindings
                .Where(binding => binding.Kind == GameRuntimeParticipantKind.Agent)
                .Select(binding => new GameRuntimeAgentPromptDeliveryCursor
                {
                    ParticipantId = binding.ParticipantId,
                }).ToList(),
            AgentMemories = command.ParticipantBindings
                .Where(binding => binding.Kind == GameRuntimeParticipantKind.Agent)
                .Select(binding => new GameRuntimeAgentMemoryState
                {
                    ParticipantId = binding.ParticipantId,
                    TokenBudget = command.AgentMemoryTokenBudget,
                }).ToList(),
        };

        GameRuntimeStateCloner.AppendHostRecord(
            runtime,
            GameRuntimeHostRecordKind.Started,
            command.StartedAt,
            "game_started",
            $"Game runtime started with {events.Count} engine event(s).");

        return runtime;
    }

    private static void UpdateRuntimeFromApplyResult(GameRuntimeState runtime, RulesGameState state, DateTimeOffset occurredAt)
    {
        runtime.EngineSnapshot = RulesGameStateSnapshot.FromState(state);
        runtime.Status = ToRuntimeStatus(state.Status);
        runtime.LastUpdatedAt = occurredAt;
        if (runtime.Status is GameRuntimeStatus.Ended or GameRuntimeStatus.Aborted)
        {
            runtime.EndedAt = occurredAt;
        }
    }

    private static ParticipantCommunicationState CreateCommunicationState(
        IReadOnlyList<GameRuntimeParticipantBinding> bindings)
    {
        var communication = new ParticipantCommunicationState();
        var sequence = communication.NextSequence;
        foreach (var binding in bindings)
        {
            var participantId = new GameParticipantId(binding.ParticipantId);
            communication.Participants.Add(new ParticipantPresenceState
            {
                ParticipantId = participantId,
                DisplayName = binding.DisplayName,
                IsJoined = true,
                JoinedSequence = sequence,
            });
            communication.Cursors.Add(new ParticipantCommunicationCursor
            {
                ParticipantId = participantId,
            });
            sequence++;
        }

        communication.NextSequence = sequence;
        return communication;
    }

    private static GameRuntimeStatus ToRuntimeStatus(RulesGameStatus status) => status switch
    {
        RulesGameStatus.NotStarted => GameRuntimeStatus.NotStarted,
        RulesGameStatus.Running => GameRuntimeStatus.Running,
        RulesGameStatus.WaitingForInput => GameRuntimeStatus.WaitingForInput,
        RulesGameStatus.Resolving => GameRuntimeStatus.Resolving,
        RulesGameStatus.Ended => GameRuntimeStatus.Ended,
        RulesGameStatus.Aborted => GameRuntimeStatus.Aborted,
        _ => GameRuntimeStatus.NotStarted,
    };

    private static GameRuntimeParticipantBinding CloneBinding(GameRuntimeParticipantBinding binding) => new()
    {
        ParticipantId = binding.ParticipantId,
        DisplayName = binding.DisplayName,
        Kind = binding.Kind,
        ProviderAlias = binding.ProviderAlias,
        ModelOverride = binding.ModelOverride,
        CharacterPrompt = binding.CharacterPrompt,
        Personality = binding.Personality,
        UserSeatId = binding.UserSeatId,
    };

    private static GameRuntimeParticipantBinding? FindParticipantBinding(GameRuntimeState runtime, string participantId) =>
        runtime.ParticipantBindings.FirstOrDefault(binding =>
            string.Equals(binding.ParticipantId, participantId, StringComparison.Ordinal));

    private ParticipantCommunicationPermissions BuildCommunicationPermissions(GameRuntimeState runtime)
    {
        var stage = runtime.EngineSnapshot?.Stage;
        var stageId = stage?.StageId.Value ?? string.Empty;
        var module = runtime.EngineSnapshot is null
            ? null
            : _moduleRegistry.Find(runtime.EngineSnapshot.ModuleId, runtime.EngineSnapshot.ModuleVersion);
        var capabilities = module?.Descriptor.CommunicationCapabilities;
        return new ParticipantCommunicationPermissions(
            stageId,
            runtime.HostAllowsPublicMessages,
            (capabilities?.AllowsPublicChannelMessages ?? false) && (stage?.AllowsPublicMessages ?? false),
            runtime.HostAllowsDirectMessages,
            (capabilities?.AllowsDirectMessages ?? false) && (stage?.AllowsDirectMessages ?? false),
            []);
    }

    private void LinkEngineEventsToCommunication(
        GameRuntimeState runtime,
        RulesGameState liveState,
        IReadOnlyList<IGameEvent> events)
    {
        foreach (var gameEvent in events.OrderBy(item => item.Sequence))
        {
            if (runtime.Communication.GameEventLinks.Any(link => string.Equals(link.GameEventId, gameEvent.EventId.ToString(), StringComparison.Ordinal)))
            {
                continue;
            }

            var link = ToParticipantGameEventLinkCommand(liveState, gameEvent, _narrationComposer);
            if (link is null)
            {
                continue;
            }

            var result = _communicationService.LinkGameEvent(runtime.Communication, link);
            if (!result.IsAccepted)
            {
                _logger.LogWarning(
                    "Game event communication link rejected: game={GameInstanceId} event={EventId} reason={ReasonCode}",
                    runtime.GameInstanceId,
                    gameEvent.EventId.ToString(),
                    result.Issues[0].Code);
            }
        }
    }

    private static LinkParticipantGameEventCommand? ToParticipantGameEventLinkCommand(
        RulesGameState liveState,
        IGameEvent gameEvent,
        IGameEventNarrationComposer narrationComposer)
    {
        var (visibility, visibleTo) = ToCommunicationVisibility(liveState, gameEvent.Visibility);
        if (visibility is null)
        {
            return null;
        }

        return new LinkParticipantGameEventCommand(
            Guid.CreateVersion7(),
            gameEvent.EventId.ToString(),
            gameEvent.Sequence,
            visibility.Value,
            visibleTo,
            narrationComposer.ComposeSummary(gameEvent),
            gameEvent.OccurredAt);
    }

    private static (ParticipantGameEventLinkVisibility? Visibility, IReadOnlyList<GameParticipantId> VisibleTo) ToCommunicationVisibility(
        RulesGameState liveState,
        GameEventVisibility visibility)
    {
        return visibility.Kind switch
        {
            GameEventVisibilityKind.Public => (ParticipantGameEventLinkVisibility.Public, []),
            GameEventVisibilityKind.PrivateToParticipant when visibility.ParticipantId is { } participantId =>
                (ParticipantGameEventLinkVisibility.PrivateToParticipantSet, [new GameParticipantId(participantId.Value)]),
            GameEventVisibilityKind.PrivateToSet when visibility.ParticipantSetId is { } participantSetId =>
                (ParticipantGameEventLinkVisibility.PrivateToParticipantSet,
                    liveState.Participants
                        .Where(participant => participant.ParticipantSetIds.Contains(participantSetId))
                        .Select(participant => new GameParticipantId(participant.ParticipantId.Value))
                        .ToArray()),
            _ => (null, []),
        };
    }

    private static string? ValidateParticipantBindings(StartGameRuntimeCommand command)
    {
        if (command.Participants.Count == 0)
        {
            return "At least one engine participant is required.";
        }

        var participantIds = command.Participants
            .Select(participant => participant.ParticipantId.Value)
            .ToHashSet(StringComparer.Ordinal);
        if (participantIds.Count != command.Participants.Count)
        {
            return "Engine participant IDs must be unique.";
        }

        var bindingIds = command.ParticipantBindings
            .Select(binding => NormalizeChoice(binding.ParticipantId) ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        if (bindingIds.Count != command.ParticipantBindings.Count || bindingIds.Contains(string.Empty))
        {
            return "Runtime participant bindings must have unique participant IDs.";
        }

        if (!participantIds.SetEquals(bindingIds))
        {
            return "Runtime participant bindings must match engine participants.";
        }

        return null;
    }

    private static void TrimPromptEnvelopes(
        GameRuntimeState runtime,
        string participantId,
        int maxPromptEnvelopesPerAgent)
    {
        var maxCount = Math.Max(1, maxPromptEnvelopesPerAgent);
        var participantEnvelopes = runtime.PromptEnvelopes
            .Where(item => string.Equals(item.ParticipantId, participantId, StringComparison.Ordinal))
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.EnvelopeId, StringComparer.Ordinal)
            .Skip(maxCount)
            .Select(item => item.EnvelopeId)
            .ToHashSet(StringComparer.Ordinal);
        if (participantEnvelopes.Count == 0)
        {
            return;
        }

        runtime.PromptEnvelopes.RemoveAll(item => participantEnvelopes.Contains(item.EnvelopeId));
    }

    private static SessionMutationResult<GameRuntimeMutationResult> Busy() =>
        SessionMutationResult<GameRuntimeMutationResult>.Busy(
            "Another mutating operation is already running for this session.");

    private static SessionMutationResult<GameRuntimeMutationResult> Invalid(ValidationIssue issue) =>
        SessionMutationResult<GameRuntimeMutationResult>.Invalid($"{issue.Code}: {issue.Message}");

    private static string NormalizeReasonCode(string? reasonCode) =>
        string.IsNullOrWhiteSpace(reasonCode) ? "aborted_by_host" : reasonCode.Trim();

    private static string? NormalizeChoice(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
