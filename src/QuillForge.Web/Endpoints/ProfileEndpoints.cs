using System.Text.Json;
using QuillForge.Core;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Contracts;

namespace QuillForge.Web.Endpoints;

public static class ProfileEndpoints
{
    public static void MapProfileEndpoints(this WebApplication app, string contentRoot)
    {
        // Switch active persona/lore/writing style
        app.MapPost("/api/profiles/switch", async (
            HttpContext httpContext,
            AppConfig config,
            IAppConfigStore configStore,
            ILogger<AppConfig> logger,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var persona = root.TryGetProperty("persona", out var pEl) ? pEl.GetString() ?? config.Persona.Active : config.Persona.Active;
            var lore = root.TryGetProperty("lore", out var lEl) ? lEl.GetString() ?? config.Lore.Active : config.Lore.Active;
            var style = root.TryGetProperty("writingStyle", out var sEl) ? sEl.GetString() ?? config.WritingStyle.Active : config.WritingStyle.Active;
            var layout = root.TryGetProperty("layout", out var layEl) ? layEl.GetString() ?? config.Layout.Active : config.Layout.Active;

            config.Persona = config.Persona with { Active = persona };
            config.Lore = new LoreConfig { Active = lore };
            config.WritingStyle = new WritingStyleConfig { Active = style };
            config.Layout = new LayoutConfig { Active = layout };

            await configStore.SaveAsync(config, ct);

            logger.LogInformation(
                "Profile switched: persona={Persona}, lore={Lore}, style={Style}",
                persona, lore, style);

            return Results.Ok(new ProfileSwitchResponse
            {
                ActivePersona = persona,
                ActiveLore = lore,
                ActiveWritingStyle = style,
                LoreFiles = 0,
            });
        });

        // Persona endpoints
        app.MapGet("/api/persona", (AppConfig config) =>
        {
            var personaDir = Path.Combine(contentRoot, ContentPaths.Persona);
            if (!Directory.Exists(personaDir))
            {
                return Results.Ok(new { Files = Array.Empty<object>(), PersonaPath = personaDir });
            }

            var files = new List<object>();
            foreach (var p in Directory.GetFiles(personaDir, "*.md", SearchOption.AllDirectories).OrderBy(f => f))
            {
                var rel = Path.GetRelativePath(personaDir, p);
                var content = File.ReadAllText(p);
                files.Add(new { Path = rel, Tokens = content.Length / 4, Size = content.Length });
            }

            return Results.Ok(new { Files = files, PersonaPath = personaDir });
        });

        app.MapGet("/api/persona/{**filePath}", async (string filePath, CancellationToken ct) =>
        {
            var resolved = Path.GetFullPath(Path.Combine(contentRoot, ContentPaths.Persona, filePath));
            var personaRoot = Path.Combine(contentRoot, ContentPaths.Persona) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(personaRoot, StringComparison.OrdinalIgnoreCase) || !File.Exists(resolved))
            {
                return Results.NotFound(new { Error = "File not found" });
            }
            var content = await File.ReadAllTextAsync(resolved, ct);
            return Results.Ok(new { Path = filePath, Content = content, Tokens = content.Length / 4 });
        });

        app.MapPut("/api/persona/{**filePath}", async (
            string filePath,
            HttpContext httpContext,
            IContentFileService fileService,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var content = body.RootElement.TryGetProperty("content", out var el) ? el.GetString() ?? "" : "";

            await fileService.WriteAsync($"persona/{filePath}", content, ct);
            return Results.Ok(new { Path = filePath, Status = "ok" });
        });

        // Writing style endpoints
        app.MapGet("/api/writing-styles", (AppConfig config) =>
        {
            var stylesDir = Path.Combine(contentRoot, ContentPaths.WritingStyles);
            if (!Directory.Exists(stylesDir))
            {
                return Results.Ok(new { Files = Array.Empty<object>(), Active = config.WritingStyle.Active });
            }

            var files = new List<object>();
            foreach (var p in Directory.GetFiles(stylesDir, "*.md").OrderBy(f => f))
            {
                var content = File.ReadAllText(p);
                files.Add(new
                {
                    Path = Path.GetFileName(p),
                    Name = Path.GetFileNameWithoutExtension(p),
                    Tokens = content.Length / 4,
                    Size = content.Length,
                });
            }

            return Results.Ok(new { Files = files, Active = config.WritingStyle.Active });
        });

        app.MapGet("/api/writing-styles/{name}", async (string name, IWritingStyleStore store, CancellationToken ct) =>
        {
            var content = await store.LoadAsync(name, ct);
            return Results.Ok(new { Path = name, Content = content, Tokens = content.Length / 4 });
        });

        app.MapPut("/api/writing-styles/{name}", async (
            string name,
            HttpContext httpContext,
            IContentFileService fileService,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var content = body.RootElement.TryGetProperty("content", out var el) ? el.GetString() ?? "" : "";

            await fileService.WriteAsync($"writing-styles/{name}.md", content, ct);
            return Results.Ok(new { Name = name, Status = "ok" });
        });

        // Layout switch — persists to config.yaml
        app.MapPost("/api/layout", async (
            HttpContext httpContext,
            AppConfig config,
            IAppConfigStore configStore,
            ILogger<AppConfig> logger,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            // Accept both "name" (frontend) and "layout" (API) property names
            var layout = "default";
            if (root.TryGetProperty("name", out var nameEl) && nameEl.GetString() is { } nameVal)
                layout = nameVal;
            else if (root.TryGetProperty("layout", out var layEl) && layEl.GetString() is { } layVal)
                layout = layVal;

            config.Layout = new LayoutConfig { Active = layout };

            await configStore.SaveAsync(config, ct);

            logger.LogInformation("Layout switched to {Layout}", layout);

            return Results.Ok(new { Layout = layout });
        });
    }
}
