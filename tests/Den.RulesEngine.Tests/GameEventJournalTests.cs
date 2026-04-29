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
    public void ReplayComparer_ReportsMatchForEquivalentFixedSeedJournals()
    {
        var gameId = new GameInstanceId("game-replay");
        var first = GameEventJournal.Empty(gameId)
            .Append(GameStartedEvent.Create(gameId, new GameModuleId("test"), new GameModuleVersion("1.0.0"), seed: 42))
            .Append(PendingInputRequestedEvent.Create(
                gameId,
                new PendingInputId("pending-1"),
                new ParticipantId("agent-1"),
                new GameStageId("vote"),
                "vote"));
        var second = GameEventJournal.Empty(gameId)
            .Append(GameStartedEvent.Create(gameId, new GameModuleId("test"), new GameModuleVersion("1.0.0"), seed: 42))
            .Append(PendingInputRequestedEvent.Create(
                gameId,
                new PendingInputId("pending-1"),
                new ParticipantId("agent-1"),
                new GameStageId("vote"),
                "vote"));

        var diff = GameEventJournalReplayComparer.Compare(first, second);

        Assert.True(diff.IsReplayMatch);
        Assert.Empty(diff.Differences);
    }

    [Fact]
    public void ReplayComparer_ReportsUsefulDifferencesForDivergedJournals()
    {
        var gameId = new GameInstanceId("game-replay");
        var expected = GameEventJournal.Empty(gameId)
            .Append(GameStartedEvent.Create(gameId, new GameModuleId("test"), new GameModuleVersion("1.0.0"), seed: 42))
            .Append(GameEndedEvent.Create(gameId, "villagers_win"));
        var actual = GameEventJournal.Empty(gameId)
            .Append(GameStartedEvent.Create(gameId, new GameModuleId("test"), new GameModuleVersion("1.0.0"), seed: 42))
            .Append(GameEndedEvent.Create(gameId, "werewolves_win"));

        var diff = GameEventJournalReplayComparer.Compare(expected, actual);

        Assert.False(diff.IsReplayMatch);
        var difference = Assert.Single(diff.Differences);
        Assert.Equal(1, difference.EventIndex);
        Assert.Equal("villagers_win", difference.Expected!.OutcomeName);
        Assert.Equal("werewolves_win", difference.Actual!.OutcomeName);
    }

    [Fact]
    public void StoredGameEvent_FromKnownEventUsesDiscriminatorEventType()
    {
        var gameId = new GameInstanceId("game-1");
        var journaled = GameEventJournal.Empty(gameId)
            .Append(GameStartedEvent.Create(gameId, new GameModuleId("test"), new GameModuleVersion("1.0.0"), seed: 42))
            .Events[0];

        var stored = StoredGameEvent.FromEvent(journaled);

        Assert.Equal("game_started", stored.EventType);
    }

    [Fact]
    public void Append_RejectsEventsForDifferentGameInstances()
    {
        var journal = GameEventJournal.Empty(new GameInstanceId("game-1"));
        var foreignEvent = GameEndedEvent.Create(new GameInstanceId("game-2"), "other");

        Assert.Throws<ArgumentException>(() => journal.Append(foreignEvent));
    }
}
