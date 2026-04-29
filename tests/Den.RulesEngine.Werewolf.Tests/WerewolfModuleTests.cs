using Den.RulesEngine;

namespace Den.RulesEngine.Werewolf.Tests;

public sealed class WerewolfModuleTests
{
    [Fact]
    public void Descriptor_ExposesSetupCommunicationAndPromptAssets()
    {
        var module = new WerewolfModule();

        Assert.Equal(WerewolfModuleAssemblyMarker.ModuleId, module.Descriptor.ModuleId);
        Assert.Equal(new PlayerCountRange(3, 12), module.Descriptor.PlayerCount);
        Assert.True(module.Descriptor.CommunicationCapabilities.AllowsPublicChannelMessages);
        Assert.True(module.Descriptor.CommunicationCapabilities.AllowsDirectMessages);
        Assert.Contains(module.Descriptor.SetupFields, field => field.Name == WerewolfConstants.WerewolfCountSetupField && field.IsRequired);
        Assert.Contains(module.RoleDefinitions, role => role.Role == WerewolfRole.Werewolf && role.TeamSetId == WerewolfConstants.WerewolfTeamSetId);
        Assert.Contains(module.RoleDefinitions, role => role.Role == WerewolfRole.Villager && role.TeamSetId == WerewolfConstants.VillageTeamSetId);
        Assert.Contains(module.Descriptor.RequiredPromptAssets, asset => asset.AssetId == "werewolf-rules");
        Assert.Contains(module.GetPromptAssets(), asset => asset.AssetId == "werewolf-one-night-follow-up" && asset.Content.Contains("follow-up", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void StartGame_AssignsRolesDeterministicallyAndProtectsPrivateVisibility()
    {
        var first = StartGame(seed: 42, seerEnabled: true);
        var replay = StartGame(seed: 42, seerEnabled: true);

        Assert.True(first.IsAccepted);
        Assert.True(replay.IsAccepted);
        Assert.Equal(WerewolfConstants.NightStage.StageId, first.State.Stage.StageId);
        Assert.Equal(1, first.State.Round.RoundNumber);
        Assert.Equal(
            RoleMap(first.State),
            RoleMap(replay.State));
        Assert.Equal(4, first.State.EventJournal.Events.OfType<WerewolfRoleAssignedEvent>().Count());
        Assert.Equal(4, first.State.EventJournal.Events.OfType<WerewolfRoleRevealedEvent>().Count());

        var werewolf = first.State.Participants.Single(participant => participant.ParticipantSetIds.Contains(WerewolfConstants.WerewolfRoleSetId));
        var villager = first.State.Participants.First(participant => !participant.ParticipantSetIds.Contains(WerewolfConstants.WerewolfRoleSetId));
        var projector = new GameVisibilityProjector();
        var werewolfProjection = projector.ProjectPlayer(GameVisibilityProjectionInput.FromState(first.State), werewolf.ParticipantId);
        var villagerProjection = projector.ProjectPlayer(GameVisibilityProjectionInput.FromState(first.State), villager.ParticipantId);

        Assert.Contains(werewolfProjection.Events, gameEvent => gameEvent.EventType == nameof(WerewolfRoleRevealedEvent));
        Assert.Contains(werewolfProjection.Events, gameEvent => gameEvent.EventType == nameof(WerewolfTeamRevealedEvent));
        Assert.DoesNotContain(werewolfProjection.Events, gameEvent => gameEvent.EventType == nameof(WerewolfRoleAssignedEvent));
        Assert.DoesNotContain(villagerProjection.Events, gameEvent => gameEvent.EventType == nameof(WerewolfTeamRevealedEvent));
        Assert.DoesNotContain(villagerProjection.Events, gameEvent => gameEvent.EventType == nameof(WerewolfRoleAssignedEvent));
    }

    [Fact]
    public void NightSubmissions_ResolveToDayDiscussionBoundaryWhenAllPlayersSubmit()
    {
        var started = StartGame(seed: 99);
        var service = CreateService();
        var requested = service.Apply(started.State, new RequestPendingInputIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            WerewolfConstants.NightStage.StageId,
            "night-action",
            [new LegalIntentOption(WerewolfConstants.SkipNightChoice, "Skip", "No night action.")],
            PendingInputAudience.AllActiveParticipants));

        var state = requested.State;
        RulesEngineApplyResult? last = null;
        foreach (var pendingInput in requested.State.PendingInputs.ToArray())
        {
            last = service.Apply(state, new SubmitPlayerChoiceIntentCommand(
                GameIntentCommandId.NewId(),
                GameId,
                pendingInput.PendingInputId,
                pendingInput.ParticipantId,
                WerewolfConstants.SkipNightChoice));
            state = last.State;
        }

        Assert.NotNull(last);
        Assert.True(last.IsAccepted);
        Assert.Equal(WerewolfConstants.DayDiscussionStage.StageId, last.State.Stage.StageId);
        Assert.Empty(last.State.PendingInputs);
        Assert.Contains(last.Events, gameEvent => gameEvent is WerewolfNightActionsResolvedEvent);
        Assert.Contains(last.Events.OfType<WerewolfStageStartedEvent>(), gameEvent => gameEvent.StageId == WerewolfConstants.DayDiscussionStage.StageId);
    }

    [Fact]
    public void Voting_EliminatesWerewolfAndEndsWithVillagerWin()
    {
        var started = StartGame(seed: 42);
        var service = CreateService();
        var werewolf = started.State.Participants.Single(participant => participant.ParticipantSetIds.Contains(WerewolfConstants.WerewolfRoleSetId));
        var requested = RequestVotes(service, started.State, out var voting);

        Assert.Contains(voting.Events.OfType<WerewolfStageStartedEvent>(), gameEvent => gameEvent.StageId == WerewolfConstants.VotingStage.StageId);
        var last = SubmitVotes(service, requested.State, _ => werewolf.ParticipantId.Value);

        Assert.NotNull(last);
        Assert.True(last.IsAccepted);
        Assert.Equal(RulesGameStatus.Ended, last.State.Status);
        Assert.False(last.State.FindParticipant(werewolf.ParticipantId)?.IsActive);
        Assert.Contains(last.Events.OfType<WerewolfVoteResolvedEvent>(), gameEvent => gameEvent.EliminatedParticipantId == werewolf.ParticipantId && !gameEvent.IsTie);
        Assert.Contains(last.Events.OfType<WerewolfWinConditionResolvedEvent>(), gameEvent => gameEvent.Winner == WerewolfWinner.Villagers);
        Assert.Contains(last.Events.OfType<GameEndedEvent>(), gameEvent => gameEvent.OutcomeName == "villagers_win");
    }

    [Fact]
    public void Voting_CanResolveWerewolfParityWin()
    {
        var started = StartGame(seed: 7, werewolfCount: 2);
        var service = CreateService();
        var villager = started.State.Participants.First(participant => !participant.ParticipantSetIds.Contains(WerewolfConstants.WerewolfRoleSetId));
        var requested = RequestVotes(service, started.State, out _);

        var last = SubmitVotes(service, requested.State, _ => villager.ParticipantId.Value);

        Assert.True(last.IsAccepted);
        Assert.Equal(RulesGameStatus.Ended, last.State.Status);
        Assert.Contains(last.Events.OfType<WerewolfWinConditionResolvedEvent>(), gameEvent => gameEvent.Winner == WerewolfWinner.Werewolves);
        Assert.Contains(last.Events.OfType<GameEndedEvent>(), gameEvent => gameEvent.OutcomeName == "werewolves_win");
    }

    [Fact]
    public void Voting_TieAdvancesToNextNightWithoutElimination()
    {
        var started = StartGame(seed: 42);
        var service = CreateService();
        var requested = RequestVotes(service, started.State, out _);
        var activeParticipants = requested.State.Participants.Where(participant => participant.IsActive).ToArray();
        var firstTarget = activeParticipants[0].ParticipantId.Value;
        var secondTarget = activeParticipants[1].ParticipantId.Value;

        var last = SubmitVotes(service, requested.State, pendingInput =>
            pendingInput.ParticipantId == activeParticipants[0].ParticipantId || pendingInput.ParticipantId == activeParticipants[1].ParticipantId
                ? firstTarget
                : secondTarget);

        Assert.True(last.IsAccepted);
        Assert.Equal(RulesGameStatus.Running, last.State.Status);
        Assert.Equal(WerewolfConstants.NightStage.StageId, last.State.Stage.StageId);
        Assert.Equal(2, last.State.Round.RoundNumber);
        Assert.All(last.State.Participants, participant => Assert.True(participant.IsActive));
        Assert.Contains(last.Events.OfType<WerewolfVoteResolvedEvent>(), gameEvent => gameEvent.EliminatedParticipantId is null && gameEvent.IsTie);
    }

    [Fact]
    public void Voting_AllAbstainAdvancesToNextNightWithoutElimination()
    {
        var started = StartGame(seed: 42);
        var service = CreateService();
        var requested = RequestVotes(service, started.State, out _);

        var last = SubmitVotes(service, requested.State, _ => WerewolfConstants.AbstainChoice);

        Assert.True(last.IsAccepted);
        Assert.Equal(RulesGameStatus.Running, last.State.Status);
        Assert.Equal(WerewolfConstants.NightStage.StageId, last.State.Stage.StageId);
        Assert.Equal(2, last.State.Round.RoundNumber);
        Assert.All(last.State.Participants, participant => Assert.True(participant.IsActive));
        Assert.Contains(last.Events.OfType<WerewolfVoteResolvedEvent>(), gameEvent => gameEvent.EliminatedParticipantId is null && !gameEvent.IsTie);
    }

    [Fact]
    public void InvalidNightAction_IsRejectedByModuleRule()
    {
        var started = StartGame(seed: 99);
        var service = CreateService();
        var requested = service.Apply(started.State, new RequestPendingInputIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            WerewolfConstants.NightStage.StageId,
            "night-action",
            [new LegalIntentOption("howl", "Howl", "Invalid baseline night action.")],
            PendingInputAudience.One(started.State.Participants[0].ParticipantId)));
        var pendingInput = Assert.Single(requested.State.PendingInputs);

        var result = service.Apply(requested.State, new SubmitPlayerChoiceIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            pendingInput.PendingInputId,
            pendingInput.ParticipantId,
            "howl"));

        Assert.False(result.IsAccepted);
        Assert.Equal("invalid_night_action", Assert.Single(result.Issues).Code);
        Assert.Contains(result.Events.OfType<IntentCommandRejectedEvent>(), gameEvent => gameEvent.ReasonCode == "invalid_night_action");
    }

