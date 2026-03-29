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
        app.MapGet("/api/mode", async (
            ISessionRuntimeStore runtimeStore,
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var sessionId = TryGetSessionId(httpContext);
            var state = await runtimeStore.LoadAsync(sessionId, ct);
            return Results.Ok(new
            {
                Mode = state.Mode.ActiveModeName,
                Project = state.Mode.ProjectName,
                File = state.Mode.CurrentFile,
                Character = state.Mode.Character,
                PendingContent = state.Writer.PendingContent,
            });
        });

        app.MapPost("/api/mode", async (
            HttpContext httpContext,
            OrchestratorAgent orchestrator,
            ISessionRuntimeStore runtimeStore,
            RuntimeStateStore legacyStore,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var mode = root.GetProperty("mode").GetString() ?? "general";
            var project = root.TryGetProperty("project", out var p) ? p.GetString() : null;
            var file = root.TryGetProperty("file", out var f) ? f.GetString() : null;
            var character = root.TryGetProperty("character", out var c) ? c.GetString() : null;
            var sessionId = root.TryGetProperty("sessionId", out var sid) ? Guid.Parse(sid.GetString()!) : (Guid?)null;

            var state = await runtimeStore.LoadAsync(sessionId, ct);
            orchestrator.SetMode(state, mode, project, file, character);
            await runtimeStore.SaveAsync(state, ct);

            // Also persist to legacy store for backward compat during transition
            await legacyStore.SaveAsync(new RuntimeState
            {
                LastMode = mode,
                LastProject = project,
                LastFile = file,
                LastCharacter = character,
            }, ct);

            return Results.Ok(new { Mode = mode, Project = project, File = file, Character = character });
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

    /// <summary>
    /// Tries to extract a session ID from query string or header.
    /// Returns null if not provided (uses default/global state).
    /// </summary>
    private static Guid? TryGetSessionId(HttpContext httpContext)
    {
        if (httpContext.Request.Query.TryGetValue("sessionId", out var sid)
            && Guid.TryParse(sid, out var parsed))
        {
            return parsed;
        }
        return null;
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
