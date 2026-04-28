using Den.RulesEngine;

namespace QuillForge.Core.Models;

/// <summary>
/// Typed, debug-oriented projection for investigating active game behavior.
/// It summarizes engine facts instead of exposing raw event payloads so public
/// status/session contracts do not accidentally leak private module data.
/// </summary>
public sealed record GameInspectorProjection
{
    public required Guid SessionId { get; init; }

    public bool HasGame { get; init; }

    public string? GameInstanceId { get; init; }

    public string? TemplateId { get; init; }

    public string? ModuleId { get; init; }

    public string? ModuleVersion { get; init; }

    public long? Seed { get; init; }

    public string? RuntimeStatus { get; init; }

    public GameInspectorEngineProjection? Engine { get; init; }

    public IReadOnlyList<GameInspectorParticipantProjection> Participants { get; init; } = [];

    public IReadOnlyList<GameInspectorPromptCursorProjection> PromptCursors { get; init; } = [];

    public IReadOnlyList<GameInspectorEventDeliveryCursorProjection> EventDeliveryCursors { get; init; } = [];

    public IReadOnlyList<GameInspectorMemoryProjection> AgentMemories { get; init; } = [];

    public IReadOnlyList<GameInspectorPromptEnvelopeProjection> PromptEnvelopes { get; init; } = [];

    public SessionUsageSummary TokenUsage { get; init; } = new();
}

public sealed record GameInspectorEngineProjection
{
    public required string Status { get; init; }

    public required int RoundNumber { get; init; }

    public required string StageId { get; init; }

    public required string StageName { get; init; }

    public required bool StageAllowsPublicMessages { get; init; }

    public required bool StageAllowsDirectMessages { get; init; }

    public required long EventJournalNextSequence { get; init; }

    public required IReadOnlyList<GameInspectorEventProjection> EventJournal { get; init; }

    public required IReadOnlyList<GameInspectorPendingInputProjection> PendingInputs { get; init; }
}

public sealed record GameInspectorEventProjection
{
    public required string EventId { get; init; }

    public required long Sequence { get; init; }

    public required string EventType { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }

    public required string Visibility { get; init; }

    public string? ParticipantId { get; init; }

    public string? PendingInputId { get; init; }

    public string? ReasonCode { get; init; }

    public string? OutcomeName { get; init; }
}

public sealed record GameInspectorPendingInputProjection
{
    public required string PendingInputId { get; init; }

    public required string ParticipantId { get; init; }

    public required string StageId { get; init; }

    public required string IntentName { get; init; }

    public required string Status { get; init; }

    public required IReadOnlyList<string> LegalChoiceNames { get; init; }
}

public sealed record GameInspectorParticipantProjection
{
    public required string ParticipantId { get; init; }

    public required string DisplayName { get; init; }

    public required string Kind { get; init; }

    public required bool IsActive { get; init; }

    public string? ProviderAlias { get; init; }

    public string? Model { get; init; }
}

public sealed record GameInspectorPromptCursorProjection
{
    public required string ParticipantId { get; init; }

    public required long LastDeliveredPublicEngineEventSequence { get; init; }

    public required IReadOnlyList<string> DeliveredPrivateEventIds { get; init; }

    public required long CommunicationDeliveredThroughSequence { get; init; }

    public required int MemoryRevision { get; init; }

    public string? LastPromptEnvelopeId { get; init; }
}

public sealed record GameInspectorEventDeliveryCursorProjection
{
    public required string ParticipantId { get; init; }

    public required long DeliveredThroughEngineEventSequence { get; init; }

    public required long DeliveredThroughCommunicationSequence { get; init; }

    public required int MemoryRevision { get; init; }

    public string? LastPromptEnvelopeId { get; init; }
}

public sealed record GameInspectorMemoryProjection
{
    public required string ParticipantId { get; init; }

    public required int Revision { get; init; }

    public required int TokenBudget { get; init; }

    public string? Summary { get; init; }

    public string? ContentHash { get; init; }

    public required int LastSummarizedRoundNumber { get; init; }

    public required long LastSummarizedPublicEngineEventSequence { get; init; }

    public required IReadOnlyList<string> LastSummarizedPrivateEventIds { get; init; }

    public required long LastSummarizedCommunicationSequence { get; init; }

    public DateTimeOffset? UpdatedAt { get; init; }
}

public sealed record GameInspectorPromptEnvelopeProjection
{
    public required string EnvelopeId { get; init; }

    public required string ParticipantId { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required long EngineCursorSequence { get; init; }

    public required long CommunicationCursorSequence { get; init; }

    public required int MemoryRevision { get; init; }

    public string? ProviderAlias { get; init; }

    public string? Model { get; init; }

    public int? PromptTokens { get; init; }

    public int? ResponseTokens { get; init; }

    public string? PromptContentHash { get; init; }

    public string? ResponseContentHash { get; init; }

    public string? PromptPreview { get; init; }

    public string? ResponsePreview { get; init; }
}
