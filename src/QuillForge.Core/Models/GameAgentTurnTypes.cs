using Den.RulesEngine;

namespace QuillForge.Core.Models;

public sealed record RunGameAgentTurnsCommand(
    DateTimeOffset OccurredAt,
    int? MaxConcurrency = null,
    TimeSpan? ResponseTimeout = null);

public sealed record GameAgentTurnRunResult(
    GameRuntimeState? Game,
    IReadOnlyList<GameAgentTurnParticipantResult> ParticipantResults,
    IReadOnlyList<IGameRuntimeEvent> RuntimeEvents,
    IReadOnlyList<IGameEvent> EngineEvents)
{
    public bool HasWork => ParticipantResults.Count > 0;
}

public sealed record GameAgentTurnParticipantResult(
    string ParticipantId,
    string? PendingInputId,
    GameAgentTurnOutcome Outcome,
    string ReasonCode,
    string Message,
    string? ProviderAlias,
    string? Model,
    TokenUsage Usage);

public enum GameAgentTurnOutcome
{
    Applied,
    Rejected,
    NoAction,
}

public sealed record GameAgentPromptContext(
    string GameInstanceId,
    string ParticipantId,
    string DisplayName,
    string StageId,
    string StageName,
    string ModuleDisplayName,
    IReadOnlyList<GamePromptAsset> PromptAssets,
    string SystemPromptTemplateContent,
    string? PersonaPromptContent,
    AgentVisibleEventsSnapshot VisibleEvents,
    IReadOnlyList<PendingInputState> PendingInputs,
    GameRuntimeAgentMemoryState? Memory,
    GameRuntimeAgentPromptDeliveryCursor? PromptCursor,
    GameRuntimeParticipantBinding Binding);

public sealed record GameAgentPromptAssembly(
    string SystemPrompt,
    string UserPrompt,
    long EngineCursorSequence,
    IReadOnlyList<string> DeliveredPrivateEventIds,
    long CommunicationCursorSequence,
    int MemoryRevision,
    string PromptContentHash);

public sealed record GameAgentResponseParseResult(
    bool IsAccepted,
    string? PendingInputId,
    string? ChoiceName,
    string ReasonCode,
    string Message)
{
    public static GameAgentResponseParseResult Accepted(string pendingInputId, string choiceName, string message) =>
        new(true, pendingInputId, choiceName, "parsed", message);

    public static GameAgentResponseParseResult Rejected(string reasonCode, string message) =>
        new(false, null, null, reasonCode, message);
}
