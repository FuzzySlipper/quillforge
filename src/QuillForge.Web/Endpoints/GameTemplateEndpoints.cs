using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Contracts;

namespace QuillForge.Web.Endpoints;

public static class GameTemplateEndpoints
{
    public static void MapGameTemplateEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/game-templates");

        group.MapGet("/", async (IGameTemplateService templateService, CancellationToken ct) =>
        {
            var templates = await templateService.ListAsync(ct);
            return Results.Ok(new GameTemplateListResponse
            {
                Templates = templates,
            });
        });

        group.MapGet("/{templateId}", async (
            string templateId,
            IGameTemplateService templateService,
            CancellationToken ct) =>
        {
            try
            {
                var envelope = await templateService.LoadAsync(templateId, ct);
                return Results.Ok(ToResponse(envelope));
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { Error = $"Game template {templateId} not found" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        group.MapPut("/{templateId}", async (
            string templateId,
            SaveGameTemplateRequest request,
            IGameTemplateService templateService,
            CancellationToken ct) =>
        {
            try
            {
                var envelope = await templateService.SaveAsync(templateId, request.Template, ct);
                return envelope.Validation.IsValid
                    ? Results.Ok(ToResponse(envelope))
                    : Results.BadRequest(ToResponse(envelope));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        group.MapPost("/{templateId}/clone", async (
            string templateId,
            CloneGameTemplateRequest request,
            IGameTemplateService templateService,
            CancellationToken ct) =>
        {
            try
            {
                var envelope = await templateService.CloneAsync(templateId, request.TargetTemplateId, request.DisplayName, ct);
                return envelope.Validation.IsValid
                    ? Results.Ok(ToResponse(envelope))
                    : Results.BadRequest(ToResponse(envelope));
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { Error = $"Game template {templateId} not found" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { Error = ex.Message });
            }
        });

        group.MapPost("/validate", async (
            ValidateGameTemplateRequest request,
            IGameTemplateService templateService,
            CancellationToken ct) =>
        {
            var validation = await templateService.ValidateAsync(request.Template, ct);
            return Results.Ok(new ValidateGameTemplateResponse
            {
                Validation = validation,
            });
        });

        group.MapDelete("/{templateId}", async (
            string templateId,
            IGameTemplateService templateService,
            CancellationToken ct) =>
        {
            try
            {
                await templateService.DeleteAsync(templateId, ct);
                return Results.Ok(new DeleteGameTemplateResponse
                {
                    TemplateId = templateId,
                });
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { Error = $"Game template {templateId} not found" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });
    }

    private static GameTemplateResponse ToResponse(GameTemplateValidationEnvelope envelope) =>
        new()
        {
            Template = envelope.Template,
            Validation = envelope.Validation,
        };
}
