using System.Text.Json;
using QuillForge.Core.Agents;
using QuillForge.Core.Services;

namespace QuillForge.Web.Endpoints;

public static class ModeEndpoints
{
    public static void MapModeEndpoints(this WebApplication app)
    {
        app.MapGet("/api/mode", (OrchestratorAgent orchestrator) =>
        {
            return Results.Ok(new
            {
                mode = orchestrator.ActiveModeName,
                project = orchestrator.ProjectName,
                file = orchestrator.CurrentFile,
            });
        });

        app.MapPost("/api/mode", async (HttpContext httpContext, OrchestratorAgent orchestrator) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body);
            var root = body.RootElement;

            var mode = root.GetProperty("mode").GetString() ?? "general";
            var project = root.TryGetProperty("project", out var p) ? p.GetString() : null;
            var file = root.TryGetProperty("file", out var f) ? f.GetString() : null;

            orchestrator.SetMode(mode, project, file);

            return Results.Ok(new { mode, project, file });
        });

        app.MapGet("/api/profiles", async (
            IPersonaStore personaStore,
            ILoreStore loreStore,
            IWritingStyleStore styleStore,
            CancellationToken ct) =>
        {
            var personas = await personaStore.ListAsync(ct);
            var loreSets = await loreStore.ListLoreSetsAsync(ct);
            var styles = await styleStore.ListAsync(ct);

            return Results.Ok(new { personas, loreSets, styles });
        });
    }
}
