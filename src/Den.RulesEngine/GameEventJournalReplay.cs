namespace Den.RulesEngine;

public static class GameEventJournalReplayComparer
{
    public static GameEventJournalReplayDiff Compare(GameEventJournal expected, GameEventJournal actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        var expectedSignatures = expected.Events.Select(GameEventReplaySignature.FromEvent).ToArray();
        var actualSignatures = actual.Events.Select(GameEventReplaySignature.FromEvent).ToArray();
        var differences = new List<GameEventJournalReplayDifference>();
        var count = Math.Max(expectedSignatures.Length, actualSignatures.Length);

        for (var i = 0; i < count; i++)
        {
            var expectedSignature = i < expectedSignatures.Length ? expectedSignatures[i] : null;
            var actualSignature = i < actualSignatures.Length ? actualSignatures[i] : null;
            if (expectedSignature == actualSignature)
            {
                continue;
            }

            differences.Add(new GameEventJournalReplayDifference(
                i,
                expectedSignature,
                actualSignature));
        }

        return new GameEventJournalReplayDiff(expected.GameInstanceId, actual.GameInstanceId, differences);
    }
}

public sealed record GameEventJournalReplayDiff(
    GameInstanceId ExpectedGameInstanceId,
    GameInstanceId ActualGameInstanceId,
    IReadOnlyList<GameEventJournalReplayDifference> Differences)
{
    public bool IsReplayMatch => Differences.Count == 0;
}

public sealed record GameEventJournalReplayDifference(
    int EventIndex,
    GameEventReplaySignature? Expected,
    GameEventReplaySignature? Actual);

public sealed record GameEventReplaySignature(
    long Sequence,
    string EventType,
    string Visibility,
    string? ParticipantId,
    string? PendingInputId,
    string? ChoiceName,
    string? ReasonCode,
    string? OutcomeName)
{
    public static GameEventReplaySignature FromEvent(IGameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        var facts = GameEventIntrospection.Inspect(gameEvent);
        return new GameEventReplaySignature(
            gameEvent.Sequence,
            gameEvent.GetType().Name,
            gameEvent.Visibility.Kind.ToString(),
            facts.ParticipantId,
            facts.PendingInputId,
            facts.ChoiceName,
            facts.ReasonCode,
            facts.OutcomeName);
    }
}
