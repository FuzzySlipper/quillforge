using Den.RulesEngine;

namespace Den.RulesEngine.Werewolf.Tests;

public sealed class WerewolfScenarioTests
{
    [Fact]
    public void ScriptedScenario_VillageWin_ReplaysWithDeterministicJournal()
    {
        var first = WerewolfScenario.Start(seed: 42);
        first.ResolveNight();
        var werewolf = first.ActiveWerewolf();
        first.ResolveVote(_ => werewolf.ParticipantId.Value);

        var replay = WerewolfScenario.Start(seed: 42);
        replay.ResolveNight();
        var replayWerewolf = replay.ActiveWerewolf();
        replay.ResolveVote(_ => replayWerewolf.ParticipantId.Value);

        Assert.Equal(RulesGameStatus.Ended, first.State.Status);
        Assert.Contains(first.Events.OfType<WerewolfWinConditionResolvedEvent>(), gameEvent => gameEvent.Winner == WerewolfWinner.Villagers);
        Assert.Contains(first.Events.OfType<GameEndedEvent>(), gameEvent => gameEvent.OutcomeName == "villagers_win");
        Assert.Equal(EventSignatures(first.Events), EventSignatures(replay.Events));
        Assert.Equal(RoleMap(first.State), RoleMap(replay.State));
    }

    [Fact]
    public void ScriptedScenario_WerewolfParityWin_EndsGameWithWerewolfOutcome()
    {
        var scenario = WerewolfScenario.Start(seed: 7, werewolfCount: 2);
        scenario.ResolveNight();
        var villager = scenario.ActiveVillager();

        scenario.ResolveVote(_ => villager.ParticipantId.Value);

        Assert.Equal(RulesGameStatus.Ended, scenario.State.Status);
        Assert.Contains(scenario.Events.OfType<WerewolfWinConditionResolvedEvent>(), gameEvent => gameEvent.Winner == WerewolfWinner.Werewolves);
        Assert.Contains(scenario.Events.OfType<GameEndedEvent>(), gameEvent => gameEvent.OutcomeName == "werewolves_win");
    }

    [Fact]
    public void ScriptedScenario_TiedVoteKeepsGameRunningAtNextNight()
    {
        var scenario = WerewolfScenario.Start(seed: 42);
        scenario.ResolveNight();
        var active = scenario.State.Participants.Where(participant => participant.IsActive).ToArray();

        scenario.ResolveVote(input =>
            input.ParticipantId == active[0].ParticipantId || input.ParticipantId == active[1].ParticipantId
                ? active[0].ParticipantId.Value
                : active[1].ParticipantId.Value);

        Assert.Equal(RulesGameStatus.Running, scenario.State.Status);
        Assert.Equal(WerewolfConstants.NightStage.StageId, scenario.State.Stage.StageId);
        Assert.Equal(2, scenario.State.Round.RoundNumber);
        Assert.Contains(scenario.Events.OfType<WerewolfVoteResolvedEvent>(), gameEvent => gameEvent.EliminatedParticipantId is null && gameEvent.IsTie);
    }

    [Fact]
    public void ScriptedScenario_AllAbstainKeepsGameRunningAtNextNight()
    {
        var scenario = WerewolfScenario.Start(seed: 42);
        scenario.ResolveNight();

        scenario.ResolveVote(_ => WerewolfConstants.AbstainChoice);

        Assert.Equal(RulesGameStatus.Running, scenario.State.Status);
        Assert.Equal(WerewolfConstants.NightStage.StageId, scenario.State.Stage.StageId);
        Assert.Equal(2, scenario.State.Round.RoundNumber);
        Assert.Contains(scenario.Events.OfType<WerewolfVoteResolvedEvent>(), gameEvent => gameEvent.EliminatedParticipantId is null && !gameEvent.IsTie);
    }

