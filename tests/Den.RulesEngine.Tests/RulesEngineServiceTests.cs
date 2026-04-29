namespace Den.RulesEngine.Tests;

public sealed class RulesEngineServiceTests
{
    [Fact]
    public void Apply_StartGame_InitializesStateAndCommitsStartFact()
    {
        var module = new TransitionTestModule();
        var service = CreateService(module);
        var participants = CreateParticipants();
        var state = RulesGameState.CreateNotStarted(GameId, module.Descriptor, 123, []);
        var command = new StartGameIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            module.Descriptor.ModuleId,
            module.Descriptor.ModuleVersion,
            123,
            GameSetup.Empty,
            participants);

        var result = service.Apply(state, command);

        Assert.True(result.IsAccepted);
        Assert.Equal(RulesGameStatus.Running, result.State.Status);
        Assert.Equal(participants.Select(participant => participant.ParticipantId), result.State.Participants.Select(participant => participant.ParticipantId));
        var started = Assert.IsType<GameStartedEvent>(Assert.Single(result.Events));
        Assert.Equal(1, started.Sequence);
        Assert.Equal(123, started.Seed);
    }

    [Fact]
    public void Apply_RequestPendingInput_SupportsOneManyAndAllParticipants()
    {
        var module = new TransitionTestModule();
        var service = CreateService(module);
        var state = CreateRunningState(module);
        var option = new LegalIntentOption("vote", "Vote", "Choose a target.");

        var one = service.Apply(state, new RequestPendingInputIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            DayStage.StageId,
            "vote",
            [option],
            PendingInputAudience.One(Alice)));
        var many = service.Apply(one.State, new RequestPendingInputIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            DayStage.StageId,
            "vote",
            [option],
            PendingInputAudience.Many([Alice, Bob])));
        var all = service.Apply(many.State, new RequestPendingInputIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            DayStage.StageId,
            "vote",
            [option],
            PendingInputAudience.AllActiveParticipants));

        Assert.True(one.IsAccepted);
        Assert.True(many.IsAccepted);
        Assert.True(all.IsAccepted);
        Assert.Equal(6, all.State.PendingInputs.Count);
        Assert.All(all.State.PendingInputs, input => Assert.Equal(PendingInputStatus.Waiting, input.Status));
        Assert.Equal(6, all.State.EventJournal.Events.OfType<PendingInputRequestedEvent>().Count());
    }

    [Fact]
    public void Apply_SubmitPlayerChoice_UpdatesPendingInputAndPreservesPrivateVisibility()
    {
        var module = new TransitionTestModule();
        var service = CreateService(module);
        var state = CreateRunningState(module);
        var option = new LegalIntentOption("vote", "Vote", "Choose a target.");
        var requested = service.Apply(state, new RequestPendingInputIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            DayStage.StageId,
            "vote",
            [option],
            PendingInputAudience.One(Alice)));
        var pendingInput = Assert.Single(requested.State.PendingInputs);

        var submitted = service.Apply(requested.State, new SubmitPlayerChoiceIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            pendingInput.PendingInputId,
            Alice,
            "vote"));

        Assert.True(submitted.IsAccepted);
        Assert.Equal(PendingInputStatus.Submitted, Assert.Single(submitted.State.PendingInputs).Status);
        Assert.Contains(submitted.Events, gameEvent => gameEvent is PlayerChoiceSubmittedEvent);

        var projector = new GameVisibilityProjector();
        var aliceProjection = projector.ProjectPlayer(GameVisibilityProjectionInput.FromState(submitted.State), Alice);
        var bobProjection = projector.ProjectPlayer(GameVisibilityProjectionInput.FromState(submitted.State), Bob);

        Assert.Contains(aliceProjection.Events, gameEvent => gameEvent.EventType == nameof(PlayerChoiceSubmittedEvent));
        Assert.DoesNotContain(bobProjection.Events, gameEvent => gameEvent.EventType == nameof(PlayerChoiceSubmittedEvent));
    }

    [Fact]
    public void Apply_SubmitPlayerChoice_RejectsOutOfStageActionsWithCommittedFact()
    {
        var module = new TransitionTestModule();
        var service = CreateService(module);
        var state = CreateRunningState(module);
        var option = new LegalIntentOption("vote", "Vote", "Choose a target.");
        var requested = service.Apply(state, new RequestPendingInputIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            DayStage.StageId,
            "vote",
            [option],
            PendingInputAudience.One(Alice)));
        var pendingInput = Assert.Single(requested.State.PendingInputs);
        var advanced = service.Apply(requested.State, new AdvanceStageIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            NightStage));

        var submitted = service.Apply(advanced.State, new SubmitPlayerChoiceIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            pendingInput.PendingInputId,
            Alice,
            "vote"));

        Assert.False(submitted.IsAccepted);
        Assert.Equal("out_of_stage", Assert.Single(submitted.Issues).Code);
        var rejected = Assert.IsType<IntentCommandRejectedEvent>(Assert.Single(submitted.Events));
        Assert.Equal("out_of_stage", rejected.ReasonCode);
        Assert.Equal(3, rejected.Sequence);
    }

    [Fact]
    public void Apply_EndRound_EmitsRoundBoundaryFactsAndLeavesMemoryUpdatesOutOfScope()
    {
        var module = new TransitionTestModule();
        var service = CreateService(module);
        var state = CreateRunningState(module) with
        {
            PendingInputs =
            [
                new PendingInputState(
                    new PendingInputId("input-1"),
                    Alice,
                    DayStage.StageId,
                    "vote",
                    PendingInputStatus.Waiting,
                    [new LegalIntentOption("vote", "Vote", "Choose a target.")])
            ]
        };

        var result = service.Apply(state, new EndRoundIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            "all_inputs_resolved"));

        Assert.True(result.IsAccepted);
        Assert.Equal(2, result.State.Round.RoundNumber);
        Assert.Empty(result.State.PendingInputs);
        Assert.Collection(
            result.Events,
            gameEvent => Assert.IsType<RoundEndedEvent>(gameEvent),
            gameEvent => Assert.IsType<RoundStartedEvent>(gameEvent));
    }

    [Fact]
    public void Apply_AdvanceDeterministicEffects_IgnoresModuleJournalMutationAndOwnsSequencing()
    {
        var module = new TransitionTestModule(tamperJournalOnAdvance: true);
        var service = CreateService(module);
        var state = CreateRunningState(module);
        var command = new AdvanceDeterministicEffectsIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            "advance");

        var result = service.Apply(state, command);

        Assert.True(result.IsAccepted);
        Assert.DoesNotContain(result.State.EventJournal.Events, gameEvent => gameEvent is GameEndedEvent);
        var advanced = Assert.IsType<DeterministicEffectsAdvancedEvent>(Assert.Single(result.Events));
        Assert.Equal(1, advanced.Sequence);
    }

    [Fact]
    public void Apply_AdvanceDeterministicEffects_ReplaysSameRngEventsAndTraceRecords()
    {
        var module = new TransitionTestModule(useRandomOnAdvance: true);
        var observer = new RecordingRulesEngineObserver();
        var service = CreateService(module, observer);
        var state = CreateRunningState(module);
        var command = new AdvanceDeterministicEffectsIntentCommand(
            new GameIntentCommandId(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")),
            GameId,
            "resolve-night");

        var first = service.Apply(state, command);
        var replay = service.Apply(state, command);

        Assert.True(first.IsAccepted);
        Assert.True(replay.IsAccepted);
        Assert.Equal(first.State.Random, replay.State.Random);
        Assert.Equal(first.Events.Select(gameEvent => gameEvent.EventId), replay.Events.Select(gameEvent => gameEvent.EventId));
        Assert.Equal(
            first.Events.OfType<DeterministicEffectsAdvancedEvent>().Single().EffectName,
            replay.Events.OfType<DeterministicEffectsAdvancedEvent>().Single().EffectName);
        Assert.Equal(
            [RulesResolutionPhase.CanStart, RulesResolutionPhase.OnRun, RulesResolutionPhase.OnEnd],
            first.TraceRecords.Select(record => record.Phase).ToArray());
        Assert.Equal(6, observer.Records.Count);
    }

    private static RulesEngineService CreateService(TransitionTestModule module, IRulesEngineObserver? observer = null)
    {
        var registry = new GameModuleRegistry();
        var result = registry.Register(module);
        Assert.True(result.IsValid);

        return new RulesEngineService(registry, observer);
    }

    private static RulesGameState CreateRunningState(TransitionTestModule module)
    {
        return RulesGameState.CreateNotStarted(
            GameId,
            module.Descriptor,
            777,
            [
                new ParticipantState(Alice, "Alice", ParticipantKind.Human, []),
                new ParticipantState(Bob, "Bob", ParticipantKind.Agent, []),
                new ParticipantState(Chandra, "Chandra", ParticipantKind.Agent, []),
                new ParticipantState(new ParticipantId("system"), "System", ParticipantKind.System, [])
            ]) with
        {
            Status = RulesGameStatus.Running,
            Round = new GameRoundState(1),
            Stage = DayStage
        };
    }

    private static ParticipantSetup[] CreateParticipants() =>
    [
        new ParticipantSetup(Alice, "Alice", ParticipantKind.Human),
        new ParticipantSetup(Bob, "Bob", ParticipantKind.Agent),
        new ParticipantSetup(Chandra, "Chandra", ParticipantKind.Agent)
    ];

    private static readonly GameInstanceId GameId = new("game-832");
    private static readonly ParticipantId Alice = new("alice");
    private static readonly ParticipantId Bob = new("bob");
    private static readonly ParticipantId Chandra = new("chandra");
    private static readonly GameStageState DayStage = new(new GameStageId("day"), "Day", 1, true, false);
    private static readonly GameStageState NightStage = new(new GameStageId("night"), "Night", 2, false, true);

    private sealed class TransitionTestModule : IGameModule
    {
        private readonly bool _useRandomOnAdvance;
        private readonly bool _tamperJournalOnAdvance;

        public TransitionTestModule(bool useRandomOnAdvance = false, bool tamperJournalOnAdvance = false)
        {
            _useRandomOnAdvance = useRandomOnAdvance;
            _tamperJournalOnAdvance = tamperJournalOnAdvance;
        }

        public GameModuleDescriptor Descriptor { get; } = new(
            new GameModuleId("transition-test"),
            new GameModuleVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            "Transition Test",
            new PlayerCountRange(1, 8),
            []);

        public ValidationResult ValidateSetup(GameSetupValidationContext context) => ValidationResult.Valid;

        public RulesGameState CreateInitialState(GameSetupInitializationContext context)
        {
            var participants = context.Participants
                .Select(participant => new ParticipantState(
                    participant.ParticipantId,
                    participant.DisplayName,
                    participant.Kind,
                    []))
                .ToArray();

            return RulesGameState.CreateNotStarted(
                context.GameInstanceId,
                context.Descriptor,
                context.Seed,
                participants);
        }

        public IReadOnlyList<LegalIntentDescriptor> GetLegalIntentDescriptors(RulesGameState state, ParticipantId participantId) => [];

        public GameModuleTransitionResult HandleIntentCommand(GameModuleTransitionContext context)
        {
            if (context.Command is not AdvanceDeterministicEffectsIntentCommand advance
                || context.Phase != RulesResolutionPhase.OnRun)
            {
                return GameModuleTransitionResult.Accepted(context.State, []);
            }

            if (_tamperJournalOnAdvance)
            {
                var tamperedJournal = context.State.EventJournal.Append(GameEndedEvent.Create(context.State.GameInstanceId, "tampered"));
                return GameModuleTransitionResult.Accepted(context.State.WithEventJournal(tamperedJournal), []);
            }

            if (!_useRandomOnAdvance)
            {
                return GameModuleTransitionResult.Accepted(context.State, []);
            }

            var draw = context.State.Random.NextInt(1000);
            var next = context.State with { Random = draw.State };
            return GameModuleTransitionResult.Accepted(
                next,
                [DeterministicEffectsAdvancedEvent.Create(context.State.GameInstanceId, $"{advance.EffectName}:{draw.Value}")]);
        }

        public IReadOnlyList<GameRuleHandlerDescriptor> GetRuleHandlerDescriptors() =>
            [new GameRuleHandlerDescriptor("advance", RulesResolutionPhase.OnRun, nameof(TransitionTestModule), 0)];

        public IReadOnlyList<GamePromptAsset> GetPromptAssets() => [];
    }

    private sealed class RecordingRulesEngineObserver : IRulesEngineObserver
    {
        public List<EngineTraceRecord> Records { get; } = [];

        public void Record(EngineTraceRecord record)
        {
            Records.Add(record);
        }
    }
}
