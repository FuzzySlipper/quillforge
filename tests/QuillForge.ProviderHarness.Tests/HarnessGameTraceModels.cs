using QuillForge.Core.Models;

namespace QuillForge.ProviderHarness.Tests;

public sealed record HarnessGameScenarioReport
{
    public required string ScenarioName { get; init; }
    public required HarnessGameTrace GameTrace { get; init; }
    public HarnessPersistedRunReport? PersistedReport { get; init; }
}

public sealed record HarnessGameTrace
{
    public string? RunId { get; init; }
    public required string ScenarioName { get; init; }
    public required string DeterminismMode { get; init; }
    public required string DeterminismDescription { get; init; }
    public bool LiveProviderRun { get; init; }
    public required Guid SessionId { get; init; }
    public string? GameInstanceId { get; init; }
    public string? TemplateId { get; init; }
    public string? ModuleId { get; init; }
    public string? ModuleVersion { get; init; }
    public long? Seed { get; init; }
    public required string Status { get; init; }
    public int? RoundNumber { get; init; }
    public string? StageId { get; init; }
    public string? StageName { get; init; }
    public string? FinalOutcome { get; init; }
    public IReadOnlyList<HarnessGameAgentTrace> Agents { get; init; } = [];
    public IReadOnlyList<HarnessGamePromptEnvelopeTrace> PromptEnvelopes { get; init; } = [];
    public IReadOnlyList<HarnessGameActionTrace> Actions { get; init; } = [];
    public IReadOnlyList<HarnessGameMemorySummaryTrace> MemorySummaries { get; init; } = [];
    public IReadOnlyList<HarnessGameEventTrace> EngineEvents { get; init; } = [];
    public IReadOnlyList<HarnessGameRuntimeEventTrace> RuntimeEvents { get; init; } = [];
    public IReadOnlyList<HarnessGameCommunicationTrace> PublicFeed { get; init; } = [];
    public IReadOnlyDictionary<string, IReadOnlyList<HarnessGameVisibleEventTrace>> PrivateEventsByParticipant { get; init; } = new Dictionary<string, IReadOnlyList<HarnessGameVisibleEventTrace>>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, IReadOnlyList<HarnessGameCommunicationTrace>> PrivateFeedByParticipant { get; init; } = new Dictionary<string, IReadOnlyList<HarnessGameCommunicationTrace>>(StringComparer.Ordinal);
    public HarnessGameFailureSurfaceTrace FailureSurface { get; init; } = new();
    public HarnessUsage Usage { get; init; } = new(0, 0);
}

public sealed record HarnessGameAgentTrace
{
    public required string ParticipantId { get; init; }
    public required string DisplayName { get; init; }
    public required string Kind { get; init; }
    public string? ProviderAlias { get; init; }
    public string? Model { get; init; }
    public HarnessGamePromptCursorTrace? PromptCursor { get; init; }
    public HarnessGameMemoryStateTrace? Memory { get; init; }
}

public sealed record HarnessGamePromptCursorTrace
{
    public long PublicEngineEventSequence { get; init; }
    public IReadOnlyList<string> PrivateEngineEventIds { get; init; } = [];
    public long CommunicationSequence { get; init; }
    public int MemoryRevision { get; init; }
    public string? LastPromptEnvelopeId { get; init; }
}

public sealed record HarnessGameMemoryStateTrace
{
    public int Revision { get; init; }
    public int TokenBudget { get; init; }
    public string? Summary { get; init; }
    public string? ContentHash { get; init; }
    public int LastSummarizedRoundNumber { get; init; }
    public long LastSummarizedPublicEngineEventSequence { get; init; }
    public IReadOnlyList<string> LastSummarizedPrivateEventIds { get; init; } = [];
    public long LastSummarizedCommunicationSequence { get; init; }
}

public sealed record HarnessGamePromptEnvelopeTrace
{
    public required string EnvelopeId { get; init; }
    public required string ParticipantId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public long EngineCursorSequence { get; init; }
    public long CommunicationCursorSequence { get; init; }
    public int MemoryRevision { get; init; }
    public string? ProviderAlias { get; init; }
    public string? Model { get; init; }
    public int? PromptTokens { get; init; }
    public int? ResponseTokens { get; init; }
    public string? PromptContentHash { get; init; }
    public string? ResponseContentHash { get; init; }
    public string? PromptPreview { get; init; }
    public string? ResponsePreview { get; init; }
}

