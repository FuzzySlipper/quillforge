using System.Text.Json;
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

        group.MapPost("/projects/{name}/run", async (
            string name,
            HttpContext httpContext,
            ForgePipeline pipeline,
            ILogger<ForgePipeline> logger,
            CancellationToken ct) =>
        {
            // SSE streaming for pipeline events
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";

            logger.LogInformation("Forge pipeline run requested for project {Name}", name);

            // The actual ForgeContext creation requires runtime config assembly.
            // This endpoint serves as the SSE streaming scaffold.
            // Full wiring happens during the configuration task.

            await httpContext.Response.WriteAsync(
                $"data: {JsonSerializer.Serialize(new { Type = "info", Message = $"Pipeline endpoint ready for project {name}" }, s_jsonOptions)}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
        });

        group.MapPost("/projects/{name}/pause", (string name, ForgePipeline pipeline) =>
        {
            pipeline.RequestPause();
            return Results.Ok(new { Paused = name });
        });

        group.MapPost("/projects/{name}/rebuild-manifest", async (
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
}
