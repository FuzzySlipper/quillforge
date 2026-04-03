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
        // Switch active conductor/lore/writing style
        app.MapPost("/api/profiles/switch", async (
            ProfileSwitchRequest request,
            ISessionRuntimeService runtimeService,
            CancellationToken ct) =>
        {
            var result = await runtimeService.SetProfileAsync(
                request.SessionId,
                new SetSessionProfileCommand(
                    request.ProfileId,
                    request.Persona,
                    request.Lore,
                    request.NarrativeRules,
                    request.WritingStyle),
                ct);

            if (result.Status == SessionMutationStatus.Busy)
            {
                return Results.Conflict(new
                {
                    error = "session_busy",
                    message = result.Error,
                });
            }

            if (result.Status == SessionMutationStatus.Invalid)
            {
                return Results.BadRequest(new
                {
                    error = "invalid_session_mutation",
                    message = result.Error,
                });
            }

            var state = result.Value!;

            return Results.Ok(new ProfileSwitchResponse
            {
                ActiveProfileId = state.Profile.ProfileId ?? "default",
                ActivePersona = state.Profile.ActivePersona ?? "default",
                ActiveLore = state.Profile.ActiveLoreSet ?? "default",
                ActiveNarrativeRules = state.Profile.ActiveNarrativeRules ?? "default",
                ActiveWritingStyle = state.Profile.ActiveWritingStyle ?? "default",
                LoreFiles = 0,
            });
        });

        app.MapGet("/api/profile-configs", async (
            IProfileConfigService profileService,
            CancellationToken ct) =>
        {
            var profiles = await profileService.ListAsync(ct);
            var defaultProfileId = await profileService.GetDefaultProfileIdAsync(ct);

            return Results.Ok(new ProfileConfigListResponse
            {
                Profiles = profiles,
                DefaultProfileId = defaultProfileId,
            });
        });

        app.MapGet("/api/profile-configs/{profileId}", async (
            string profileId,
            IProfileConfigService profileService,
            CancellationToken ct) =>
        {
            try
            {
                var resolved = await profileService.LoadResolvedAsync(profileId, ct);
                return Results.Ok(ToProfileConfigResponse(resolved));
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { Error = $"Profile {profileId} not found" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        app.MapPut("/api/profile-configs/{profileId}", async (
            string profileId,
            HttpContext httpContext,
            IProfileConfigService profileService,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var config = new ProfileConfig
            {
                Conductor = root.TryGetProperty("conductor", out var conductorEl)
                    ? conductorEl.GetString() ?? "default"
                    : root.TryGetProperty("persona", out var personaEl)
                        ? personaEl.GetString() ?? "default"
                        : "default",
                LoreSet = root.TryGetProperty("loreSet", out var loreSetEl)
                    ? loreSetEl.GetString() ?? "default"
                    : root.TryGetProperty("lore", out var loreEl)
                        ? loreEl.GetString() ?? "default"
                        : "default",
                NarrativeRules = root.TryGetProperty("narrativeRules", out var rulesEl)
                    ? rulesEl.GetString() ?? "default"
                    : "default",
                WritingStyle = root.TryGetProperty("writingStyle", out var styleEl)
                    ? styleEl.GetString() ?? "default"
                    : "default",
            };

            try
            {
                var saved = await profileService.SaveAsync(profileId, config, ct);
                return Results.Ok(ToProfileConfigResponse(saved));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
        });

        app.MapPost("/api/profile-configs/{profileId}/select", async (
            string profileId,
            AppConfig runtimeConfig,
            IProfileConfigService profileService,
            CancellationToken ct) =>
        {
            try
            {
                var selection = await profileService.SelectAsync(profileId, ct);
                AppConfigRuntimeSync.CopyFrom(runtimeConfig, selection.UpdatedAppConfig);
                return Results.Ok(new ProfileSwitchResponse
                {
                    ActiveProfileId = selection.ProfileId,
                    ActivePersona = selection.Config.Conductor,
                    ActiveLore = selection.Config.LoreSet,
                    ActiveNarrativeRules = selection.Config.NarrativeRules,
                    ActiveWritingStyle = selection.Config.WritingStyle,
                    LoreFiles = 0,
                });
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { Error = $"Profile {profileId} not found" });
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { Error = ex.Message });
            }
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

    private static ProfileConfigResponse ToProfileConfigResponse(ResolvedProfileConfig resolved)
    {
        return new ProfileConfigResponse
        {
            ProfileId = resolved.ProfileId,
            Conductor = resolved.Config.Conductor,
            LoreSet = resolved.Config.LoreSet,
            NarrativeRules = resolved.Config.NarrativeRules,
            WritingStyle = resolved.Config.WritingStyle,
        };
    }
}
