using Den.RulesEngine;

namespace QuillForge.Core.Models;

public sealed record StartGameRuntimeCommand(
    string? TemplateId,
    GameInstanceId GameInstanceId,
    GameModuleId ModuleId,
    GameModuleVersion ModuleVersion,
    long Seed,
    GameTemplateVersion TemplateVersion,
    GameSetup Setup,
    IReadOnlyList<ParticipantSetup> Participants,
    IReadOnlyList<GameRuntimeParticipantBinding> ParticipantBindings,
    int AgentMemoryTokenBudget,
    DateTimeOffset StartedAt,
    bool HostAllowsPublicMessages = true,
    bool HostAllowsDirectMessages = true);

public sealed record ApplyGameRuntimeEngineCommand(
    IGameIntentCommand EngineCommand,
    DateTimeOffset OccurredAt);

public sealed record ResumeGameRuntimeCommand(
    DateTimeOffset ResumedAt);

public sealed record AbortGameRuntimeCommand(
    GameIntentCommandId CommandId,
    string ReasonCode,
    DateTimeOffset AbortedAt);

public sealed record PostGameRuntimePublicMessageCommand(
    Guid MessageId,
    string ParticipantId,
    ParticipantMessageAuthorKind AuthorKind,
    string Text,
    DateTimeOffset CreatedAt);

public sealed record SendGameRuntimeDirectMessageCommand(
    Guid MessageId,
    string ParticipantId,
    ParticipantMessageAuthorKind AuthorKind,
    IReadOnlyList<string> RecipientParticipantIds,
    string Text,
    DateTimeOffset CreatedAt);

public sealed record RecordGameRuntimeAgentPromptCommand(
    string EnvelopeId,
    string ParticipantId,
    DateTimeOffset CreatedAt,
    long EngineCursorSequence,
    IReadOnlyList<string> DeliveredPrivateEventIds,
    long CommunicationCursorSequence,
    int MemoryRevision,
    string? ProviderAlias,
    string? Model,
    int? PromptTokens,
    int? ResponseTokens,
    string PromptContentHash,
    string ResponseContentHash,
    string? PromptText = null,
    string? ResponseText = null,
    int MaxPromptEnvelopesPerAgent = 10);

public sealed record RecordGameRuntimeAgentMemorySummaryCommand(
    string EnvelopeId,
    string ParticipantId,
    DateTimeOffset CreatedAt,
    string? Summary,
    string? SummaryContentHash,
    MemorySummaryDecision Decision,
    string? ProviderAlias,
    string? Model,
    int? PromptTokens,
    int? ResponseTokens,
    string PromptContentHash,
    string ResponseContentHash,
    string PromptText,
    string ResponseText,
    int MaxPromptEnvelopesPerAgent = 10);
