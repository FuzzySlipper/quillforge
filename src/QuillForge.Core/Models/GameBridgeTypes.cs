using Den.RulesEngine;

namespace QuillForge.Core.Models;

public sealed record StartGameFromTemplateCommand(
    string TemplateId,
    string? UserDisplayName,
    long? Seed,
    DateTimeOffset StartedAt);

public sealed record SubmitGameTypedActionCommand(
    string ParticipantId,
    string PendingInputId,
    string ChoiceName,
    DateTimeOffset OccurredAt);

public sealed record SubmitGameTextActionCommand(
    string ParticipantId,
    string Text,
    DateTimeOffset OccurredAt);

public sealed record EndGameBridgeCommand(
    GameIntentCommandId CommandId,
    string OutcomeName,
    DateTimeOffset EndedAt);

public sealed record GameBridgeMutationResult(
    GameBridgeView View,
    IReadOnlyList<IGameRuntimeEvent> RuntimeEvents,
    IReadOnlyList<IGameEvent> EngineEvents,
    IReadOnlyList<IParticipantCommunicationEvent> CommunicationEvents)
{
    public static GameBridgeMutationResult FromRuntime(GameBridgeView view, GameRuntimeMutationResult result) =>
        new(view, result.RuntimeEvents, result.EngineEvents, []);

    public static GameBridgeMutationResult FromCommunication(GameBridgeView view, GameRuntimeCommunicationMutationResult result) =>
        new(view, [], [], result.CommunicationEvents);
}

public sealed record GameBridgeView(
    GameRuntimeStatus Status,
    string? GameInstanceId,
    string? TemplateId,
    string? ModuleId,
    string? ModuleVersion,
    GameBridgePublicView Public,
    GameBridgePlayerView? Player);

public sealed record GameBridgePublicView(
    IReadOnlyList<GameBridgeNarrationEntry> Narration,
    IReadOnlyList<ParticipantFeedEntry> Feed);

public sealed record GameBridgePlayerView(
    string ParticipantId,
    string DisplayName,
    IReadOnlyList<VisibleGameEvent> EngineEvents,
    IReadOnlyList<PendingInputState> PendingInputs,
    IReadOnlyList<ParticipantFeedEntry> Feed,
    GameRuntimeEventDeliveryCursor? Cursor);

public sealed record GameBridgeNarrationEntry(
    string EventId,
    long Sequence,
    string EventType,
    string Text,
    DateTimeOffset OccurredAt);

public sealed record GameIntentTranslationRequest(
    string GameInstanceId,
    string ParticipantId,
    string Text,
    IReadOnlyList<PendingInputState> PendingInputs,
    DateTimeOffset OccurredAt);

public sealed record GameIntentTranslationResult(
    bool IsAccepted,
    string? PendingInputId,
    string? ChoiceName,
    double Confidence,
    string ReasonCode,
    string Message)
{
    public static GameIntentTranslationResult Accepted(
        string pendingInputId,
        string choiceName,
        double confidence,
        string message) =>
        new(true, pendingInputId, choiceName, confidence, "translated", message);

    public static GameIntentTranslationResult Rejected(string reasonCode, string message) =>
        new(false, null, null, 0, reasonCode, message);
}
