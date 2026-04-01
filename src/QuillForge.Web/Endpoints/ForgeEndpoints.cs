using System.Text.Json;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Pipeline;
using QuillForge.Core.Services;

namespace QuillForge.Web.Endpoints;

public static class ForgeEndpoints
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void MapForgeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/forge");

        // /api/forge/projects used by commands.ts for listing
        group.MapGet("/projects", async (IContentFileService fileService, CancellationToken ct) =>
        {
            var files = await fileService.ListAsync("forge", "manifest.json", ct);
            var projects = new List<object>();

            foreach (var file in files)
            {
                try
                {
                    var json = await fileService.ReadAsync(file, ct);
                    var manifest = JsonSerializer.Deserialize<ForgeManifest>(json,
                        new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
                    if (manifest is not null)
                    {
                        projects.Add(new
                        {
                            ProjectName = manifest.ProjectName,
                            Stage = manifest.Stage.ToString(),
                            ChapterCount = manifest.ChapterCount,
                            Paused = manifest.Paused,
                        });
                    }
                }
                catch { /* skip malformed manifests */ }
            }

            return Results.Ok(projects);
        });

        // POST /api/forge/projects/{name}/start — run full pipeline with SSE streaming
        group.MapPost("/{name}/start", async (
            string name,
            HttpContext httpContext,
            ForgePipeline pipeline,
            ForgePlannerAgent planner,
            ForgeWriterAgent writer,
            ForgeReviewerAgent reviewer,
            IContentFileService fileService,
            IEnumerable<IToolHandler> toolHandlers,
            IWritingStyleStore writingStyleStore,
            ILoreStore loreStore,
            AppConfig config,
            ILogger<ForgePipeline> logger,
            CancellationToken ct) =>
        {
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";

            var context = await BuildForgeContextAsync(name, pipeline, planner, writer, reviewer,
                fileService, toolHandlers, writingStyleStore, loreStore, config, logger, ct);

            // Unpause if resuming
            context.Manifest = context.Manifest with { Paused = false };

            await foreach (var evt in pipeline.RunAsync(context, ct))
            {
                var sseData = MapForgeEventToSse(evt);
                await httpContext.Response.WriteAsync($"data: {sseData}\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
            }
        });

        // POST /api/forge/projects/{name}/design — run only planning stage
        group.MapPost("/{name}/design", async (
            string name,
            HttpContext httpContext,
            ForgePipeline pipeline,
            ForgePlannerAgent planner,
            ForgeWriterAgent writer,
            ForgeReviewerAgent reviewer,
            IContentFileService fileService,
            IEnumerable<IToolHandler> toolHandlers,
            IWritingStyleStore writingStyleStore,
            ILoreStore loreStore,
            AppConfig config,
            ILogger<ForgePipeline> logger,
            CancellationToken ct) =>
        {
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";

            var context = await BuildForgeContextAsync(name, pipeline, planner, writer, reviewer,
                fileService, toolHandlers, writingStyleStore, loreStore, config, logger, ct);

            // Design runs the pipeline but requests pause after planning completes
            pipeline.RequestPause();
            context.Manifest = context.Manifest with
            {
                Stage = ForgeStage.Planning,
                Paused = false,
            };

            await foreach (var evt in pipeline.RunAsync(context, ct))
            {
                var sseData = MapForgeEventToSse(evt);
                await httpContext.Response.WriteAsync($"data: {sseData}\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
            }

            // Send complete event
            var done = JsonSerializer.Serialize(new { Type = "complete", Message = "Design phase complete." }, s_jsonOptions);
            await httpContext.Response.WriteAsync($"data: {done}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
        });

        // GET /api/forge/projects/{name}/status — current pipeline state
        group.MapGet("/{name}/status", async (
            string name,
            IContentFileService fileService,
            CancellationToken ct) =>
        {
            try
            {
                var json = await fileService.ReadAsync($"forge/{name}/manifest.json", ct);
                var manifest = JsonSerializer.Deserialize<ForgeManifest>(json,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                if (manifest is null)
                    return Results.NotFound(new { Error = "Manifest not found" });

                return Results.Ok(new
                {
                    manifest.ProjectName,
                    Stage = manifest.Stage.ToString(),
                    manifest.ChapterCount,
                    manifest.Paused,
                    Chapters = manifest.Chapters.ToDictionary(
                        kvp => kvp.Key,
                        kvp => new
                        {
                            State = kvp.Value.State.ToString(),
                            kvp.Value.RevisionCount,
                            kvp.Value.WordCount,
                        }),
                    manifest.Stats,
                });
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { Error = $"Project '{name}' not found" });
            }
        });

        // POST /api/forge/projects/{name}/approve — resume from pause
        group.MapPost("/{name}/approve", async (
            string name,
            HttpContext httpContext,
            ForgePipeline pipeline,
            ForgePlannerAgent planner,
            ForgeWriterAgent writer,
            ForgeReviewerAgent reviewer,
            IContentFileService fileService,
            IEnumerable<IToolHandler> toolHandlers,
            IWritingStyleStore writingStyleStore,
            ILoreStore loreStore,
            AppConfig config,
            ILogger<ForgePipeline> logger,
            CancellationToken ct) =>
        {
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";

            var context = await BuildForgeContextAsync(name, pipeline, planner, writer, reviewer,
                fileService, toolHandlers, writingStyleStore, loreStore, config, logger, ct);

            if (!context.Manifest.Paused)
            {
                var err = JsonSerializer.Serialize(new { Type = "error", Message = "Pipeline is not paused" }, s_jsonOptions);
                await httpContext.Response.WriteAsync($"data: {err}\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
                return;
            }

            context.Manifest = context.Manifest with { Paused = false };

            await foreach (var evt in pipeline.RunAsync(context, ct))
            {
                var sseData = MapForgeEventToSse(evt);
                await httpContext.Response.WriteAsync($"data: {sseData}\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
            }
        });

        group.MapPost("/{name}/pause", (string name, ForgePipeline pipeline) =>
        {
            pipeline.RequestPause();
            return Results.Ok(new { Paused = name });
        });

        group.MapPost("/{name}/rebuild-manifest", async (
            string name,
            ForgePipeline pipeline,
            CancellationToken ct) =>
        {
            var manifest = await pipeline.RebuildManifestAsync(name, ct);
            return Results.Ok(new
            {
                manifest.ProjectName,
                Stage = manifest.Stage.ToString(),
                manifest.ChapterCount,
                Chapters = manifest.Chapters.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.State.ToString()),
            });
        });
    }

    /// <summary>
    /// Tool names the forge pipeline agents need.
    /// Planner uses write_file/read_file/list_files; writer uses query_lore.
    /// </summary>
    private static readonly HashSet<string> ForgeToolNames =
        ["write_file", "read_file", "list_files", "query_lore"];

    /// <summary>
    /// Build a ForgeContext from an existing manifest or create a fresh one.
    /// </summary>
    private static async Task<ForgeContext> BuildForgeContextAsync(
        string projectName,
        ForgePipeline pipeline,
        ForgePlannerAgent planner,
        ForgeWriterAgent writer,
        ForgeReviewerAgent reviewer,
        IContentFileService fileService,
        IEnumerable<IToolHandler> toolHandlers,
        IWritingStyleStore writingStyleStore,
        ILoreStore loreStore,
        AppConfig config,
        ILogger logger,
        CancellationToken ct)
    {
        ForgeManifest manifest;
        var manifestPath = $"forge/{projectName}/manifest.json";

        try
        {
            var json = await fileService.ReadAsync(manifestPath, ct);
            manifest = JsonSerializer.Deserialize<ForgeManifest>(json,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                ?? CreateNewManifest(projectName, config);
        }
        catch (FileNotFoundException)
        {
            manifest = CreateNewManifest(projectName, config);
            var json = JsonSerializer.Serialize(manifest,
                new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await fileService.WriteAsync(manifestPath, json, ct);
        }

        // Wire up forge-relevant tools from the DI-registered handlers
        var forgeTools = toolHandlers
            .Where(t => ForgeToolNames.Contains(t.Name))
            .ToList();

        // Load writing style content
        var writingStyle = "";
        try
        {
            writingStyle = await writingStyleStore.LoadAsync(config.WritingStyle.Active, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not load writing style '{Style}' for forge run", config.WritingStyle.Active);
        }

        // Load lore context summary for the planner
        var loreContext = "";
        var activeLoreSet = config.Lore.Active;
        if (!string.IsNullOrEmpty(activeLoreSet))
        {
            try
            {
                var loreFiles = await loreStore.LoadLoreSetAsync(activeLoreSet, ct);
                loreContext = string.Join("\n\n---\n\n",
                    loreFiles.Select(kvp => $"### {kvp.Key}\n\n{kvp.Value}"));
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not load lore set '{LoreSet}' for forge run", activeLoreSet);
            }
        }

        return new ForgeContext
        {
            Manifest = manifest,
            ProjectPath = $"forge/{projectName}",
            Planner = planner,
            Writer = writer,
            Reviewer = reviewer,
            WriterTools = forgeTools,
            FileService = fileService,
            AgentContext = new AgentContext
            {
                SessionId = Guid.CreateVersion7(),
                ActiveMode = "forge",
                ActiveLoreSet = activeLoreSet,
                ActiveWritingStyle = config.WritingStyle.Active,
                RunLorePath = $"forge/{projectName}/run-lore.md",
            },
            WritingStyle = writingStyle,
            LoreContext = loreContext,
            RunLorePath = $"forge/{projectName}/run-lore.md",
            ReviewPassThreshold = config.Forge.ReviewPassThreshold,
            MaxRevisions = config.Forge.MaxRevisions,
        };
    }

    private static ForgeManifest CreateNewManifest(string projectName, AppConfig config)
    {
        return new ForgeManifest
        {
            ProjectName = projectName,
            Stage = ForgeStage.Planning,
            PauseAfterChapter1 = config.Forge.PauseAfterChapter1,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Maps ForgeEvent objects to SSE JSON payloads with event types the frontend expects.
    /// </summary>
    private static string MapForgeEventToSse(ForgeEvent evt)
    {
        return evt switch
        {
            StageStartedEvent stage => JsonSerializer.Serialize(
                new { Type = "stage", Message = $"{stage.StageName} started" }, s_jsonOptions),

            StageCompletedEvent stage => JsonSerializer.Serialize(
                new { Type = "stage", Message = $"{stage.StageName} complete" }, s_jsonOptions),

            ChapterProgressEvent ch => JsonSerializer.Serialize(
                new { Type = "chapter", Chapter = ch.ChapterId, Status = ch.Status, WordCount = 0, Detail = ch.Detail }, s_jsonOptions),

            ForgeErrorEvent err => JsonSerializer.Serialize(
                new { Type = "error", Message = err.Message, Stage = err.StageName }, s_jsonOptions),

            ForgeCompletedEvent done => JsonSerializer.Serialize(
                new
                {
                    Type = "complete",
                    Message = "Pipeline complete",
                    ChaptersComplete = done.Stats.AgentCalls,
                    TotalTokens = done.Stats.TotalInputTokens + done.Stats.TotalOutputTokens,
                }, s_jsonOptions),

            _ => JsonSerializer.Serialize(new { Type = "ping" }, s_jsonOptions),
        };
    }

    /// <summary>
    /// Additional forge/project management endpoints not tied to pipeline execution.
    /// </summary>
    public static void MapForgeManagementEndpoints(this WebApplication app)
    {
        app.MapGet("/api/forge/prompts", async (IContentFileService fileService, CancellationToken ct) =>
        {
            var files = await fileService.ListAsync("forge-prompts", "*.md", ct);
            var prompts = new List<object>();
            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file.Split('/').Last());
                try
                {
                    var content = await fileService.ReadAsync(file, ct);
                    prompts.Add(new { Name = name, Content = content });
                }
                catch { }
            }
            return Results.Ok(new { Prompts = prompts });
        });

        app.MapPost("/api/forge/create", async (
            HttpContext httpContext,
            IContentFileService fileService,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var name = body.RootElement.TryGetProperty("name", out var el) ? el.GetString() ?? "untitled" : "untitled";

            await fileService.WriteAsync($"forge/{name}/plan/.gitkeep", "", ct);
            await fileService.WriteAsync($"forge/{name}/drafts/.gitkeep", "", ct);
            await fileService.WriteAsync($"forge/{name}/output/.gitkeep", "", ct);

            return Results.Ok(new { Status = "ok", Name = name, Created = true });
        });

        app.MapGet("/api/projects", async (IStoryStore store, CancellationToken ct) =>
        {
            var projects = await store.ListProjectsAsync(ct);
            return Results.Ok(new { Projects = projects });
        });
    }
}
