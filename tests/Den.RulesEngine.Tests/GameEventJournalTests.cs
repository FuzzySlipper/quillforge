namespace Den.RulesEngine.Tests;

public sealed class GameEventJournalTests
{
    [Fact]
    public void Append_AssignsStableIdsAndMonotonicSequences()
    {
        var gameId = new GameInstanceId("game-1");
        var journal = GameEventJournal.Empty(gameId);

        journal = journal.Append(GameStartedEvent.Create(gameId, new GameModuleId("test"), new GameModuleVersion("1.0.0"), seed: 42));
        journal = journal.Append(GameEndedEvent.Create(gameId, "villagers-win"));

        Assert.Equal(3, journal.NextSequence);
        Assert.Equal([1, 2], journal.Events.Select(gameEvent => gameEvent.Sequence).ToArray());
        Assert.All(journal.Events, gameEvent => Assert.NotEqual(Guid.Empty, gameEvent.EventId.Value));
        Assert.All(journal.Events, gameEvent => Assert.Equal(gameId, gameEvent.GameInstanceId));
    }

    [Fact]
    public void Append_ReplaysSameEventIdsForSameGameAndSequence()
    {
        var gameId = new GameInstanceId("game-1");
        var first = GameEventJournal.Empty(gameId)
            .Append(GameStartedEvent.Create(gameId, new GameModuleId("test"), new GameModuleVersion("1.0.0"), seed: 42));
        var replay = GameEventJournal.Empty(gameId)
            .Append(GameStartedEvent.Create(gameId, new GameModuleId("test"), new GameModuleVersion("1.0.0"), seed: 42));

        Assert.Equal(first.Events[0].EventId, replay.Events[0].EventId);
        Assert.Equal(first.Events[0].OccurredAt, replay.Events[0].OccurredAt);
    }

    [Fact]
    public void Append_RejectsEventsForDifferentGameInstances()
    {
        var journal = GameEventJournal.Empty(new GameInstanceId("game-1"));
        var foreignEvent = GameEndedEvent.Create(new GameInstanceId("game-2"), "other");

        Assert.Throws<ArgumentException>(() => journal.Append(foreignEvent));
    }
}
