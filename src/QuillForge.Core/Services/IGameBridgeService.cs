using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface IGameBridgeService
{
    Task<GameBridgeView> GetViewAsync(
        Guid sessionId,
        string? participantId = null,
        CancellationToken ct = default);

    Task<SessionMutationResult<GameBridgeMutationResult>> StartFromTemplateAsync(
        Guid sessionId,
        StartGameFromTemplateCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<GameBridgeMutationResult>> SubmitTypedActionAsync(
        Guid sessionId,
        SubmitGameTypedActionCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<GameBridgeMutationResult>> SubmitTextActionAsync(
        Guid sessionId,
        SubmitGameTextActionCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<GameBridgeMutationResult>> PostPublicMessageAsync(
        Guid sessionId,
        PostGameRuntimePublicMessageCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<GameBridgeMutationResult>> SendDirectMessageAsync(
        Guid sessionId,
        SendGameRuntimeDirectMessageCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<GameBridgeMutationResult>> EndAsync(
        Guid sessionId,
        EndGameBridgeCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<GameBridgeMutationResult>> AbortAsync(
        Guid sessionId,
        AbortGameRuntimeCommand command,
        CancellationToken ct = default);
}