    private static RulesEngineApplyResult StartGame(long seed, bool seerEnabled = false, int werewolfCount = 1)
    {
        var module = new WerewolfModule();
        var state = RulesGameState.CreateNotStarted(GameId, module.Descriptor, seed, []);
        return CreateService().Apply(state, new StartGameIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            module.Descriptor.ModuleId,
            module.Descriptor.ModuleVersion,
            seed,
            Setup(seerEnabled, werewolfCount),
            Participants));
    }

    private static RulesEngineService CreateService()
    {
        var registry = new GameModuleRegistry();
        var result = registry.Register(new WerewolfModule());
        Assert.True(result.IsValid);
        return new RulesEngineService(registry);
    }

    private static GameSetup Setup(bool seerEnabled, int werewolfCount) =>
        new([
            new IntGameSetupValue(WerewolfConstants.WerewolfCountSetupField, werewolfCount),
            new BoolGameSetupValue(WerewolfConstants.SeerEnabledSetupField, seerEnabled),
            new BoolGameSetupValue(WerewolfConstants.OneNightCompatibleSetupField, true)
        ]);

    private static RulesEngineApplyResult RequestVotes(
        RulesEngineService service,
        RulesGameState state,
        out RulesEngineApplyResult voting)
    {
        voting = service.Apply(state, new AdvanceStageIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            WerewolfConstants.VotingStage));

        return service.Apply(voting.State, new RequestPendingInputIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            WerewolfConstants.VotingStage.StageId,
            "vote",
            VoteOptions(voting.State),
            PendingInputAudience.AllActiveParticipants));
    }

    private static RulesEngineApplyResult SubmitVotes(
        RulesEngineService service,
        RulesGameState state,
        Func<PendingInputState, string> choiceForInput)
    {
        RulesEngineApplyResult? last = null;
        foreach (var pendingInput in state.PendingInputs.ToArray())
        {
            last = service.Apply(state, new SubmitPlayerChoiceIntentCommand(
                GameIntentCommandId.NewId(),
                GameId,
                pendingInput.PendingInputId,
                pendingInput.ParticipantId,
                choiceForInput(pendingInput)));
            state = last.State;
        }

        return last ?? throw new InvalidOperationException("Vote request produced no pending inputs.");
    }

    private static IReadOnlyList<LegalIntentOption> VoteOptions(RulesGameState state) =>
        state.Participants
            .Where(participant => participant.IsActive)
            .Select(participant => new LegalIntentOption(participant.ParticipantId.Value, participant.DisplayName, $"Vote {participant.DisplayName}"))
            .Append(new LegalIntentOption(WerewolfConstants.AbstainChoice, "Abstain", "Do not eliminate anyone."))
            .ToArray();

    private static Dictionary<string, string> RoleMap(RulesGameState state) =>
        state.Participants.ToDictionary(
            participant => participant.ParticipantId.Value,
            participant => participant.ParticipantSetIds.Single(setId => setId.Value.StartsWith("role:", StringComparison.Ordinal)).Value,
            StringComparer.Ordinal);

    private static readonly GameInstanceId GameId = new("werewolf-test");
    private static readonly ParticipantSetup[] Participants =
    [
        new(new ParticipantId("alice"), "Alice", ParticipantKind.Human),
        new(new ParticipantId("bob"), "Bob", ParticipantKind.Agent),
        new(new ParticipantId("carol"), "Carol", ParticipantKind.Agent),
        new(new ParticipantId("drew"), "Drew", ParticipantKind.Agent)
    ];
}
