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
    DateTimeOffset StartedAt);

public sealed record ApplyGameRuntimeEngineCommand(
    IGameIntentCommand EngineCommand,
    DateTimeOffset OccurredAt);

public sealed record ResumeGameRuntimeCommand(
    DateTimeOffset ResumedAt);

public sealed record AbortGameRuntimeCommand(
    GameIntentCommandId CommandId,
    string ReasonCode,
    DateTimeOffset AbortedAt);
