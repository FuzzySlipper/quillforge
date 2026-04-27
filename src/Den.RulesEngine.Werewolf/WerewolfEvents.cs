using Den.RulesEngine;

namespace Den.RulesEngine.Werewolf;

public sealed record WerewolfRoleAssignedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    ParticipantId ParticipantId,
    WerewolfRole Role) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static WerewolfRoleAssignedEvent Create(GameInstanceId gameInstanceId, ParticipantId participantId, WerewolfRole role) =>
        new(default, 0, gameInstanceId, default, GameEventVisibility.HiddenSystemOnly, participantId, role);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed record WerewolfRoleRevealedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    ParticipantId ParticipantId,
    WerewolfRole Role) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static WerewolfRoleRevealedEvent Create(GameInstanceId gameInstanceId, ParticipantId participantId, WerewolfRole role) =>
        new(default, 0, gameInstanceId, default, GameEventVisibility.PrivateToParticipant(participantId), participantId, role);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed record WerewolfTeamRevealedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    IReadOnlyList<ParticipantId> WerewolfParticipantIds) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static WerewolfTeamRevealedEvent Create(GameInstanceId gameInstanceId, IReadOnlyList<ParticipantId> werewolfParticipantIds) =>
        new(default, 0, gameInstanceId, default, GameEventVisibility.PrivateToSet(WerewolfConstants.WerewolfTeamSetId), werewolfParticipantIds.ToArray());

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed record WerewolfStageStartedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    GameStageId StageId,
    int RoundNumber) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static WerewolfStageStartedEvent Create(GameInstanceId gameInstanceId, GameStageId stageId, int roundNumber) =>
        new(default, 0, gameInstanceId, default, GameEventVisibility.Public, stageId, roundNumber);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed record WerewolfNightActionsResolvedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    int RoundNumber) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static WerewolfNightActionsResolvedEvent Create(GameInstanceId gameInstanceId, int roundNumber) =>
        new(default, 0, gameInstanceId, default, GameEventVisibility.Public, roundNumber);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed record WerewolfVoteRecordedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    ParticipantId VoterParticipantId,
    ParticipantId? TargetParticipantId) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static WerewolfVoteRecordedEvent Create(GameInstanceId gameInstanceId, ParticipantId voterParticipantId, ParticipantId? targetParticipantId) =>
        new(default, 0, gameInstanceId, default, GameEventVisibility.Public, voterParticipantId, targetParticipantId);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed record WerewolfVoteResolvedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    ParticipantId? EliminatedParticipantId,
    bool IsTie) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static WerewolfVoteResolvedEvent Create(GameInstanceId gameInstanceId, ParticipantId? eliminatedParticipantId, bool isTie) =>
        new(default, 0, gameInstanceId, default, GameEventVisibility.Public, eliminatedParticipantId, isTie);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed record WerewolfPlayerEliminatedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    ParticipantId ParticipantId,
    WerewolfRole Role) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static WerewolfPlayerEliminatedEvent Create(GameInstanceId gameInstanceId, ParticipantId participantId, WerewolfRole role) =>
        new(default, 0, gameInstanceId, default, GameEventVisibility.Public, participantId, role);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed record WerewolfWinConditionResolvedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    WerewolfWinner Winner,
    string ReasonCode) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static WerewolfWinConditionResolvedEvent Create(GameInstanceId gameInstanceId, WerewolfWinner winner, string reasonCode) =>
        new(default, 0, gameInstanceId, default, GameEventVisibility.Public, winner, reasonCode);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}
