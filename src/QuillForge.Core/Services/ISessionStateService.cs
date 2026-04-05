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

    Task<SessionMutationResult<WriterPendingCaptureEvent>> CaptureWriterPendingAsync(
        Guid? sessionId,
        CaptureWriterPendingCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<WriterPendingContentAcceptedEvent>> AcceptWriterPendingAsync(
        Guid? sessionId,
        CancellationToken ct = default);

    Task<SessionMutationResult<WriterPendingContentRejectedEvent>> RejectWriterPendingAsync(
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
