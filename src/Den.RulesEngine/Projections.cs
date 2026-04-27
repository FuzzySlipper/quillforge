namespace Den.RulesEngine;

public sealed class GameVisibilityProjector
{
    public PublicGameProjection ProjectPublic(GameEventJournal journal)
    {
        ArgumentNullException.ThrowIfNull(journal);

        return new PublicGameProjection(
            journal.GameInstanceId,
            journal.Events
                .Where(gameEvent => gameEvent.Visibility.Kind == GameEventVisibilityKind.Public)
                .Select(VisibleGameEvent.FromEvent)
                .ToArray());
    }

    public PlayerGameProjection ProjectPlayer(RulesGameState state, ParticipantId participantId)
    {
        ArgumentNullException.ThrowIfNull(state);

        var participant = state.FindParticipant(participantId)
            ?? throw new ArgumentException("Participant is not registered in this game.", nameof(participantId));

        var visibleEvents = state.EventJournal.Events
            .Where(gameEvent => gameEvent.Visibility.IsVisibleTo(participant))
            .Select(VisibleGameEvent.FromEvent)
            .ToArray();

        var pendingInputs = state.PendingInputs
            .Where(input => input.IsWaitingFor(participantId))
            .ToArray();

        return new PlayerGameProjection(
            state.GameInstanceId,
            participant,
            state.Status,
            state.Round,
            state.Stage,
            visibleEvents,
            pendingInputs);
    }
}

public sealed record PublicGameProjection(
    GameInstanceId GameInstanceId,
    IReadOnlyList<VisibleGameEvent> Events);

public sealed record PlayerGameProjection(
    GameInstanceId GameInstanceId,
    ParticipantState Participant,
    RulesGameStatus Status,
    GameRoundState Round,
    GameStageState Stage,
    IReadOnlyList<VisibleGameEvent> Events,
    IReadOnlyList<PendingInputState> PendingInputs);

public sealed record VisibleGameEvent(
    GameEventId EventId,
    long Sequence,
    string EventType,
    DateTimeOffset OccurredAt)
{
    public static VisibleGameEvent FromEvent(IGameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        return new VisibleGameEvent(
            gameEvent.EventId,
            gameEvent.Sequence,
            gameEvent.GetType().Name,
            gameEvent.OccurredAt);
    }
}
