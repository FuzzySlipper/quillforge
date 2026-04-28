using Den.RulesEngine;

namespace QuillForge.Core.Models;

public sealed record RunGameAgentMemorySummariesCommand(
    DateTimeOffset OccurredAt,
    int? MaxSummaries = null);

public sealed record GameAgentMemorySummaryRunResult(
    GameRuntimeState? Game,
    IReadOnlyList<GameAgentMemorySummaryParticipantResult> ParticipantResults,
    IReadOnlyList<IGameRuntimeEvent> RuntimeEvents)
{
    public bool HasWork => ParticipantResults.Count > 0;
}

public sealed record GameAgentMemorySummaryParticipantResult(
    string ParticipantId,
    int RoundNumber,
    GameAgentMemorySummaryOutcome Outcome,
    string ReasonCode,
    string Message,
    string? ProviderAlias,
    string? Model,
    TokenUsage Usage);

public enum GameAgentMemorySummaryOutcome
{
    Recorded,
    Rejected,
}

public sealed record AgentVisibleEventsCursor(
    long PublicEngineEventSequence,
    IReadOnlyList<string> PrivateEngineEventIds,
    long CommunicationSequence,
    int MemoryRevision)
{
    public static AgentVisibleEventsCursor Empty { get; } = new(0, [], 0, 0);
}

/// <summary>
/// Trusted, typed projection of everything a single agent participant may use
/// while assembling a prompt. Prompt builders accept this type instead of raw
/// engine journal entries so hidden facts cannot be passed by accident.
/// </summary>
public sealed record AgentVisibleEventsSnapshot(
    string GameInstanceId,
    string ParticipantId,
    AgentVisibleEventsCursor PriorCursor,
    AgentVisibleEventsCursor NewCursor,
    IReadOnlyList<VisibleGameEvent> EngineEvents,
    IReadOnlyList<ParticipantFeedEntry> FeedEntries)
{
    public bool HasNewEvents => EngineEvents.Count > 0 || FeedEntries.Count > 0;
}

public sealed record GameAgentMemorySummaryPromptContext(
    string GameInstanceId,
    string ParticipantId,
    string DisplayName,
    int RoundNumber,
    string ModuleDisplayName,
    IReadOnlyList<GamePromptAsset> PromptAssets,
    string? PriorMemorySummary,
    int TokenBudget,
    AgentVisibleEventsSnapshot VisibleEvents,
    GameRuntimeParticipantBinding Binding);

public sealed record GameAgentMemorySummaryPromptAssembly(
    string SystemPrompt,
    string UserPrompt,
    AgentVisibleEventsCursor PriorCursor,
    AgentVisibleEventsCursor NewCursor,
    int MemoryRevision,
    string PromptContentHash);

public sealed record MemorySummaryDecision(
    string DecisionId,
    string ParticipantId,
    int RoundNumber,
    DateTimeOffset CreatedAt,
    AgentVisibleEventsCursor PriorCursor,
    AgentVisibleEventsCursor NewCursor,
    int? PromptTokens,
    int? ResponseTokens,
    bool ExceededTokenBudget,
    bool Trimmed,
    bool Retried,
    string? ProviderAlias,
    string? Model,
    string SnapshotId,
    string? RejectionReason,
    string? SummaryContentHash);

public sealed record GameAgentMemorySummaryParseResult(
    bool IsAccepted,
    string? Summary,
    string ReasonCode,
    string Message)
{
    public static GameAgentMemorySummaryParseResult Accepted(string summary) =>
        new(true, summary, "parsed", "Memory summary accepted.");

    public static GameAgentMemorySummaryParseResult Rejected(string reasonCode, string message) =>
        new(false, null, reasonCode, message);
}
