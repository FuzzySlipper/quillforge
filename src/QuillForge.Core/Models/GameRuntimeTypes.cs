using System.Text.Json.Serialization;
using Den.RulesEngine;

namespace QuillForge.Core.Models;

/// <summary>
/// Session-owned runtime state for one active or recently completed social game.
/// QuillForge services are the only writers; endpoints and adapters submit typed
/// commands to the game runtime service instead of mutating this object directly.
/// </summary>
public sealed class GameRuntimeState
{
    public GameRuntimeStatus Status { get; set; } = GameRuntimeStatus.NotStarted;

    public string? GameInstanceId { get; set; }

    public string? TemplateId { get; set; }

    public string? ModuleId { get; set; }

    public string? ModuleVersion { get; set; }

    public long? Seed { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? LastResumedAt { get; set; }

    public DateTimeOffset? LastUpdatedAt { get; set; }

    public DateTimeOffset? EndedAt { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RulesGameStateSnapshot? EngineSnapshot { get; set; }

    public ParticipantCommunicationState Communication { get; set; } = new();

    public bool HostAllowsPublicMessages { get; set; } = true;

    public bool HostAllowsDirectMessages { get; set; } = true;

    public List<GameRuntimeParticipantBinding> ParticipantBindings { get; set; } = [];

    public List<GameRuntimeEventDeliveryCursor> EventDeliveryCursors { get; set; } = [];

    public List<GameRuntimeAgentMemoryState> AgentMemories { get; set; } = [];

    public List<MemorySummaryDecision> MemorySummaryDecisions { get; set; } = [];

    public List<GameRuntimeAgentPromptDeliveryCursor> PromptCursors { get; set; } = [];

    public List<GameRuntimeAgentPromptEnvelope> PromptEnvelopes { get; set; } = [];

    public long NextHostRecordSequence { get; set; } = 1;

    public List<GameRuntimeHostRecord> HostRecords { get; set; } = [];

    [JsonIgnore]
    public bool IsActive => Status is GameRuntimeStatus.Running
        or GameRuntimeStatus.WaitingForInput
        or GameRuntimeStatus.Resolving
        or GameRuntimeStatus.WaitingOnAgentTurns;
}

[JsonConverter(typeof(JsonStringEnumConverter<GameRuntimeStatus>))]
public enum GameRuntimeStatus
{
    NotStarted,
    Running,
    WaitingForInput,
    Resolving,
    WaitingOnAgentTurns,
    Ended,
    Aborted
}

public sealed class GameRuntimeParticipantBinding
{
    public required string ParticipantId { get; set; }

    public required string DisplayName { get; set; }

    public GameRuntimeParticipantKind Kind { get; set; }

    public string? ProviderAlias { get; set; }

    public string? ModelOverride { get; set; }

    public string? CharacterPrompt { get; set; }

    public string? Personality { get; set; }

    public string? UserSeatId { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<GameRuntimeParticipantKind>))]
public enum GameRuntimeParticipantKind
{
    Human,
    Agent,
    System
}

public sealed class GameRuntimeEventDeliveryCursor
{
    public required string ParticipantId { get; set; }

    public long DeliveredThroughEngineEventSequence { get; set; }

    public long DeliveredThroughCommunicationSequence { get; set; }

    public int MemoryRevision { get; set; }

    public string? LastPromptEnvelopeId { get; set; }
}

public sealed class GameRuntimeAgentMemoryState
{
    public required string ParticipantId { get; set; }

    public int Revision { get; set; }

    public int TokenBudget { get; set; }

    public string? Summary { get; set; }

    public string? ContentHash { get; set; }

    public int LastSummarizedRoundNumber { get; set; }

    public long LastSummarizedPublicEngineEventSequence { get; set; }

    public List<string> LastSummarizedPrivateEventIds { get; set; } = [];

    public long LastSummarizedCommunicationSequence { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }
}

public sealed class GameRuntimeAgentPromptDeliveryCursor
{
    public required string ParticipantId { get; set; }

    public long LastDeliveredPublicEngineEventSequence { get; set; }

    public List<string> DeliveredPrivateEventIds { get; set; } = [];

    public long CommunicationDeliveredThroughSequence { get; set; }

    public int MemoryRevision { get; set; }

    public string? LastPromptEnvelopeId { get; set; }
}

public sealed class GameRuntimeAgentPromptEnvelope
{
    public required string EnvelopeId { get; set; }

    public required string ParticipantId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public long EngineCursorSequence { get; set; }

    public long CommunicationCursorSequence { get; set; }

    public int MemoryRevision { get; set; }

    public string? ProviderAlias { get; set; }

    public string? Model { get; set; }

    public int? PromptTokens { get; set; }

    public int? ResponseTokens { get; set; }

    public string? PromptContentHash { get; set; }

    public string? ResponseContentHash { get; set; }

    public string? PromptText { get; set; }

    public string? ResponseText { get; set; }
}

public sealed class GameRuntimeHostRecord
{
    public Guid RecordId { get; set; } = Guid.CreateVersion7();

    public long Sequence { get; set; }

    public GameRuntimeHostRecordKind Kind { get; set; }

    public DateTimeOffset OccurredAt { get; set; }

    public string ReasonCode { get; set; } = string.Empty;

    public string Summary { get; set; } = string.Empty;

    public Guid? SourceSessionId { get; set; }

    public Guid? TargetSessionId { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter<GameRuntimeHostRecordKind>))]
public enum GameRuntimeHostRecordKind
{
    Started,
    Resumed,
    EngineCommandApplied,
    Aborted,
    Forked,
    AgentPromptRecorded,
    AgentMemorySummaryRecorded
}
