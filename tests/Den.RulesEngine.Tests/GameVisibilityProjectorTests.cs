namespace Den.RulesEngine.Tests;

public sealed class GameVisibilityProjectorTests
{
    [Fact]
    public void ProjectPublic_ReturnsOnlyPublicEvents()
    {
        var state = CreateStateWithVisibilityEvents();
        var projection = new GameVisibilityProjector().ProjectPublic(state.EventJournal);

        Assert.Equal(["GameStartedEvent"], projection.Events.Select(gameEvent => gameEvent.EventType).ToArray());
    }

    [Fact]
    public void ProjectPlayer_ReturnsPublicOwnPrivateAndParticipantSetEvents()
    {
        var state = CreateStateWithVisibilityEvents();
        var input = GameVisibilityProjectionInput.FromState(state);
        var aliceProjection = new GameVisibilityProjector().ProjectPlayer(input, new ParticipantId("alice"));
        var bobProjection = new GameVisibilityProjector().ProjectPlayer(input, new ParticipantId("bob"));

        Assert.Equal(
            ["GameStartedEvent", "PlayerChoiceSubmittedEvent", "NoActionTakenEvent"],
            aliceProjection.Events.Select(gameEvent => gameEvent.EventType).ToArray());
        Assert.DoesNotContain(aliceProjection.Events, gameEvent => gameEvent.EventType == "DeterministicEffectsAdvancedEvent");
        Assert.DoesNotContain(aliceProjection.Events, gameEvent => gameEvent.EventType == "GameEndedEvent");

        Assert.Equal(
            ["GameStartedEvent", "GameEndedEvent"],
            bobProjection.Events.Select(gameEvent => gameEvent.EventType).ToArray());
        Assert.DoesNotContain(bobProjection.Events, gameEvent => gameEvent.EventType == "PlayerChoiceSubmittedEvent");
        Assert.DoesNotContain(bobProjection.Events, gameEvent => gameEvent.EventType == "NoActionTakenEvent");
        Assert.DoesNotContain(bobProjection.Events, gameEvent => gameEvent.EventType == "DeterministicEffectsAdvancedEvent");
    }

    [Fact]
    public void ProjectionInput_DoesNotExposeFullRulesStateAuthority()
    {
        var properties = typeof(GameVisibilityProjectionInput)
            .GetProperties()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("ModuleId", properties);
        Assert.DoesNotContain("ModuleVersion", properties);
        Assert.DoesNotContain("Random", properties);
    }

    [Fact]
    public void ProjectPlayer_ReturnsOnlyPendingInputForThatPlayer()
    {
        var state = CreateStateWithVisibilityEvents();
        var projection = new GameVisibilityProjector().ProjectPlayer(GameVisibilityProjectionInput.FromState(state), new ParticipantId("alice"));

        var pendingInput = Assert.Single(projection.PendingInputs);
        Assert.Equal(new PendingInputId("input-alice"), pendingInput.PendingInputId);
    }

    private static RulesGameState CreateStateWithVisibilityEvents()
    {
        var gameId = new GameInstanceId("game-1");
        var wolves = new ParticipantSetId("wolves");
        var alice = new ParticipantState(new ParticipantId("alice"), "Alice", ParticipantKind.Agent, [wolves]);
        var bob = ParticipantState.Human(new ParticipantId("bob"), "Bob");
        var descriptor = TestGameModule.CreateDescriptor();
        var state = RulesGameState.CreateNotStarted(gameId, descriptor, 123, [alice, bob]);

        var journal = state.EventJournal
            .Append(GameStartedEvent.Create(gameId, descriptor.ModuleId, descriptor.ModuleVersion, 123))
            .Append(PlayerChoiceSubmittedEvent.Create(
                gameId,
                new PendingInputId("input-alice"),
                alice.ParticipantId,
                "howl",
                GameEventVisibility.PrivateToParticipant(alice.ParticipantId)))
            .Append(NoActionTakenEvent.Create(gameId, new PendingInputId("input-bob"), bob.ParticipantId, "default") with
            {
                Visibility = GameEventVisibility.PrivateToSet(wolves)
            })
            .Append(GameEndedEvent.Create(gameId, "hidden-other-player") with
            {
                Visibility = GameEventVisibility.PrivateToParticipant(bob.ParticipantId)
            })
            .Append(DeterministicEffectsAdvancedEvent.Create(gameId, "hidden-system"));

        var pendingInputs = new[]
        {
            new PendingInputState(
                new PendingInputId("input-alice"),
                alice.ParticipantId,
                state.Stage.StageId,
                "howl",
                PendingInputStatus.Waiting,
                [new LegalIntentOption("howl", "Howl", "Howl at the moon.")]),
            new PendingInputState(
                new PendingInputId("input-bob"),
                bob.ParticipantId,
                state.Stage.StageId,
                "vote",
                PendingInputStatus.Waiting,
                [new LegalIntentOption("vote", "Vote", "Vote publicly.")])
        };

        return state with
        {
            Status = RulesGameStatus.WaitingForInput,
            EventJournal = journal,
            PendingInputs = pendingInputs
        };
    }
}
