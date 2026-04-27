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
        var werewolfProjection = projector.ProjectPlayer(first.State, werewolf.ParticipantId);
        var villagerProjection = projector.ProjectPlayer(first.State, villager.ParticipantId);

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
        var voting = service.Apply(started.State, new AdvanceStageIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            WerewolfConstants.VotingStage));
        var requested = service.Apply(voting.State, new RequestPendingInputIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            WerewolfConstants.VotingStage.StageId,
            "vote",
            VoteOptions(voting.State),
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
                werewolf.ParticipantId.Value));
            state = last.State;
        }

        Assert.NotNull(last);
        Assert.True(last.IsAccepted);
        Assert.Equal(RulesGameStatus.Ended, last.State.Status);
        Assert.False(last.State.FindParticipant(werewolf.ParticipantId)?.IsActive);
        Assert.Contains(last.Events.OfType<WerewolfVoteResolvedEvent>(), gameEvent => gameEvent.EliminatedParticipantId == werewolf.ParticipantId && !gameEvent.IsTie);
        Assert.Contains(last.Events.OfType<WerewolfWinConditionResolvedEvent>(), gameEvent => gameEvent.Winner == WerewolfWinner.Villagers);
        Assert.Contains(last.Events.OfType<GameEndedEvent>(), gameEvent => gameEvent.OutcomeName == "villagers_win");
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

    private static RulesEngineApplyResult StartGame(long seed, bool seerEnabled = false)
    {
        var module = new WerewolfModule();
        var state = RulesGameState.CreateNotStarted(GameId, module.Descriptor, seed, []);
        return CreateService().Apply(state, new StartGameIntentCommand(
            GameIntentCommandId.NewId(),
            GameId,
            module.Descriptor.ModuleId,
            module.Descriptor.ModuleVersion,
            seed,
            Setup(seerEnabled),
            Participants));
    }

    private static RulesEngineService CreateService()
    {
        var registry = new GameModuleRegistry();
        var result = registry.Register(new WerewolfModule());
        Assert.True(result.IsValid);
        return new RulesEngineService(registry);
    }

    private static GameSetup Setup(bool seerEnabled) =>
        new([
            new IntGameSetupValue(WerewolfConstants.WerewolfCountSetupField, 1),
            new BoolGameSetupValue(WerewolfConstants.SeerEnabledSetupField, seerEnabled),
            new BoolGameSetupValue(WerewolfConstants.OneNightCompatibleSetupField, true)
        ]);

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
