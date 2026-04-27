using System.Text.Json.Serialization;

namespace Den.RulesEngine;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(GameStartedEvent), "game_started")]
[JsonDerivedType(typeof(PlayerChoiceSubmittedEvent), "player_choice_submitted")]
[JsonDerivedType(typeof(DeterministicEffectsAdvancedEvent), "deterministic_effects_advanced")]
[JsonDerivedType(typeof(IntentCommandRejectedEvent), "intent_command_rejected")]
[JsonDerivedType(typeof(NoActionTakenEvent), "no_action_taken")]
[JsonDerivedType(typeof(PendingInputRequestedEvent), "pending_input_requested")]
[JsonDerivedType(typeof(StageAdvancedEvent), "stage_advanced")]
[JsonDerivedType(typeof(RoundEndedEvent), "round_ended")]
[JsonDerivedType(typeof(RoundStartedEvent), "round_started")]
[JsonDerivedType(typeof(GameEndedEvent), "game_ended")]
[JsonDerivedType(typeof(GameAbortedEvent), "game_aborted")]
public interface IGameEvent
{
    GameEventId EventId { get; }

    long Sequence { get; }

    GameInstanceId GameInstanceId { get; }

    DateTimeOffset OccurredAt { get; }

    GameEventVisibility Visibility { get; }

    IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt);
}

public abstract record GameEventBase(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility) : IGameEvent
{
    public abstract IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt);
}

public sealed record GameStartedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    GameModuleId ModuleId,
    GameModuleVersion ModuleVersion,
    long Seed) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static GameStartedEvent Create(GameInstanceId gameInstanceId, GameModuleId moduleId, GameModuleVersion moduleVersion, long seed) =>
        new(default, 0, gameInstanceId, default, GameEventVisibility.Public, moduleId, moduleVersion, seed);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed record PlayerChoiceSubmittedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    PendingInputId PendingInputId,
    ParticipantId ParticipantId,
    string ChoiceName) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static PlayerChoiceSubmittedEvent Create(
        GameInstanceId gameInstanceId,
        PendingInputId pendingInputId,
        ParticipantId participantId,
        string choiceName,
        GameEventVisibility visibility) =>
        new(default, 0, gameInstanceId, default, visibility, pendingInputId, participantId, choiceName);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed record DeterministicEffectsAdvancedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    string EffectName) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static DeterministicEffectsAdvancedEvent Create(GameInstanceId gameInstanceId, string effectName) =>
        new(default, 0, gameInstanceId, default, GameEventVisibility.HiddenSystemOnly, effectName);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed record IntentCommandRejectedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    GameIntentCommandId CommandId,
    string ReasonCode,
    string Reason) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static IntentCommandRejectedEvent Create(IGameIntentCommand command, string reasonCode, string reason) =>
        new(default, 0, command.GameInstanceId, default, GameEventVisibility.HiddenSystemOnly, command.CommandId, reasonCode, reason);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed record NoActionTakenEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    PendingInputId PendingInputId,
    ParticipantId ParticipantId,
    string ReasonCode) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static NoActionTakenEvent Create(
        GameInstanceId gameInstanceId,
        PendingInputId pendingInputId,
        ParticipantId participantId,
        string reasonCode) =>
        new(default, 0, gameInstanceId, default, GameEventVisibility.Public, pendingInputId, participantId, reasonCode);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed record PendingInputRequestedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    PendingInputId PendingInputId,
    ParticipantId ParticipantId,
    GameStageId StageId,
    string IntentName) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static PendingInputRequestedEvent Create(
        GameInstanceId gameInstanceId,
        PendingInputId pendingInputId,
        ParticipantId participantId,
        GameStageId stageId,
        string intentName) =>
        new(
            default,
            0,
            gameInstanceId,
            default,
            GameEventVisibility.PrivateToParticipant(participantId),
            pendingInputId,
            participantId,
            stageId,
            intentName);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed record StageAdvancedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    GameStageId PreviousStageId,
    GameStageId NextStageId) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static StageAdvancedEvent Create(
        GameInstanceId gameInstanceId,
        GameStageId previousStageId,
        GameStageId nextStageId) =>
        new(default, 0, gameInstanceId, default, GameEventVisibility.Public, previousStageId, nextStageId);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed record RoundEndedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    int RoundNumber,
    string ReasonCode) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static RoundEndedEvent Create(GameInstanceId gameInstanceId, int roundNumber, string reasonCode) =>
        new(default, 0, gameInstanceId, default, GameEventVisibility.Public, roundNumber, reasonCode);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed record RoundStartedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    int RoundNumber) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static RoundStartedEvent Create(GameInstanceId gameInstanceId, int roundNumber) =>
        new(default, 0, gameInstanceId, default, GameEventVisibility.Public, roundNumber);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed record GameEndedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    string OutcomeName) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static GameEndedEvent Create(GameInstanceId gameInstanceId, string outcomeName) =>
        new(default, 0, gameInstanceId, default, GameEventVisibility.Public, outcomeName);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed record GameAbortedEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    string ReasonCode) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static GameAbortedEvent Create(GameInstanceId gameInstanceId, string reasonCode) =>
        new(default, 0, gameInstanceId, default, GameEventVisibility.Public, reasonCode);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}
