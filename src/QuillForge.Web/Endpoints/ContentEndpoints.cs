using System.Text.Json;
using QuillForge.Core;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.Utilities;

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

        app.MapGet("/api/lore", async (ILoreStore loreStore, AppConfig config, CancellationToken ct) =>
        {
            var lore = await loreStore.LoadLoreSetAsync(config.Lore.Active, ct);
            var files = lore.Select(kvp => new
            {
                Path = kvp.Key,
                Size = kvp.Value.Length,
                Tokens = kvp.Value.Length / 4, // rough estimate
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
                ActiveProject = config.Lore.Active,
                LorePath = $"lore/{config.Lore.Active}",
            });
        });

        app.MapGet("/api/lore/projects", async (ILoreStore loreStore, AppConfig config, CancellationToken ct) =>
        {
            var projects = await loreStore.ListLoreSetsAsync(ct);
            return Results.Ok(new
            {
                Projects = projects,
                Active = config.Lore.Active,
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

        // Individual lore file CRUD (catch-all must come AFTER /api/lore/projects)
        app.MapGet("/api/lore/{**filePath}", async (string filePath, AppConfig config, CancellationToken ct) =>
        {
            var resolved = Path.GetFullPath(Path.Combine(contentRoot, ContentPaths.Lore, config.Lore.Active, filePath));
            var loreDir = Path.Combine(contentRoot, ContentPaths.Lore, config.Lore.Active);
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
            AtomicFileWriter writer,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var content = body.RootElement.TryGetProperty("content", out var el) ? el.GetString() ?? "" : "";

            var resolved = Path.GetFullPath(Path.Combine(contentRoot, ContentPaths.Lore, config.Lore.Active, filePath));
            var loreDir = Path.Combine(contentRoot, ContentPaths.Lore, config.Lore.Active);
            if (!resolved.StartsWith(loreDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return Results.BadRequest(new { Error = "Invalid path" });
            }

            var dir = Path.GetDirectoryName(resolved);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            await writer.WriteAsync(resolved, content, ct);
            return Results.Ok(new { Path = filePath, Status = "ok" });
        });

        app.MapDelete("/api/lore/{**filePath}", (string filePath, AppConfig config) =>
        {
            var resolved = Path.GetFullPath(Path.Combine(contentRoot, ContentPaths.Lore, config.Lore.Active, filePath));
            var loreDir = Path.Combine(contentRoot, ContentPaths.Lore, config.Lore.Active);
            if (!resolved.StartsWith(loreDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolved))
            {
                return Results.NotFound(new { Error = "File not found" });
            }
            File.Delete(resolved);
            return Results.Ok(new { Deleted = filePath });
        });

        // Serve content files (backgrounds, generated images, etc.) as static files
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
}
