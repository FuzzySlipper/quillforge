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
    int? RoundNumber,
    string? StageId,
    string? StageName,
    IReadOnlyList<GameBridgeParticipantView> Roster,
    GameBridgePublicView Public,
    GameBridgePlayerView? Player)
{
    public GameBridgeModuleAuthoringView? ModuleAuthoring { get; init; }
}

public sealed record GameBridgeModuleAuthoringView(
    IReadOnlyList<GameBridgeSetupFieldView> SetupFields,
    IReadOnlyList<GameBridgeStageHookView> Stages,
    IReadOnlyList<GameBridgeActionFormView> ActionForms,
    IReadOnlyList<GameBridgePromptAssetView> PromptAssets,
    GameBridgeCommunicationCapabilitiesView CommunicationCapabilities,
    GameBridgeMemoryExpectationsView MemoryExpectations,
    GameBridgeProjectionCapabilitiesView ProjectionCapabilities);

public sealed record GameBridgeSetupFieldView(
    string Name,
    string ValueKind,
    bool IsRequired,
    string DisplayName,
    string Description);

public sealed record GameBridgeStageHookView(
    string StageId,
    string DisplayName,
    string Description,
    int Sequence,
    bool AllowsPublicMessages,
    bool AllowsDirectMessages);

public sealed record GameBridgeActionFormView(
    string IntentName,
    string StageId,
    string DisplayName,
    string Description,
    string Layout,
    IReadOnlyList<GameBridgeActionFieldView> Fields);

public sealed record GameBridgeActionFieldView(
    string Name,
    string ValueKind,
    bool IsRequired,
    string DisplayName,
    string Description);

public sealed record GameBridgePromptAssetView(
    string AssetId,
    string Kind,
    bool IsRequired);

public sealed record GameBridgeCommunicationCapabilitiesView(
    bool AllowsPublicChannelMessages,
    bool AllowsDirectMessages);

public sealed record GameBridgeMemoryExpectationsView(
    bool UsesRoundSummaries,
    int SuggestedSummaryTokenBudget,
    int MaximumRetainedRoundSummaries);

public sealed record GameBridgeProjectionCapabilitiesView(
    bool SupportsPublicEventProjection,
    bool SupportsParticipantPrivateProjection,
    bool SupportsHostInspectorProjection);

public sealed record GameBridgePublicView(
    IReadOnlyList<GameBridgeNarrationEntry> Narration,
    IReadOnlyList<ParticipantFeedEntry> Feed);

public sealed record GameBridgeParticipantView(
    string ParticipantId,
    string DisplayName,
    GameRuntimeParticipantKind Kind,
    bool IsJoined,
    bool IsCurrentPlayer);

public sealed record GameBridgePlayerView(
    string ParticipantId,
    string DisplayName,
    IReadOnlyList<VisibleGameEvent> EngineEvents,
    IReadOnlyList<PendingInputState> PendingInputs,
    IReadOnlyList<ParticipantFeedEntry> Feed,
    GameRuntimeEventDeliveryCursor? Cursor)
{
    public IReadOnlyList<GameBridgeActionFormView> ActionForms { get; init; } = [];
}

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
