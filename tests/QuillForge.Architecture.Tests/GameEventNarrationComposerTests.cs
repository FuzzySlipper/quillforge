using Den.RulesEngine;
using Den.RulesEngine.Werewolf;
using QuillForge.Core.Services;
using QuillForge.Web.Services;

namespace QuillForge.Architecture.Tests;

public sealed class GameEventNarrationComposerTests
{
    [Fact]
    public void Composite_DispatchesWerewolfEventsToWerewolfComposer()
    {
        var gameId = new GameInstanceId("game-werewolf");
        var participantId = new ParticipantId("player-1");
        var composite = new CompositeGameEventNarrationComposer(
            [new WerewolfGameEventNarrationComposer()],
            new DefaultGameEventNarrationComposer());

        var text = composite.ComposeSummary(WerewolfRoleRevealedEvent.Create(gameId, participantId, WerewolfRole.Seer));

        Assert.Equal("Your role is Seer.", text);
    }

    [Fact]
    public void Composite_FallsBackToDefaultForCoreEvents()
    {
        var gameId = new GameInstanceId("game-werewolf");
        var gameEvent = GameStartedEvent.Create(
            gameId,
            WerewolfModuleAssemblyMarker.ModuleId,
            WerewolfModuleAssemblyMarker.ModuleVersion,
            seed: 42);
        var fallback = new DefaultGameEventNarrationComposer();
        var composite = new CompositeGameEventNarrationComposer(
            [new WerewolfGameEventNarrationComposer()],
            fallback);

        var text = composite.ComposeSummary(gameEvent);

        Assert.Equal(fallback.ComposeSummary(gameEvent), text);
        Assert.Equal("Game started with module werewolf.", text);
    }

    [Fact]
    public void Composite_DispatchesSyntheticSecondModuleComposer()
    {
        var gameEvent = SyntheticSecondModuleEvent.Create(new GameInstanceId("game-second"));
        var composite = new CompositeGameEventNarrationComposer(
            [new WerewolfGameEventNarrationComposer(), new SyntheticSecondModuleNarrationComposer()],
            new DefaultGameEventNarrationComposer());

        var text = composite.ComposeSummary(gameEvent);

        Assert.Equal("Second module narrated synthetic beat.", text);
    }

    private sealed class SyntheticSecondModuleNarrationComposer : IGameModuleEventNarrationComposer
    {
        public bool CanCompose(IGameEvent gameEvent) => gameEvent is SyntheticSecondModuleEvent;

        public string ComposeSummary(IGameEvent gameEvent)
        {
            Assert.IsType<SyntheticSecondModuleEvent>(gameEvent);
            return "Second module narrated synthetic beat.";
        }
    }

    private sealed record SyntheticSecondModuleEvent(
        GameEventId EventId,
        long Sequence,
        GameInstanceId GameInstanceId,
        DateTimeOffset OccurredAt,
        GameEventVisibility Visibility) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
    {
        public static SyntheticSecondModuleEvent Create(GameInstanceId gameInstanceId) =>
            new(default, 0, gameInstanceId, default, GameEventVisibility.Public);

        public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
            this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
    }
}
