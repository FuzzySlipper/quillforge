using System.Text.Json;
using QuillForge.Core;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.Utilities;
using QuillForge.Web.Services;

namespace QuillForge.Web.Endpoints;

public static class ContentEndpoints
{
    public static void MapContentEndpoints(this WebApplication app, string contentRoot)
    {
        app.MapGet("/api/layouts", (IContentFileService fileService, CancellationToken ct) =>
        {
            var layoutsDir = Path.Combine(contentRoot, ContentPaths.Layouts);
            if (!Directory.Exists(layoutsDir))
            {
                return Results.Ok(new { Layouts = Array.Empty<string>() });
            }

            var layouts = Directory.GetFiles(layoutsDir, "*.md")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n)
                .ToList();

            return Results.Ok(new { Layouts = layouts });
        });

        app.MapGet("/api/layouts/{name}", async (string name, IContentFileService fileService, CancellationToken ct) =>
        {
            try
            {
                var content = await fileService.ReadAsync($"layouts/{name}.md", ct);
                return Results.Ok(new { Name = name, Content = content });
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { Error = $"Layout '{name}' not found" });
            }
        });

        app.MapGet("/api/backgrounds", () =>
        {
            var bgDir = Path.Combine(contentRoot, ContentPaths.Backgrounds);
            if (!Directory.Exists(bgDir))
            {
                return Results.Ok(new { Backgrounds = Array.Empty<object>() });
            }

            var backgrounds = Directory.GetFiles(bgDir)
                .Where(f => !Path.GetFileName(f).StartsWith('.') && Path.GetFileName(f) != "ATTRIBUTION")
                .Select(f => new
                {
                    Filename = Path.GetFileName(f),
                    Url = $"/content/backgrounds/{Path.GetFileName(f)}",
                })
                .OrderBy(b => b.Filename)
                .ToList();

            return Results.Ok(new { Backgrounds = backgrounds });
        });

        app.MapGet("/api/lore", async (
            HttpContext httpContext,
            ILoreStore loreStore,
            AppConfig config,
            ISessionProfileReadService profileReadService,
            CancellationToken ct) =>
        {
            var activeLoreSet = await ResolveActiveLoreSetAsync(httpContext, config, profileReadService, ct);
            var lore = await loreStore.LoadLoreSetAsync(activeLoreSet, ct);
            var files = lore.Select(kvp => new
            {
                Path = kvp.Key,
                Size = kvp.Value.Length,
                Tokens = kvp.Value.Length / 4,
            }).ToList();

            var categories = files
                .Select(f => f.Path.Contains('/') ? f.Path.Split('/')[0] : "root")
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            return Results.Ok(new
            {
                Files = files,
                Categories = categories,
                ActiveProject = activeLoreSet,
                LorePath = $"lore/{activeLoreSet}",
            });
        });

        app.MapGet("/api/lore/projects", async (
            HttpContext httpContext,
            ILoreStore loreStore,
            AppConfig config,
            ISessionProfileReadService profileReadService,
            CancellationToken ct) =>
        {
            var projects = await loreStore.ListLoreSetsAsync(ct);
            var activeLoreSet = await ResolveActiveLoreSetAsync(httpContext, config, profileReadService, ct);
            return Results.Ok(new
            {
                Projects = projects,
                Active = activeLoreSet,
            });
        });

        app.MapPost("/api/lore/projects", async (
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var name = body.RootElement.TryGetProperty("name", out var el) ? el.GetString() ?? "" : "";

            if (string.IsNullOrWhiteSpace(name))
            {
                return Results.BadRequest(new { Error = "Project name is required" });
            }

            var projectDir = Path.Combine(contentRoot, ContentPaths.Lore, name);
            if (Directory.Exists(projectDir))
            {
                return Results.Conflict(new { Error = $"Lore project '{name}' already exists" });
            }

            Directory.CreateDirectory(projectDir);
            return Results.Ok(new { Status = "ok", Name = name });
        });

        app.MapGet("/api/lore/{**filePath}", async (
            string filePath,
            HttpContext httpContext,
            AppConfig config,
            ISessionProfileReadService profileReadService,
            CancellationToken ct) =>
        {
            var activeLoreSet = await ResolveActiveLoreSetAsync(httpContext, config, profileReadService, ct);
            var resolved = Path.GetFullPath(Path.Combine(contentRoot, ContentPaths.Lore, activeLoreSet, filePath));
            var loreDir = Path.Combine(contentRoot, ContentPaths.Lore, activeLoreSet);
            if (!resolved.StartsWith(loreDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolved))
            {
                return Results.NotFound(new { Error = "File not found" });
            }
            var content = await File.ReadAllTextAsync(resolved, ct);
            return Results.Ok(new { Path = filePath, Content = content, Tokens = content.Length / 4 });
        });

        app.MapPut("/api/lore/{**filePath}", async (
            string filePath,
            HttpContext httpContext,
            AppConfig config,
            ISessionProfileReadService profileReadService,
            AtomicFileWriter writer,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var content = body.RootElement.TryGetProperty("content", out var el) ? el.GetString() ?? "" : "";
            var activeLoreSet = await ResolveActiveLoreSetAsync(httpContext, config, profileReadService, ct);

            var resolved = Path.GetFullPath(Path.Combine(contentRoot, ContentPaths.Lore, activeLoreSet, filePath));
            var loreDir = Path.Combine(contentRoot, ContentPaths.Lore, activeLoreSet);
            if (!resolved.StartsWith(loreDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { Error = "Invalid path" });
            }

            var dir = Path.GetDirectoryName(resolved);
            if (dir is not null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            await writer.WriteAsync(resolved, content, ct);
            return Results.Ok(new { Path = filePath, Status = "ok" });
        });

        app.MapDelete("/api/lore/{**filePath}", async (
            string filePath,
            HttpContext httpContext,
            AppConfig config,
            ISessionProfileReadService profileReadService,
            CancellationToken ct) =>
        {
            var activeLoreSet = await ResolveActiveLoreSetAsync(httpContext, config, profileReadService, ct);
            var resolved = Path.GetFullPath(Path.Combine(contentRoot, ContentPaths.Lore, activeLoreSet, filePath));
            var loreDir = Path.Combine(contentRoot, ContentPaths.Lore, activeLoreSet);
            if (!resolved.StartsWith(loreDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolved))
            {
                return Results.NotFound(new { Error = "File not found" });
            }
            File.Delete(resolved);
            return Results.Ok(new { Deleted = filePath });
        });

        app.MapGet("/content/{**path}", (string path) =>
        {
            var fullPath = Path.GetFullPath(Path.Combine(contentRoot, path));
            var rootWithSep = contentRoot.EndsWith(Path.DirectorySeparatorChar)
                ? contentRoot
                : contentRoot + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            {
                return Results.NotFound();
            }
            return Results.File(fullPath);
        });
    }

    private static async Task<string> ResolveActiveLoreSetAsync(
        HttpContext httpContext,
        AppConfig config,
        ISessionProfileReadService profileReadService,
        CancellationToken ct)
    {
        var sessionId = httpContext.TryGetSessionId();
        if (!sessionId.HasValue)
        {
            return config.Lore.Active;
        }

        var view = await profileReadService.LoadAsync(sessionId, ct);
        return view.ActiveLoreSet;
    }
}
