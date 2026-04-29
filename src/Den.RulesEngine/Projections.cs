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

    public PlayerGameProjection ProjectPlayer(GameVisibilityProjectionInput input, ParticipantId participantId)
    {
        ArgumentNullException.ThrowIfNull(input);

        var participant = input.FindParticipant(participantId)
            ?? throw new ArgumentException("Participant is not registered in this game.", nameof(participantId));

        var visibleEvents = input.EventJournal.Events
            .Where(gameEvent => gameEvent.Visibility.IsVisibleTo(participant))
            .Select(VisibleGameEvent.FromEvent)
            .ToArray();

        var pendingInputs = input.PendingInputs
            .Where(pendingInput => pendingInput.IsWaitingFor(participantId))
            .ToArray();

        return new PlayerGameProjection(
            input.GameInstanceId,
            participant,
            input.Status,
            input.Round,
            input.Stage,
            visibleEvents,
            pendingInputs);
    }
}

public sealed record GameVisibilityProjectionInput(
    GameInstanceId GameInstanceId,
    RulesGameStatus Status,
    GameRoundState Round,
    GameStageState Stage,
    IReadOnlyList<ParticipantState> Participants,
    IReadOnlyList<PendingInputState> PendingInputs,
    GameEventJournal EventJournal)
{
    public static GameVisibilityProjectionInput FromState(RulesGameState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new GameVisibilityProjectionInput(
            state.GameInstanceId,
            state.Status,
            state.Round,
            state.Stage,
            state.Participants.ToArray(),
            state.PendingInputs.ToArray(),
            state.EventJournal);
    }

    public ParticipantState? FindParticipant(ParticipantId participantId) =>
        Participants.FirstOrDefault(participant => participant.ParticipantId == participantId);
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
