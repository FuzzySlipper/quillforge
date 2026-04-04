using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface ISessionStateService
{
    Task<SessionState> LoadViewAsync(Guid? sessionId, CancellationToken ct = default);

    Task<SessionMutationResult<SessionState>> SetProfileAsync(
        Guid? sessionId,
        SetSessionProfileCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<SessionState>> SetRoleplayAsync(
        Guid? sessionId,
        SetSessionRoleplayCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<SessionState>> SetModeAsync(
        Guid? sessionId,
        SetSessionModeCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<SessionState>> CaptureWriterPendingAsync(
        Guid? sessionId,
        CaptureWriterPendingCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<WriterPendingDecisionResult>> AcceptWriterPendingAsync(
        Guid? sessionId,
        CancellationToken ct = default);

    Task<SessionMutationResult<SessionState>> RejectWriterPendingAsync(
        Guid? sessionId,
        CancellationToken ct = default);

    Task<SessionMutationResult<SessionState>> UpdateNarrativeStateAsync(
        Guid? sessionId,
        UpdateNarrativeStateCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<SessionState>> SetActivePlotAsync(
        Guid? sessionId,
        SetActivePlotCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<SessionState>> ClearActivePlotAsync(
        Guid? sessionId,
        CancellationToken ct = default);
}

public interface ISessionRuntimeService : ISessionStateService
{
}
