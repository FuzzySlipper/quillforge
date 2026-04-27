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
        var first = Fnv1a64(input);
        var second = Fnv1a64($"{input}:event");
        Span<byte> guidBytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(guidBytes[..8], first);
        BitConverter.TryWriteBytes(guidBytes[8..], second);

        return new GameEventId(new Guid(guidBytes));
    }

    private static ulong Fnv1a64(string value)
    {
        const ulong offsetBasis = 14695981039346656037ul;
        const ulong prime = 1099511628211ul;

        var hash = offsetBasis;
        unchecked
        {
            foreach (var character in value)
            {
                hash ^= character;
                hash *= prime;
            }
        }

        return hash;
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
