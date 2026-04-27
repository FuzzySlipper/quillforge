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
