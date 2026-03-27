using System.Text.Json;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.Configuration;
using QuillForge.Storage.FileSystem;
using QuillForge.Storage.Utilities;

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
                    ForgeWriter = config.Models.ForgeWriter,
                    ForgePlanner = config.Models.ForgePlanner,
                    ForgeReviewer = config.Models.ForgeReviewer,
                }
            });
        });

        app.MapPut("/api/agents/models", async (
            HttpContext httpContext,
            AppConfig config,
            AtomicFileWriter writer,
            ILogger<AppConfig> logger,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            // Apply updates — keep existing values for fields not provided
            if (root.TryGetProperty("orchestrator", out var o) && o.GetString() is { } orch)
                config.Models.Orchestrator = orch;
            if (root.TryGetProperty("proseWriter", out var pw) && pw.GetString() is { } prose)
                config.Models.ProseWriter = prose;
            if (root.TryGetProperty("librarian", out var lb) && lb.GetString() is { } lib)
                config.Models.Librarian = lib;
            if (root.TryGetProperty("forgeWriter", out var fw) && fw.GetString() is { } fWriter)
                config.Models.ForgeWriter = fWriter;
            if (root.TryGetProperty("forgePlanner", out var fp) && fp.GetString() is { } fPlanner)
                config.Models.ForgePlanner = fPlanner;
            if (root.TryGetProperty("forgeReviewer", out var fr) && fr.GetString() is { } fReviewer)
                config.Models.ForgeReviewer = fReviewer;

            // Persist to config.yaml
            var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory)
                ?? FindSolutionRoot(Directory.GetCurrentDirectory());
            var contentRoot = solutionRoot is not null
                ? Path.Combine(solutionRoot, "build")
                : Path.Combine(AppContext.BaseDirectory, "build");
            var configPath = Path.Combine(contentRoot, "config.yaml");

            await writer.WriteAsync(configPath, ConfigurationLoader.Serialize(config), ct);

            logger.LogInformation(
                "Agent models updated: orchestrator={Orch}, proseWriter={Prose}, librarian={Lib}",
                config.Models.Orchestrator, config.Models.ProseWriter, config.Models.Librarian);

            return Results.Ok(new
            {
                Status = "ok",
                Assignments = new
                {
                    Orchestrator = config.Models.Orchestrator,
                    ProseWriter = config.Models.ProseWriter,
                    Librarian = config.Models.Librarian,
                    ForgeWriter = config.Models.ForgeWriter,
                    ForgePlanner = config.Models.ForgePlanner,
                    ForgeReviewer = config.Models.ForgeReviewer,
                }
            });
        });
    }

    private static string? FindSolutionRoot(string startDir)
    {
        var dir = startDir;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "QuillForge.slnx")))
                return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return null;
    }
}
