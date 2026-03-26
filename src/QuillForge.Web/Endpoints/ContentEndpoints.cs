using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Web.Endpoints;

public static class ContentEndpoints
{
    public static void MapContentEndpoints(this WebApplication app, string contentRoot)
    {
        app.MapGet("/api/layouts", (IContentFileService fileService, CancellationToken ct) =>
        {
            var layoutsDir = Path.Combine(contentRoot, "layouts");
            if (!Directory.Exists(layoutsDir))
            {
                return Results.Ok(new { layouts = Array.Empty<string>() });
            }

            var layouts = Directory.GetFiles(layoutsDir, "*.md")
                .Select(Path.GetFileNameWithoutExtension)
                .OrderBy(n => n)
                .ToList();

            return Results.Ok(new { layouts });
        });

        app.MapGet("/api/layouts/{name}", async (string name, IContentFileService fileService, CancellationToken ct) =>
        {
            try
            {
                var content = await fileService.ReadAsync($"layouts/{name}.md", ct);
                return Results.Ok(new { name, content });
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { error = $"Layout '{name}' not found" });
            }
        });

        app.MapGet("/api/backgrounds", () =>
        {
            var bgDir = Path.Combine(contentRoot, "backgrounds");
            if (!Directory.Exists(bgDir))
            {
                return Results.Ok(new { backgrounds = Array.Empty<object>() });
            }

            var backgrounds = Directory.GetFiles(bgDir)
                .Where(f => !Path.GetFileName(f).StartsWith('.') && Path.GetFileName(f) != "ATTRIBUTION")
                .Select(f => new
                {
                    filename = Path.GetFileName(f),
                    url = $"/content/backgrounds/{Path.GetFileName(f)}",
                })
                .OrderBy(b => b.filename)
                .ToList();

            return Results.Ok(new { backgrounds });
        });

        app.MapGet("/api/lore", async (ILoreStore loreStore, AppConfig config, CancellationToken ct) =>
        {
            var lore = await loreStore.LoadLoreSetAsync(config.Lore.Active, ct);
            var files = lore.Select(kvp => new
            {
                path = kvp.Key,
                size = kvp.Value.Length,
                tokens = kvp.Value.Length / 4, // rough estimate
            }).ToList();

            var categories = files
                .Select(f => f.path.Contains('/') ? f.path.Split('/')[0] : "root")
                .Distinct()
                .OrderBy(c => c)
                .ToList();

            return Results.Ok(new
            {
                files,
                categories,
                active_project = config.Lore.Active,
                lore_path = $"lore/{config.Lore.Active}",
            });
        });

        app.MapGet("/api/lore/projects", async (ILoreStore loreStore, AppConfig config, CancellationToken ct) =>
        {
            var projects = await loreStore.ListLoreSetsAsync(ct);
            return Results.Ok(new
            {
                projects,
                active = config.Lore.Active,
            });
        });

        // Serve content files (backgrounds, generated images, etc.) as static files
        app.MapGet("/content/{**path}", (string path) =>
        {
            var fullPath = Path.GetFullPath(Path.Combine(contentRoot, path));
            if (!fullPath.StartsWith(contentRoot) || !File.Exists(fullPath))
            {
                return Results.NotFound();
            }
            return Results.File(fullPath);
        });
    }
}
