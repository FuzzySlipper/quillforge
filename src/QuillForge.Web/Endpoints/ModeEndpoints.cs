using System.Text.Json;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.FileSystem;

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

        app.MapPost("/api/mode", async (
            HttpContext httpContext,
            OrchestratorAgent orchestrator,
            RuntimeStateStore stateStore,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var mode = root.GetProperty("mode").GetString() ?? "general";
            var project = root.TryGetProperty("project", out var p) ? p.GetString() : null;
            var file = root.TryGetProperty("file", out var f) ? f.GetString() : null;

            orchestrator.SetMode(mode, project, file);

            // Persist so it survives refresh/restart
            await stateStore.SaveAsync(new RuntimeState
            {
                LastMode = mode,
                LastProject = project,
                LastFile = file,
            }, ct);

            return Results.Ok(new { mode, project, file });
        });

        app.MapGet("/api/profiles", async (
            IPersonaStore personaStore,
            ILoreStore loreStore,
            IWritingStyleStore styleStore,
            AppConfig config,
            CancellationToken ct) =>
        {
            var personas = await personaStore.ListAsync(ct);
            var loreSets = await loreStore.ListLoreSetsAsync(ct);
            var styles = await styleStore.ListAsync(ct);

            return Results.Ok(new
            {
                personas,
                lore_sets = loreSets,
                writing_styles = styles,
                active_persona = config.Persona.Active,
                active_lore = config.Lore.Active,
                active_writing_style = config.WritingStyle.Active,
            });
        });

        app.MapGet("/api/agents/models", (AppConfig config) =>
        {
            return Results.Ok(new
            {
                assignments = new
                {
                    orchestrator = config.Models.Orchestrator,
                    prose_writer = config.Models.ProseWriter,
                    librarian = config.Models.Librarian,
                }
            });
        });
    }
}