    [Fact]
    public void MissingVote_DoesNotResolveVotingOrEndGame()
    {
        var scenario = WerewolfScenario.Start(seed: 42);
        scenario.ResolveNight();
        scenario.RequestVoting();
        var werewolf = scenario.ActiveWerewolf();
        var firstPendingInputs = scenario.State.PendingInputs.Take(scenario.State.PendingInputs.Count - 1).ToArray();

        foreach (var pendingInput in firstPendingInputs)
        {
            scenario.Submit(pendingInput, werewolf.ParticipantId.Value);
        }

        Assert.Equal(RulesGameStatus.WaitingForInput, scenario.State.Status);
        Assert.Equal(WerewolfConstants.VotingStage.StageId, scenario.State.Stage.StageId);
        Assert.DoesNotContain(scenario.Events.OfType<WerewolfVoteResolvedEvent>(), gameEvent => gameEvent.Sequence > 0);
        Assert.DoesNotContain(scenario.Events.OfType<GameEndedEvent>(), gameEvent => gameEvent.Sequence > 0);
        Assert.Contains(scenario.State.PendingInputs, input => input.Status == PendingInputStatus.Waiting);
    }

    [Fact]
    public void InvalidVoteTarget_IsRejectedWithoutResolvingVoting()
    {
        var scenario = WerewolfScenario.Start(seed: 42);
        scenario.ResolveNight();
        scenario.RequestVoting(extraLegalOption: new LegalIntentOption("ghost", "Ghost", "Invalid target accepted by harness to exercise module validation."));
        var pendingInput = scenario.State.PendingInputs[0];

        var result = scenario.Submit(pendingInput, "ghost");

        Assert.False(result.IsAccepted);
        Assert.Equal("invalid_vote_target", Assert.Single(result.Issues).Code);
        Assert.Equal(RulesGameStatus.WaitingForInput, scenario.State.Status);
        Assert.Contains(result.Events.OfType<IntentCommandRejectedEvent>(), gameEvent => gameEvent.ReasonCode == "invalid_vote_target");
    }

    [Fact]
    public void PrivateRoleAndNightActionVisibility_DoNotLeakToOtherParticipants()
    {
        var scenario = WerewolfScenario.Start(seed: 42);
        scenario.RequestNightActions();
        var pendingInput = scenario.State.PendingInputs[0];
        var participant = pendingInput.ParticipantId;
        var other = scenario.State.Participants.First(item => item.ParticipantId != participant).ParticipantId;

        scenario.Submit(pendingInput, WerewolfConstants.SkipNightChoice);

        var projector = new GameVisibilityProjector();
        var participantProjection = projector.ProjectPlayer(scenario.State, participant);
        var otherProjection = projector.ProjectPlayer(scenario.State, other);

        Assert.Contains(participantProjection.Events, gameEvent => gameEvent.EventType == nameof(WerewolfRoleRevealedEvent));
        Assert.Contains(participantProjection.Events, gameEvent => gameEvent.EventType == nameof(PlayerChoiceSubmittedEvent));
        Assert.DoesNotContain(otherProjection.Events, gameEvent => gameEvent.EventType == nameof(PlayerChoiceSubmittedEvent));
        Assert.DoesNotContain(participantProjection.Events, gameEvent => gameEvent.EventType == nameof(WerewolfRoleAssignedEvent));
        Assert.DoesNotContain(otherProjection.Events, gameEvent => gameEvent.EventType == nameof(WerewolfRoleAssignedEvent));
    }

