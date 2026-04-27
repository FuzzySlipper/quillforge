namespace Den.RulesEngine;

public sealed record GameEventJournal(
    GameInstanceId GameInstanceId,
    long NextSequence,
    IReadOnlyList<IGameEvent> Events)
{
    public static GameEventJournal Empty(GameInstanceId gameInstanceId) => new(gameInstanceId, 1, []);

    public GameEventJournal Append(IGameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        if (gameEvent.GameInstanceId != GameInstanceId)
        {
            throw new ArgumentException("Event belongs to a different game instance.", nameof(gameEvent));
        }

        var committed = gameEvent.WithJournalMetadata(
            CreateEventId(GameInstanceId, NextSequence),
            NextSequence,
            ResolveOccurredAt(gameEvent, NextSequence));

        return this with
        {
            NextSequence = NextSequence + 1,
            Events = Events.Concat([committed]).ToArray()
        };
    }

    public GameEventJournal AppendRange(IEnumerable<IGameEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        var journal = this;
        foreach (var gameEvent in events)
        {
            journal = journal.Append(gameEvent);
        }

        return journal;
    }

    public GameEventJournalSnapshot ToSnapshot() => new(GameInstanceId, NextSequence, Events.ToArray());

    private static GameEventId CreateEventId(GameInstanceId gameInstanceId, long sequence)
    {
        var input = $"{gameInstanceId.Value}:{sequence}";
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(input));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);

        return new GameEventId(new Guid(guidBytes));
    }

    private static DateTimeOffset ResolveOccurredAt(IGameEvent gameEvent, long sequence)
    {
        return gameEvent.OccurredAt == default
            ? DateTimeOffset.UnixEpoch.AddTicks(sequence)
            : gameEvent.OccurredAt;
    }
}

public sealed record GameEventJournalSnapshot(
    GameInstanceId GameInstanceId,
    long NextSequence,
    IReadOnlyList<IGameEvent> Events)
{
    public GameEventJournal ToJournal() => new(GameInstanceId, NextSequence, Events.ToArray());
}
