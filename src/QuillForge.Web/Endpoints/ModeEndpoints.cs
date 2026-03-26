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
                Mode = orchestrator.ActiveModeName,
                Project = orchestrator.ProjectName,
                File = orchestrator.CurrentFile,
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

            return Results.Ok(new { Mode = mode, Project = project, File = file });
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
                Personas = personas,
                LoreSets = loreSets,
                WritingStyles = styles,
                ActivePersona = config.Persona.Active,
                ActiveLore = config.Lore.Active,
                ActiveWritingStyle = config.WritingStyle.Active,
            });
        });

        app.MapGet("/api/agents/models", (AppConfig config) =>
        {
            return Results.Ok(new
            {
                Assignments = new
                {
                    Orchestrator = config.Models.Orchestrator,
                    ProseWriter = config.Models.ProseWriter,
                    Librarian = config.Models.Librarian,
                }
            });
        });
    }
}
