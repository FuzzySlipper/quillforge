namespace Den.RulesEngine;

public sealed class RulesEngineService
{
    private readonly GameModuleRegistry _moduleRegistry;
    private readonly IRulesEngineObserver _observer;

    public RulesEngineService(GameModuleRegistry moduleRegistry, IRulesEngineObserver? observer = null)
    {
        _moduleRegistry = moduleRegistry;
        _observer = observer ?? new NoOpRulesEngineObserver();
    }

    public RulesEngineApplyResult Apply(RulesGameState state, IGameIntentCommand command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);

        var moduleLookup = ResolveModule(state, command);
        if (moduleLookup.Issue is not null)
        {
            return Reject(state, command, moduleLookup.Issue);
        }

        var validation = GameIntentCommandValidationService.Validate(state, command);
        if (!validation.IsAccepted)
        {
            return Reject(state, command, validation.Issues[0]);
        }

        var module = moduleLookup.Module!;
        return command switch
        {
            StartGameIntentCommand start => ApplyStartGame(state, module, start),
            RequestPendingInputIntentCommand request => ApplyRequestPendingInput(state, request),
            SubmitPlayerChoiceIntentCommand submit => ApplySubmitPlayerChoice(state, module, submit),
            AdvanceStageIntentCommand advanceStage => ApplyAdvanceStage(state, advanceStage),
            EndRoundIntentCommand endRound => ApplyEndRound(state, endRound),
            AdvanceDeterministicEffectsIntentCommand advance => ApplyModuleCommand(state, module, advance, appendDefaultAdvanceEvent: true),
            EndGameIntentCommand end => ApplyEndGame(state, end),
            AbortGameIntentCommand abort => ApplyAbortGame(state, abort),
            _ => Reject(state, command, new ValidationIssue("unknown_intent_command", "Intent command type is not recognized."))
        };
    }

    private RulesEngineApplyResult ApplyStartGame(
        RulesGameState state,
        IGameModule module,
        StartGameIntentCommand command)
    {
        var setupValidation = new GameSetupValidationService(_moduleRegistry).Validate(
            command.ModuleId,
            module.Descriptor.MinimumTemplateVersion,
            command.Setup,
            command.Participants);

        if (!setupValidation.IsValid)
        {
            return Reject(state, command, setupValidation.Issues[0]);
        }

        var initialized = module.CreateInitialState(new GameSetupInitializationContext(
            command.GameInstanceId,
            module.Descriptor,
            command.Setup,
            command.Participants,
            command.Seed));

        var working = initialized with
        {
            Status = RulesGameStatus.Running,
            EventJournal = state.EventJournal
        };

        var started = GameStartedEvent.Create(command.GameInstanceId, command.ModuleId, command.ModuleVersion, command.Seed);
        return AcceptWithEvents(working, [started]);
    }

    private RulesEngineApplyResult ApplyRequestPendingInput(
        RulesGameState state,
        RequestPendingInputIntentCommand command)
    {
        var targets = ResolveAudienceTargets(state, command.Audience);
        var pendingInputs = state.PendingInputs.ToList();
        var events = new List<IGameEvent>();

        foreach (var participantId in targets)
        {
            var pendingInputId = CreatePendingInputId(command.CommandId, participantId);
            pendingInputs.Add(new PendingInputState(
                pendingInputId,
                participantId,
                command.StageId,
                command.IntentName,
                PendingInputStatus.Waiting,
                command.LegalOptions.ToArray()));
            events.Add(PendingInputRequestedEvent.Create(
                state.GameInstanceId,
                pendingInputId,
                participantId,
                command.StageId,
                command.IntentName));
        }

        var next = state with
        {
            Status = RulesGameStatus.WaitingForInput,
            PendingInputs = pendingInputs.ToArray()
        };

        return AcceptWithEvents(next, events);
    }

    private RulesEngineApplyResult ApplySubmitPlayerChoice(
        RulesGameState state,
        IGameModule module,
        SubmitPlayerChoiceIntentCommand command)
    {
        var pendingInputs = state.PendingInputs
            .Select(input => input.PendingInputId == command.PendingInputId
                ? input with { Status = PendingInputStatus.Submitted }
                : input)
            .ToArray();
        var working = state with { PendingInputs = pendingInputs };
        var submitted = PlayerChoiceSubmittedEvent.Create(
            state.GameInstanceId,
            command.PendingInputId,
            command.ParticipantId,
            command.ChoiceName,
            GameEventVisibility.PrivateToParticipant(command.ParticipantId));

        var serviceResult = AcceptWithEvents(working, [submitted]);
        var moduleResult = ApplyModulePhases(serviceResult.State, module, command, []);

        return moduleResult with
        {
            Events = serviceResult.Events.Concat(moduleResult.Events).ToArray()
        };
    }

    private RulesEngineApplyResult ApplyAdvanceStage(RulesGameState state, AdvanceStageIntentCommand command)
    {
        var next = state with
        {
            Status = RulesGameStatus.Running,
            Stage = command.NextStage
        };
        var gameEvent = StageAdvancedEvent.Create(
            state.GameInstanceId,
            state.Stage.StageId,
            command.NextStage.StageId);

        return AcceptWithEvents(next, [gameEvent]);
    }

    private RulesEngineApplyResult ApplyEndRound(RulesGameState state, EndRoundIntentCommand command)
    {
        var nextRoundNumber = state.Round.RoundNumber + 1;
        var next = state with
        {
            Status = RulesGameStatus.Running,
            Round = new GameRoundState(nextRoundNumber),
            PendingInputs = []
        };

        return AcceptWithEvents(next,
        [
            RoundEndedEvent.Create(state.GameInstanceId, state.Round.RoundNumber, command.ReasonCode),
            RoundStartedEvent.Create(state.GameInstanceId, nextRoundNumber)
        ]);
    }

    private RulesEngineApplyResult ApplyModuleCommand(
        RulesGameState state,
        IGameModule module,
        IGameIntentCommand command,
        bool appendDefaultAdvanceEvent)
    {
        var result = ApplyModulePhases(state, module, command, []);
        if (!result.IsAccepted || !appendDefaultAdvanceEvent || command is not AdvanceDeterministicEffectsIntentCommand advance)
        {
            return result;
        }

        if (result.Events.Count > 0)
        {
            return result;
        }

        var defaultAdvance = AcceptWithEvents(
            result.State,
            [DeterministicEffectsAdvancedEvent.Create(state.GameInstanceId, advance.EffectName)]);

        return defaultAdvance with { TraceRecords = result.TraceRecords };
    }

    private RulesEngineApplyResult ApplyEndGame(RulesGameState state, EndGameIntentCommand command)
    {
        var next = state with
        {
            Status = RulesGameStatus.Ended,
            PendingInputs = []
        };

        return AcceptWithEvents(next, [GameEndedEvent.Create(state.GameInstanceId, command.OutcomeName)]);
    }

    private RulesEngineApplyResult ApplyAbortGame(RulesGameState state, AbortGameIntentCommand command)
    {
        var next = state with
        {
            Status = RulesGameStatus.Aborted,
            PendingInputs = []
        };

        return AcceptWithEvents(next, [GameAbortedEvent.Create(state.GameInstanceId, command.ReasonCode)]);
    }

    private RulesEngineApplyResult ApplyModulePhases(
        RulesGameState state,
        IGameModule module,
        IGameIntentCommand command,
        IReadOnlyList<EngineTraceRecord> priorTraceRecords)
    {
        var working = state;
        var traceRecords = priorTraceRecords.ToList();
        var commandEvents = new List<IGameEvent>();

        foreach (var phase in OrderedPhases())
        {
            var journalBeforePhase = working.EventJournal;
            var transition = module.HandleIntentCommand(new GameModuleTransitionContext(working, command, phase));
            var traceRecord = CreateTraceRecord(module, working, command, phase, transition);
            traceRecords.Add(traceRecord);
            _observer.Record(traceRecord);

            if (!transition.IsAccepted)
            {
                var rejected = Reject(working, command, transition.Issues[0]);
                return rejected with
                {
                    TraceRecords = traceRecords.ToArray(),
                    Events = commandEvents.Concat(rejected.Events).ToArray()
                };
            }

            working = transition.State.WithEventJournal(journalBeforePhase);
            var appendResult = AcceptWithEvents(working, transition.Events);
            working = appendResult.State;
            commandEvents.AddRange(appendResult.Events);
        }

        return new RulesEngineApplyResult(working, commandEvents.ToArray(), traceRecords.ToArray(), []);
    }

    private static RulesResolutionPhase[] OrderedPhases() =>
    [
        RulesResolutionPhase.CanStart,
        RulesResolutionPhase.OnRun,
        RulesResolutionPhase.OnEnd
    ];

    private RulesEngineApplyResult Reject(RulesGameState state, IGameIntentCommand command, ValidationIssue issue)
    {
        var rejected = IntentCommandRejectedEvent.Create(command, issue.Code, issue.Message);
        var journal = state.EventJournal.Append(rejected);
        var committed = journal.Events[^1];
        var next = state.WithEventJournal(journal);

        return new RulesEngineApplyResult(next, [committed], [], [issue]);
    }

    private static RulesEngineApplyResult AcceptWithEvents(RulesGameState state, IReadOnlyList<IGameEvent> events)
    {
        var journal = state.EventJournal;
        var committedEvents = new List<IGameEvent>();
        foreach (var gameEvent in events)
        {
            journal = journal.Append(gameEvent);
            committedEvents.Add(journal.Events[^1]);
        }

        return new RulesEngineApplyResult(state.WithEventJournal(journal), committedEvents.ToArray(), [], []);
    }

    private (IGameModule? Module, ValidationIssue? Issue) ResolveModule(RulesGameState state, IGameIntentCommand command)
    {
        var moduleId = command is StartGameIntentCommand start ? start.ModuleId : state.ModuleId;
        var moduleVersion = command is StartGameIntentCommand startVersion ? startVersion.ModuleVersion : state.ModuleVersion;
        var module = _moduleRegistry.Find(moduleId);
        if (module is null)
        {
            return (null, new ValidationIssue("unknown_module_id", $"Module '{moduleId}' is not registered."));
        }

        if (module.Descriptor.ModuleVersion != moduleVersion)
        {
            return (null, new ValidationIssue(
                "unsupported_module_version",
                $"Module '{moduleId}' version '{moduleVersion}' is not registered."));
        }

        return (module, null);
    }

    private static IReadOnlyList<ParticipantId> ResolveAudienceTargets(RulesGameState state, PendingInputAudience audience)
    {
        return audience.Kind switch
        {
            PendingInputAudienceKind.OneParticipant => audience.ParticipantId is null
                ? []
                : [audience.ParticipantId.Value],
            PendingInputAudienceKind.ManyParticipants => audience.ParticipantIds.ToArray(),
            PendingInputAudienceKind.AllActiveParticipants => state.Participants
                .Where(participant => participant.IsActive && participant.Kind != ParticipantKind.System)
                .Select(participant => participant.ParticipantId)
                .ToArray(),
            _ => []
        };
    }

    private static PendingInputId CreatePendingInputId(GameIntentCommandId commandId, ParticipantId participantId) =>
        new($"{commandId.Value:D}:{participantId.Value}");

    private static EngineTraceRecord CreateTraceRecord(
        IGameModule module,
        RulesGameState state,
        IGameIntentCommand command,
        RulesResolutionPhase phase,
        GameModuleTransitionResult transition)
    {
        var issue = transition.Issues.FirstOrDefault();
        return new EngineTraceRecord(
            state.GameInstanceId,
            module.Descriptor.ModuleId,
            module.Descriptor.ModuleVersion,
            state.EventJournal.NextSequence,
            command.GetType().Name,
            phase,
            module.GetType().Name,
            0,
            transition.IsAccepted ? "accepted" : "rejected",
            issue?.Code);
    }
}

public sealed record RulesEngineApplyResult(
    RulesGameState State,
    IReadOnlyList<IGameEvent> Events,
    IReadOnlyList<EngineTraceRecord> TraceRecords,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsAccepted => Issues.Count == 0;
}
