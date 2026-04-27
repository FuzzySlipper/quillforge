using System.Text.Json;
using System.Text.Json.Serialization;

namespace Den.RulesEngine;

[JsonConverter(typeof(GameEventJsonConverter))]
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

public sealed record StoredGameEvent(
    GameEventId EventId,
    long Sequence,
    GameInstanceId GameInstanceId,
    DateTimeOffset OccurredAt,
    GameEventVisibility Visibility,
    string EventType) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
{
    public static StoredGameEvent FromEvent(IGameEvent gameEvent) =>
        new(
            gameEvent.EventId,
            gameEvent.Sequence,
            gameEvent.GameInstanceId,
            gameEvent.OccurredAt,
            gameEvent.Visibility,
            gameEvent.GetType().Name);

    public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
        this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
}

public sealed class GameEventJsonConverter : JsonConverter<IGameEvent>
{
    public override IGameEvent Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var discriminator = root.GetProperty("type").GetString();
        var payload = root.TryGetProperty("event", out var eventPayload)
            ? eventPayload.GetRawText()
            : root.GetRawText();
        return discriminator switch
        {
            "game_started" => Deserialize<GameStartedEvent>(payload, options),
            "player_choice_submitted" => Deserialize<PlayerChoiceSubmittedEvent>(payload, options),
            "deterministic_effects_advanced" => Deserialize<DeterministicEffectsAdvancedEvent>(payload, options),
            "intent_command_rejected" => Deserialize<IntentCommandRejectedEvent>(payload, options),
            "no_action_taken" => Deserialize<NoActionTakenEvent>(payload, options),
            "pending_input_requested" => Deserialize<PendingInputRequestedEvent>(payload, options),
            "stage_advanced" => Deserialize<StageAdvancedEvent>(payload, options),
            "round_ended" => Deserialize<RoundEndedEvent>(payload, options),
            "round_started" => Deserialize<RoundStartedEvent>(payload, options),
            "game_ended" => Deserialize<GameEndedEvent>(payload, options),
            "game_aborted" => Deserialize<GameAbortedEvent>(payload, options),
            "stored_game_event" => Deserialize<StoredGameEvent>(payload, options),
            _ => Deserialize<StoredGameEvent>(payload, options),
        };
    }

    public override void Write(Utf8JsonWriter writer, IGameEvent value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", Discriminator(value));
        writer.WritePropertyName("event");
        SerializePayload(writer, value, options);
        writer.WriteEndObject();
    }

    private static T Deserialize<T>(string json, JsonSerializerOptions options)
        where T : IGameEvent =>
        JsonSerializer.Deserialize<T>(json, options)
        ?? throw new JsonException($"Could not deserialize game event payload as {typeof(T).Name}.");

    private static string Discriminator(IGameEvent gameEvent) => gameEvent switch
    {
        GameStartedEvent => "game_started",
        PlayerChoiceSubmittedEvent => "player_choice_submitted",
        DeterministicEffectsAdvancedEvent => "deterministic_effects_advanced",
        IntentCommandRejectedEvent => "intent_command_rejected",
        NoActionTakenEvent => "no_action_taken",
        PendingInputRequestedEvent => "pending_input_requested",
        StageAdvancedEvent => "stage_advanced",
        RoundEndedEvent => "round_ended",
        RoundStartedEvent => "round_started",
        GameEndedEvent => "game_ended",
        GameAbortedEvent => "game_aborted",
        StoredGameEvent => "stored_game_event",
        _ => "stored_game_event",
    };

    private static void SerializePayload(Utf8JsonWriter writer, IGameEvent value, JsonSerializerOptions options)
    {
        switch (value)
        {
            case GameStartedEvent known:
                JsonSerializer.Serialize(writer, known, options);
                break;
            case PlayerChoiceSubmittedEvent known:
                JsonSerializer.Serialize(writer, known, options);
                break;
            case DeterministicEffectsAdvancedEvent known:
                JsonSerializer.Serialize(writer, known, options);
                break;
            case IntentCommandRejectedEvent known:
                JsonSerializer.Serialize(writer, known, options);
                break;
            case NoActionTakenEvent known:
                JsonSerializer.Serialize(writer, known, options);
                break;
            case PendingInputRequestedEvent known:
                JsonSerializer.Serialize(writer, known, options);
                break;
            case StageAdvancedEvent known:
                JsonSerializer.Serialize(writer, known, options);
                break;
            case RoundEndedEvent known:
                JsonSerializer.Serialize(writer, known, options);
                break;
            case RoundStartedEvent known:
                JsonSerializer.Serialize(writer, known, options);
                break;
            case GameEndedEvent known:
                JsonSerializer.Serialize(writer, known, options);
                break;
            case GameAbortedEvent known:
                JsonSerializer.Serialize(writer, known, options);
                break;
            case StoredGameEvent known:
                JsonSerializer.Serialize(writer, known, options);
                break;
            default:
                JsonSerializer.Serialize(writer, StoredGameEvent.FromEvent(value), options);
                break;
        }
    }
}