public sealed record HarnessGameActionTrace
{
    public required string ParticipantId { get; init; }
    public string? PendingInputId { get; init; }
    public required string Outcome { get; init; }
    public required string ReasonCode { get; init; }
    public required string Message { get; init; }
    public string? ProviderAlias { get; init; }
    public string? Model { get; init; }
    public string? ChoiceName { get; init; }
    public HarnessUsage Usage { get; init; } = new(0, 0);
}

public sealed record HarnessGameMemorySummaryTrace
{
    public required string ParticipantId { get; init; }
    public int RoundNumber { get; init; }
    public required string Outcome { get; init; }
    public required string ReasonCode { get; init; }
    public required string Message { get; init; }
    public string? ProviderAlias { get; init; }
    public string? Model { get; init; }
    public HarnessUsage Usage { get; init; } = new(0, 0);
    public string? DecisionId { get; init; }
    public bool ExceededTokenBudget { get; init; }
    public bool Trimmed { get; init; }
    public bool Retried { get; init; }
    public string? RejectionReason { get; init; }
    public string? SummaryContentHash { get; init; }
}

public sealed record HarnessGameEventTrace
{
    public required string EventId { get; init; }
    public long Sequence { get; init; }
    public required string EventType { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public required string Visibility { get; init; }
    public string? ParticipantId { get; init; }
    public string? PendingInputId { get; init; }
    public string? ChoiceName { get; init; }
    public string? ReasonCode { get; init; }
    public string? OutcomeName { get; init; }
}

public sealed record HarnessGameRuntimeEventTrace
{
    public required string EventName { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public string? ParticipantId { get; init; }
    public string? ProviderAlias { get; init; }
    public string? Model { get; init; }
    public int? PromptTokens { get; init; }
    public int? ResponseTokens { get; init; }
}

public sealed record HarnessGameVisibleEventTrace(
    string EventId,
    long Sequence,
    string EventType,
    DateTimeOffset OccurredAt);

public sealed record HarnessGameCommunicationTrace
{
    public long Sequence { get; init; }
    public required string Kind { get; init; }
    public string? MessageId { get; init; }
    public string? LinkId { get; init; }
    public string? AuthorParticipantId { get; init; }
    public string? AuthorKind { get; init; }
    public IReadOnlyList<string> RecipientParticipantIds { get; init; } = [];
    public string? Text { get; init; }
    public string? GameEventId { get; init; }
    public long? GameEventSequence { get; init; }
    public string? Summary { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record HarnessGameFailureSurfaceTrace
{
    public IReadOnlyList<HarnessGameAgentFailureTrace> AgentResponseRejected { get; init; } = [];
    public IReadOnlyList<HarnessGameNoActionTrace> NoActionTaken { get; init; } = [];
    public IReadOnlyList<HarnessGameIntentCommandRejectedTrace> IntentCommandRejected { get; init; } = [];
    public IReadOnlyList<HarnessGameAbortedTrace> GameAborted { get; init; } = [];
    public IReadOnlyList<HarnessGameMemoryDecisionFailureTrace> MemoryDecisionFlags { get; init; } = [];
}

public sealed record HarnessGameAgentFailureTrace(
    string ParticipantId,
    string PendingInputId,
    string ReasonCode,
    string Reason,
    string Visibility,
    long Sequence);

public sealed record HarnessGameNoActionTrace(
    string ParticipantId,
    string PendingInputId,
    string ReasonCode,
    long Sequence);

public sealed record HarnessGameIntentCommandRejectedTrace(
    string CommandId,
    string ReasonCode,
    string Reason,
    long Sequence);

public sealed record HarnessGameAbortedTrace(
    string ReasonCode,
    long Sequence);

public sealed record HarnessGameMemoryDecisionFailureTrace(
    string ParticipantId,
    int RoundNumber,
    bool ExceededTokenBudget,
    bool Trimmed,
    bool Retried,
    string? RejectionReason,
    string? SummaryContentHash);
