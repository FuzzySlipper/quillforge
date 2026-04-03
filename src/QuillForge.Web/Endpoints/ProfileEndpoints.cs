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
            AppConfig runtimeConfig,
            IAppConfigStore configStore,
            ILogger<AppConfig> logger,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var persona = root.TryGetProperty("persona", out var pEl) ? pEl.GetString() : null;
            var lore = root.TryGetProperty("lore", out var lEl) ? lEl.GetString() : null;
            var narrativeRules = root.TryGetProperty("narrativeRules", out var nrEl) ? nrEl.GetString() : null;
            var style = root.TryGetProperty("writingStyle", out var sEl) ? sEl.GetString() : null;
            var layout = root.TryGetProperty("layout", out var layEl) ? layEl.GetString() : null;

            var updatedConfig = await configStore.UpdateAsync(current => current with
            {
                Persona = current.Persona with { Active = persona ?? current.Persona.Active },
                Lore = current.Lore with { Active = lore ?? current.Lore.Active },
                NarrativeRules = current.NarrativeRules with { Active = narrativeRules ?? current.NarrativeRules.Active },
                WritingStyle = current.WritingStyle with { Active = style ?? current.WritingStyle.Active },
                Layout = current.Layout with { Active = layout ?? current.Layout.Active },
            }, ct);
            AppConfigRuntimeSync.CopyFrom(runtimeConfig, updatedConfig);

            logger.LogInformation(
                "Profile switched: persona={Persona}, lore={Lore}, narrativeRules={NarrativeRules}, style={Style}",
                updatedConfig.Persona.Active,
                updatedConfig.Lore.Active,
                updatedConfig.NarrativeRules.Active,
                updatedConfig.WritingStyle.Active);

            return Results.Ok(new ProfileSwitchResponse
            {
                ActivePersona = updatedConfig.Persona.Active,
                ActiveLore = updatedConfig.Lore.Active,
                ActiveNarrativeRules = updatedConfig.NarrativeRules.Active,
                ActiveWritingStyle = updatedConfig.WritingStyle.Active,
                LoreFiles = 0,
            });
        });

        // Conductor endpoints, exposed through the legacy /api/persona routes
        app.MapGet("/api/persona", () =>
        {
            var conductorRoot = Path.Combine(contentRoot, ContentPaths.Conductor);
            var files = ListConductorFiles(contentRoot);
            if (files.Count == 0)
            {
                return Results.Ok(new { Files = Array.Empty<object>(), PersonaPath = conductorRoot });
            }

            return Results.Ok(new { Files = files, PersonaPath = conductorRoot });
        });

        app.MapGet("/api/persona/{**filePath}", async (string filePath, CancellationToken ct) =>
        {
            var resolved = ResolveConductorFile(contentRoot, filePath);
            if (resolved is null)
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

            var normalizedPath = filePath.Replace('\\', '/');
            await fileService.WriteAsync($"{ContentPaths.Conductor}/{normalizedPath}", content, ct);
            return Results.Ok(new { Path = filePath, Status = "ok" });
        });

        // Narrative rules endpoints
        app.MapGet("/api/narrative-rules", (AppConfig config) =>
        {
            var rulesDir = Path.Combine(contentRoot, ContentPaths.NarrativeRules);
            if (!Directory.Exists(rulesDir))
            {
                return Results.Ok(new { Files = Array.Empty<object>(), Active = config.NarrativeRules.Active });
            }

            var files = new List<object>();
            foreach (var p in Directory.GetFiles(rulesDir, "*.md").OrderBy(f => f))
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

            return Results.Ok(new { Files = files, Active = config.NarrativeRules.Active });
        });

        app.MapGet("/api/narrative-rules/{name}", async (
            string name,
            INarrativeRulesStore store,
            CancellationToken ct) =>
        {
            var content = await store.LoadAsync(name, ct);
            return Results.Ok(new { Path = name, Content = content, Tokens = content.Length / 4 });
        });

        app.MapPut("/api/narrative-rules/{name}", async (
            string name,
            HttpContext httpContext,
            IContentFileService fileService,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var content = body.RootElement.TryGetProperty("content", out var el) ? el.GetString() ?? "" : "";

            await fileService.WriteAsync($"{ContentPaths.NarrativeRules}/{name}.md", content, ct);
            return Results.Ok(new { Name = name, Status = "ok" });
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
            AppConfig runtimeConfig,
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

            var updatedConfig = await configStore.UpdateAsync(current => current with
            {
                Layout = current.Layout with { Active = layout }
            }, ct);
            AppConfigRuntimeSync.CopyFrom(runtimeConfig, updatedConfig);

            logger.LogInformation("Layout switched to {Layout}", layout);

            return Results.Ok(new { Layout = updatedConfig.Layout.Active });
        });
    }

    private static List<object> ListConductorFiles(string contentRoot)
    {
        var files = new List<object>();
        foreach (var entry in EnumerateConductorFiles(contentRoot))
        {
            var content = File.ReadAllText(entry.ResolvedPath);
            files.Add(new { Path = entry.RelativePath, Tokens = content.Length / 4, Size = content.Length });
        }

        return files;
    }

    private static IEnumerable<(string RelativePath, string ResolvedPath)> EnumerateConductorFiles(string contentRoot)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in EnumerateConductorRoots(contentRoot))
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var path in Directory.GetFiles(root, "*.md", SearchOption.AllDirectories).OrderBy(f => f))
            {
                var relativePath = Path.GetRelativePath(root, path).Replace('\\', '/');
                if (seen.Add(relativePath))
                {
                    yield return (relativePath, path);
                }
            }
        }
    }

    private static string? ResolveConductorFile(string contentRoot, string filePath)
    {
        foreach (var root in EnumerateConductorRoots(contentRoot))
        {
            var resolved = Path.GetFullPath(Path.Combine(root, filePath));
            var normalizedRoot = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
            if (!resolved.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (File.Exists(resolved))
            {
                return resolved;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateConductorRoots(string contentRoot)
    {
        yield return Path.Combine(contentRoot, ContentPaths.Conductor);
        yield return Path.Combine(contentRoot, ContentPaths.Persona);
    }
}
