using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Contracts;

namespace QuillForge.Web.Endpoints;

public static class LoreCanonizationEndpoints
{
    public static void MapLoreCanonizationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/lore/canonize");

        group.MapPost("/preview", async (
            LoreCanonizationPreviewRequest request,
            ISessionLoreCanonizationService canonizationService,
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

            var result = await canonizationService.GenerateProposalAsync(
                request.SessionId,
                new GenerateLoreCanonizationProposalCommand(request.TargetFilePath),
                ct);

            return ToHttpResult(
                result,
                success => Results.Ok(new LoreCanonizationPreviewResponse
                {
                    SessionId = success.SessionId,
                    Status = "preview_ready",
                    Proposal = ToProposalDto(success.Proposal),
                }));
        });

        group.MapPost("/apply", async (
            LoreCanonizationMutationRequest request,
            ISessionLoreCanonizationService canonizationService,
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

            var result = await canonizationService.ApplyProposalAsync(request.SessionId, ct);
            return ToHttpResult(
                result,
                success => Results.Ok(new LoreCanonizationApplyResponse
                {
                    SessionId = success.SessionId,
                    Status = "applied",
                    LoreSet = success.LoreSet,
                    TargetFilePath = success.TargetFilePath,
                    ContentLength = success.SavedContent.Length,
                }));
        });

        group.MapPost("/discard", async (
            LoreCanonizationMutationRequest request,
            ISessionLoreCanonizationService canonizationService,
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

            var result = await canonizationService.DiscardProposalAsync(request.SessionId, ct);
            return ToHttpResult(
                result,
                success => Results.Ok(new LoreCanonizationDiscardResponse
                {
                    SessionId = success.SessionId,
                    Status = "discarded",
                    TargetFilePath = success.TargetFilePath,
                }));
        });
    }

    private static IResult ToHttpResult<T>(
        SessionMutationResult<T> result,
        Func<T, IResult> onSuccess)
    {
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

        return onSuccess(result.Value!);
    }

    private static LoreCanonizationProposalDto ToProposalDto(LoreCanonizationProposalState proposal)
    {
        return new LoreCanonizationProposalDto
        {
            SessionId = proposal.SessionId,
            LoreSet = proposal.LoreSet,
            TargetFilePath = proposal.TargetFilePath,
            Summary = proposal.Summary,
            NewFacts = proposal.NewFacts,
            ModifiedFacts = proposal.ModifiedFacts,
            Conflicts = proposal.Conflicts,
            ProposedMarkdown = proposal.ProposedMarkdown,
            ProposedFileContent = proposal.ProposedFileContent,
            CanApply = proposal.CanApply,
            GeneratedAt = proposal.GeneratedAt,
        };
    }
}
