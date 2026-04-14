using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Contracts;

namespace QuillForge.Web.Endpoints;

public static class WriterEndpoints
{
    public static void MapWriterEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/writer/pending");

        group.MapPost("/accept", async (
            WriterPendingMutationRequest request,
            ISessionStateService runtimeService,
            CancellationToken ct) =>
        {
            if (request.SessionId == Guid.Empty)
            {
                return Results.BadRequest(new
                {
                    error = "invalid_session_mutation",
                    message = "sessionId is required.",
                });
            }

            var result = await runtimeService.AcceptWriterPendingAsync(request.SessionId, ct);
            if (result.Status == SessionMutationStatus.Busy)
            {
                return Results.Conflict(new
                {
                    error = "session_busy",
                    message = result.Error,
                });
            }

            if (result.Status == SessionMutationStatus.Invalid)
            {
                return Results.BadRequest(new
                {
                    error = "invalid_session_mutation",
                    message = result.Error,
                });
            }

            var accepted = result.Value!;
            return Results.Ok(new WriterPendingAcceptResponse
            {
                SessionId = accepted.SessionId ?? request.SessionId,
                Status = "accepted",
                SavedPath = accepted.SavedPath,
                ContentLength = accepted.AcceptedContent.Length,
            });
        });

        group.MapPost("/reject", async (
            WriterPendingMutationRequest request,
            ISessionStateService runtimeService,
            CancellationToken ct) =>
        {
            if (request.SessionId == Guid.Empty)
            {
                return Results.BadRequest(new
                {
                    error = "invalid_session_mutation",
                    message = "sessionId is required.",
                });
            }

            var result = await runtimeService.RejectWriterPendingAsync(request.SessionId, ct);
            if (result.Status == SessionMutationStatus.Busy)
            {
                return Results.Conflict(new
                {
                    error = "session_busy",
                    message = result.Error,
                });
            }

            if (result.Status == SessionMutationStatus.Invalid)
            {
                return Results.BadRequest(new
                {
                    error = "invalid_session_mutation",
                    message = result.Error,
                });
            }

            return Results.Ok(new WriterPendingRejectResponse
            {
                SessionId = result.Value!.SessionView.SessionId ?? request.SessionId,
                Status = "rejected",
            });
        });
    }
}