    [Fact]
    public void OneNightCompatibleSetup_IsAcceptedWhilePublishedVariantMechanicsStayDocumentedFollowUp()
    {
        var scenario = WerewolfScenario.Start(seed: 12, oneNightCompatible: true);
        var module = new WerewolfModule();
        var note = module.GetPromptAssets().Single(asset => asset.AssetId == "werewolf-one-night-follow-up");

        Assert.True(scenario.StartResult.IsAccepted);
        Assert.Contains("One Night", note.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("follow-up", note.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("center-card", note.Content, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] EventSignatures(IReadOnlyList<IGameEvent> events) =>
        events.Select(EventSignature).ToArray();

    private static string EventSignature(IGameEvent gameEvent) =>
        gameEvent switch
        {
            GameStartedEvent started => $"{started.Sequence}:{nameof(GameStartedEvent)}:{started.Seed}",
            WerewolfRoleAssignedEvent assigned => $"{assigned.Sequence}:{nameof(WerewolfRoleAssignedEvent)}:{assigned.ParticipantId}:{assigned.Role}:{assigned.Visibility.Kind}",
            WerewolfRoleRevealedEvent revealed => $"{revealed.Sequence}:{nameof(WerewolfRoleRevealedEvent)}:{revealed.ParticipantId}:{revealed.Role}:{revealed.Visibility.Kind}",
            WerewolfTeamRevealedEvent team => $"{team.Sequence}:{nameof(WerewolfTeamRevealedEvent)}:{string.Join(',', team.WerewolfParticipantIds.Select(id => id.Value).Order(StringComparer.Ordinal))}:{team.Visibility.Kind}",
            WerewolfStageStartedEvent stage => $"{stage.Sequence}:{nameof(WerewolfStageStartedEvent)}:{stage.StageId}:{stage.RoundNumber}",
            PendingInputRequestedEvent input => $"{input.Sequence}:{nameof(PendingInputRequestedEvent)}:{input.ParticipantId}:{input.StageId}:{input.IntentName}",
            PlayerChoiceSubmittedEvent choice => $"{choice.Sequence}:{nameof(PlayerChoiceSubmittedEvent)}:{choice.ParticipantId}:{choice.ChoiceName}:{choice.Visibility.Kind}",
            WerewolfNightActionsResolvedEvent night => $"{night.Sequence}:{nameof(WerewolfNightActionsResolvedEvent)}:{night.RoundNumber}",
            StageAdvancedEvent advanced => $"{advanced.Sequence}:{nameof(StageAdvancedEvent)}:{advanced.PreviousStageId}:{advanced.NextStageId}",
            WerewolfVoteRecordedEvent vote => $"{vote.Sequence}:{nameof(WerewolfVoteRecordedEvent)}:{vote.VoterParticipantId}:{vote.TargetParticipantId?.Value ?? "abstain"}",
            WerewolfVoteResolvedEvent resolved => $"{resolved.Sequence}:{nameof(WerewolfVoteResolvedEvent)}:{resolved.EliminatedParticipantId?.Value ?? "none"}:{resolved.IsTie}",
            WerewolfPlayerEliminatedEvent eliminated => $"{eliminated.Sequence}:{nameof(WerewolfPlayerEliminatedEvent)}:{eliminated.ParticipantId}:{eliminated.Role}",
            WerewolfWinConditionResolvedEvent win => $"{win.Sequence}:{nameof(WerewolfWinConditionResolvedEvent)}:{win.Winner}:{win.ReasonCode}",
            GameEndedEvent ended => $"{ended.Sequence}:{nameof(GameEndedEvent)}:{ended.OutcomeName}",
            IntentCommandRejectedEvent rejected => $"{rejected.Sequence}:{nameof(IntentCommandRejectedEvent)}:{rejected.ReasonCode}",
            _ => $"{gameEvent.Sequence}:{gameEvent.GetType().Name}:{gameEvent.Visibility.Kind}"
        };

    private static Dictionary<string, string> RoleMap(RulesGameState state) =>
        state.Participants.ToDictionary(
            participant => participant.ParticipantId.Value,
            participant => participant.ParticipantSetIds.Single(setId => setId.Value.StartsWith("role:", StringComparison.Ordinal)).Value,
            StringComparer.Ordinal);

    private sealed class WerewolfScenario
    {
        private int _nextCommandId;

        private WerewolfScenario(RulesEngineService service, RulesEngineApplyResult startResult)
        {
            Service = service;
            StartResult = startResult;
            State = startResult.State;
            Events.AddRange(startResult.Events);
        }

        public RulesEngineService Service { get; }

        public RulesEngineApplyResult StartResult { get; }

        public RulesGameState State { get; private set; }

        public List<IGameEvent> Events { get; } = [];

        public static WerewolfScenario Start(
            long seed,
            int werewolfCount = 1,
            bool seerEnabled = false,
            bool oneNightCompatible = false)
        {
            var module = new WerewolfModule();
            var registry = new GameModuleRegistry();
            var registration = registry.Register(module);
            Assert.True(registration.IsValid);
            var service = new RulesEngineService(registry);
            var scenario = new WerewolfScenario(
                service,
                new RulesEngineApplyResult(
                    RulesGameState.CreateNotStarted(GameId, module.Descriptor, seed, []),
                    [],
                    [],
                    []));
            var start = service.Apply(scenario.State, new StartGameIntentCommand(
                scenario.NextCommandId(),
                GameId,
                module.Descriptor.ModuleId,
                module.Descriptor.ModuleVersion,
                seed,
                Setup(werewolfCount, seerEnabled, oneNightCompatible),
                Participants));

            return new WerewolfScenario(service, start)
            {
                _nextCommandId = scenario._nextCommandId
            };
        }

        public ParticipantState ActiveWerewolf() =>
            State.Participants.Single(participant => participant.IsActive && participant.ParticipantSetIds.Contains(WerewolfConstants.WerewolfRoleSetId));

        public ParticipantState ActiveVillager() =>
            State.Participants.First(participant => participant.IsActive && !participant.ParticipantSetIds.Contains(WerewolfConstants.WerewolfRoleSetId));

        public void ResolveNight()
        {
            RequestNightActions();
            foreach (var pendingInput in State.PendingInputs.ToArray())
            {
                Submit(pendingInput, WerewolfConstants.SkipNightChoice);
            }
        }

        public void RequestNightActions()
        {
            Apply(new RequestPendingInputIntentCommand(
                NextCommandId(),
                GameId,
                WerewolfConstants.NightStage.StageId,
                "night-action",
                [new LegalIntentOption(WerewolfConstants.SkipNightChoice, "Skip", "No baseline night action.")],
                PendingInputAudience.AllActiveParticipants));
        }

        public void RequestVoting(LegalIntentOption? extraLegalOption = null)
        {
            Apply(new AdvanceStageIntentCommand(NextCommandId(), GameId, WerewolfConstants.VotingStage));
            var options = VoteOptions(State).ToList();
            if (extraLegalOption is not null)
            {
                options.Add(extraLegalOption);
            }

            Apply(new RequestPendingInputIntentCommand(
                NextCommandId(),
                GameId,
                WerewolfConstants.VotingStage.StageId,
                "vote",
                options,
                PendingInputAudience.AllActiveParticipants));
        }

        public void ResolveVote(Func<PendingInputState, string> choiceForInput)
        {
            RequestVoting();
            foreach (var pendingInput in State.PendingInputs.ToArray())
            {
                Submit(pendingInput, choiceForInput(pendingInput));
            }
        }

        public RulesEngineApplyResult Submit(PendingInputState pendingInput, string choiceName) =>
            Apply(new SubmitPlayerChoiceIntentCommand(
                NextCommandId(),
                GameId,
                pendingInput.PendingInputId,
                pendingInput.ParticipantId,
                choiceName));

        private RulesEngineApplyResult Apply(IGameIntentCommand command)
        {
            var result = Service.Apply(State, command);
            State = result.State;
            Events.AddRange(result.Events);
            return result;
        }

        private GameIntentCommandId NextCommandId()
        {
            _nextCommandId++;
            return new GameIntentCommandId(Guid.Parse($"00000000-0000-0000-0000-{_nextCommandId:000000000000}"));
        }
    }

    private static GameSetup Setup(int werewolfCount, bool seerEnabled, bool oneNightCompatible) =>
        new([
            new IntGameSetupValue(WerewolfConstants.WerewolfCountSetupField, werewolfCount),
            new BoolGameSetupValue(WerewolfConstants.SeerEnabledSetupField, seerEnabled),
            new BoolGameSetupValue(WerewolfConstants.OneNightCompatibleSetupField, oneNightCompatible)
        ]);

    private static IReadOnlyList<LegalIntentOption> VoteOptions(RulesGameState state) =>
        state.Participants
            .Where(participant => participant.IsActive)
            .Select(participant => new LegalIntentOption(participant.ParticipantId.Value, participant.DisplayName, $"Vote {participant.DisplayName}"))
            .Append(new LegalIntentOption(WerewolfConstants.AbstainChoice, "Abstain", "Do not eliminate anyone."))
            .ToArray();

    private static readonly GameInstanceId GameId = new("werewolf-scenario");
    private static readonly ParticipantSetup[] Participants =
    [
        new(new ParticipantId("alice"), "Alice", ParticipantKind.Human),
        new(new ParticipantId("bob"), "Bob", ParticipantKind.Agent),
        new(new ParticipantId("carol"), "Carol", ParticipantKind.Agent),
        new(new ParticipantId("drew"), "Drew", ParticipantKind.Agent)
    ];
}
