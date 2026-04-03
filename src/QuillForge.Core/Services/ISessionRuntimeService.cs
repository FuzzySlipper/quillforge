using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface ISessionRuntimeService
{
    Task<SessionRuntimeState> LoadViewAsync(Guid? sessionId, CancellationToken ct = default);

    Task<SessionMutationResult<SessionRuntimeState>> SetProfileAsync(
        Guid? sessionId,
        SetSessionProfileCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<SessionRuntimeState>> SetModeAsync(
        Guid? sessionId,
        SetSessionModeCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<SessionRuntimeState>> CaptureWriterPendingAsync(
        Guid? sessionId,
        CaptureWriterPendingCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<WriterPendingDecisionResult>> AcceptWriterPendingAsync(
        Guid? sessionId,
        CancellationToken ct = default);

    Task<SessionMutationResult<SessionRuntimeState>> RejectWriterPendingAsync(
        Guid? sessionId,
        CancellationToken ct = default);

    Task<SessionMutationResult<SessionRuntimeState>> UpdateNarrativeStateAsync(
        Guid? sessionId,
        UpdateNarrativeStateCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<SessionRuntimeState>> SetActivePlotAsync(
        Guid? sessionId,
        SetActivePlotCommand command,
        CancellationToken ct = default);

    Task<SessionMutationResult<SessionRuntimeState>> ClearActivePlotAsync(
        Guid? sessionId,
        CancellationToken ct = default);
}
