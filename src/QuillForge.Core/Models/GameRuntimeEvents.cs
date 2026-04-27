using Den.RulesEngine;

namespace QuillForge.Core.Models;

public interface IGameRuntimeEvent
{
    string EventName { get; }

    DateTimeOffset OccurredAt { get; }
}

public sealed record GameRuntimeStartedEvent(
    string GameInstanceId,
    string? TemplateId,
    string ModuleId,
    string ModuleVersion,
    GameRuntimeStatus Status,
    DateTimeOffset OccurredAt) : IGameRuntimeEvent
{
    public string EventName => nameof(GameRuntimeStartedEvent);
}

public sealed record GameRuntimeEngineCommandAppliedEvent(
    string GameInstanceId,
    GameIntentCommandId CommandId,
    string CommandType,
    GameRuntimeStatus Status,
    IReadOnlyList<IGameEvent> EngineEvents,
    DateTimeOffset OccurredAt) : IGameRuntimeEvent
{
    public string EventName => nameof(GameRuntimeEngineCommandAppliedEvent);
}

public sealed record GameRuntimeResumedEvent(
    string GameInstanceId,
    GameRuntimeStatus Status,
    DateTimeOffset OccurredAt) : IGameRuntimeEvent
{
    public string EventName => nameof(GameRuntimeResumedEvent);
}

public sealed record GameRuntimeAbortedEvent(
    string GameInstanceId,
    string ReasonCode,
    GameRuntimeStatus Status,
    DateTimeOffset OccurredAt) : IGameRuntimeEvent
{
    public string EventName => nameof(GameRuntimeAbortedEvent);
}

public sealed record GameRuntimeForkedEvent(
    Guid SourceSessionId,
    Guid TargetSessionId,
    string GameInstanceId,
    DateTimeOffset OccurredAt) : IGameRuntimeEvent
{
    public string EventName => nameof(GameRuntimeForkedEvent);
}

public sealed record GameRuntimeMutationResult(
    GameRuntimeState Game,
    IReadOnlyList<IGameRuntimeEvent> RuntimeEvents,
    IReadOnlyList<IGameEvent> EngineEvents);

public sealed record GameRuntimeCommunicationMutationResult(
    GameRuntimeState Game,
    IReadOnlyList<IParticipantCommunicationEvent> CommunicationEvents);
