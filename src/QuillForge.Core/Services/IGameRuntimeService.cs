using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface IGameRuntimeService
{
    Task<GameRuntimeState?> LoadViewAsync(Guid sessionId, CancellationToken ct = default);

    Task<SessionMutationResult<GameRuntimeMutationResult>> StartAsync(
        Guid sessionId,
        StartGameRuntimeCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<GameRuntimeMutationResult>> ApplyEngineCommandAsync(
        Guid sessionId,
        ApplyGameRuntimeEngineCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<GameRuntimeMutationResult>> ResumeAsync(
        Guid sessionId,
        ResumeGameRuntimeCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<GameRuntimeMutationResult>> AbortAsync(
        Guid sessionId,
        AbortGameRuntimeCommand command,
        CancellationToken ct = default);
}
