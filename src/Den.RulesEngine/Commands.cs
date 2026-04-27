namespace Den.RulesEngine;

public interface IGameIntentCommand
{
    GameIntentCommandId CommandId { get; }

    GameInstanceId GameInstanceId { get; }
}

public sealed record StartGameIntentCommand(
    GameIntentCommandId CommandId,
    GameInstanceId GameInstanceId,
    GameModuleId ModuleId,
    GameModuleVersion ModuleVersion,
    long Seed,
    GameSetup Setup,
    IReadOnlyList<ParticipantSetup> Participants) : IGameIntentCommand;

public sealed record SubmitPlayerChoiceIntentCommand(
    GameIntentCommandId CommandId,
    GameInstanceId GameInstanceId,
    PendingInputId PendingInputId,
    ParticipantId ParticipantId,
    string ChoiceName) : IGameIntentCommand;

public sealed record AdvanceDeterministicEffectsIntentCommand(
    GameIntentCommandId CommandId,
    GameInstanceId GameInstanceId,
    string EffectName) : IGameIntentCommand;

public sealed record RequestPendingInputIntentCommand(
    GameIntentCommandId CommandId,
    GameInstanceId GameInstanceId,
    GameStageId StageId,
    string IntentName,
    IReadOnlyList<LegalIntentOption> LegalOptions,
    PendingInputAudience Audience) : IGameIntentCommand;

public sealed record AdvanceStageIntentCommand(
    GameIntentCommandId CommandId,
    GameInstanceId GameInstanceId,
    GameStageState NextStage) : IGameIntentCommand;

public sealed record EndRoundIntentCommand(
    GameIntentCommandId CommandId,
    GameInstanceId GameInstanceId,
    string ReasonCode) : IGameIntentCommand;

public sealed record EndGameIntentCommand(
    GameIntentCommandId CommandId,
    GameInstanceId GameInstanceId,
    string OutcomeName) : IGameIntentCommand;

public sealed record AbortGameIntentCommand(
    GameIntentCommandId CommandId,
    GameInstanceId GameInstanceId,
    string ReasonCode) : IGameIntentCommand;

public sealed record PendingInputAudience(
    PendingInputAudienceKind Kind,
    ParticipantId? ParticipantId,
    IReadOnlyList<ParticipantId> ParticipantIds)
{
    public static PendingInputAudience One(ParticipantId participantId) =>
        new(PendingInputAudienceKind.OneParticipant, participantId, []);

    public static PendingInputAudience Many(IReadOnlyList<ParticipantId> participantIds) =>
        new(PendingInputAudienceKind.ManyParticipants, null, participantIds.ToArray());

    public static PendingInputAudience AllActiveParticipants { get; } =
        new(PendingInputAudienceKind.AllActiveParticipants, null, []);
}

public enum PendingInputAudienceKind
{
    OneParticipant,
    ManyParticipants,
    AllActiveParticipants
}

public sealed record ParticipantSetup(
    ParticipantId ParticipantId,
    string DisplayName,
    ParticipantKind Kind);

public sealed record GameSetup(IReadOnlyList<GameSetupValue> Values)
{
    public static GameSetup Empty { get; } = new([]);

    public GameSetupValue? FindValue(string name) =>
        Values.FirstOrDefault(value => string.Equals(value.Name, name, StringComparison.Ordinal));
}

public abstract record GameSetupValue(string Name);

public sealed record StringGameSetupValue(string Name, string Value) : GameSetupValue(Name);

public sealed record IntGameSetupValue(string Name, int Value) : GameSetupValue(Name);

public sealed record BoolGameSetupValue(string Name, bool Value) : GameSetupValue(Name);

public sealed record ParticipantIdGameSetupValue(string Name, ParticipantId Value) : GameSetupValue(Name);

public sealed record ParticipantSetGameSetupValue(string Name, IReadOnlyList<ParticipantId> Values) : GameSetupValue(Name);
