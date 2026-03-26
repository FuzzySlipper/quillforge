using System.Text.Json;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.Configuration;
using QuillForge.Storage.Utilities;

namespace QuillForge.Web.Endpoints;

public static class ProfileEndpoints
{
    public static void MapProfileEndpoints(this WebApplication app, string contentRoot)
    {
        // Switch active persona/lore/writing style
        app.MapPost("/api/profiles/switch", async (
            HttpContext httpContext,
            AppConfig config,
            AtomicFileWriter writer,
            ILogger<ConfigurationLoader> logger,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var configPath = Path.Combine(contentRoot, "config.yaml");
            var loader = new ConfigurationLoader(logger);
            var current = loader.Load(configPath);

            var persona = GetString(root, "persona") ?? current.Persona.Active;
            var lore = GetString(root, "lore", "lore_set") ?? current.Lore.Active;
            var style = GetString(root, "writing_style") ?? current.WritingStyle.Active;
            var layout = GetString(root, "layout") ?? current.Layout.Active;

            // Build YAML without leading indentation
            var yaml =
                $"models:\n" +
                $"  orchestrator: {current.Models.Orchestrator}\n" +
                $"  prose_writer: {current.Models.ProseWriter}\n" +
                $"  librarian: {current.Models.Librarian}\n" +
                $"  forge_writer: {current.Models.ForgeWriter}\n" +
                $"  forge_planner: {current.Models.ForgePlanner}\n" +
                $"  forge_reviewer: {current.Models.ForgeReviewer}\n" +
                $"persona:\n" +
                $"  active: {persona}\n" +
                $"  max_tokens: {current.Persona.MaxTokens}\n" +
                $"lore:\n" +
                $"  active: {lore}\n" +
                $"writing_style:\n" +
                $"  active: {style}\n" +
                $"layout:\n" +
                $"  active: {layout}\n" +
                $"roleplay:\n" +
                $"  ai_character: {current.Roleplay.AiCharacter ?? ""}\n" +
                $"  user_character: {current.Roleplay.UserCharacter ?? ""}\n" +
                $"forge:\n" +
                $"  review_pass_threshold: {current.Forge.ReviewPassThreshold}\n" +
                $"  max_revisions: {current.Forge.MaxRevisions}\n" +
                $"  pause_after_chapter1: {current.Forge.PauseAfterChapter1.ToString().ToLowerInvariant()}\n" +
                $"  stage_timeout_minutes: {current.Forge.StageTimeoutMinutes}\n" +
                $"web_search:\n" +
                $"  enabled: {current.WebSearch.Enabled.ToString().ToLowerInvariant()}\n" +
                $"  provider: {current.WebSearch.Provider}\n" +
                $"  searxng_url: {current.WebSearch.SearxngUrl ?? ""}\n" +
                $"  max_results: {current.WebSearch.MaxResults}\n";

            await writer.WriteAsync(configPath, yaml, ct);

            logger.LogInformation(
                "Profile switched: persona={Persona}, lore={Lore}, style={Style}",
                persona, lore, style);

            return Results.Ok(new
            {
                status = "ok",
                active_persona = persona,
                active_lore = lore,
                active_writing_style = style,
                lore_files = 0,
            });
        });

        // Persona endpoints — matches Python response shape: { files: [...], persona_path: "..." }
        app.MapGet("/api/persona", (AppConfig config) =>
        {
            var personaDir = Path.Combine(contentRoot, "persona");
            if (!Directory.Exists(personaDir))
            {
                return Results.Ok(new { files = Array.Empty<object>(), persona_path = personaDir });
            }

            var files = new List<object>();
            foreach (var p in Directory.GetFiles(personaDir, "*.md", SearchOption.AllDirectories).OrderBy(f => f))
            {
                var rel = Path.GetRelativePath(personaDir, p);
                var content = File.ReadAllText(p);
                files.Add(new { path = rel, tokens = content.Length / 4, size = content.Length });
            }

            return Results.Ok(new { files, persona_path = personaDir });
        });

        app.MapGet("/api/persona/{**filePath}", async (string filePath, CancellationToken ct) =>
        {
            var resolved = Path.GetFullPath(Path.Combine(contentRoot, "persona", filePath));
            if (!resolved.StartsWith(Path.Combine(contentRoot, "persona")) || !File.Exists(resolved))
            {
                return Results.NotFound(new { error = "File not found" });
            }
            var content = await File.ReadAllTextAsync(resolved, ct);
            return Results.Ok(new { path = filePath, content, tokens = content.Length / 4 });
        });

        // Writing style endpoints — matches Python: { files: [...], active: "..." }
        app.MapGet("/api/writing-styles", (AppConfig config) =>
        {
            var stylesDir = Path.Combine(contentRoot, "writing-styles");
            if (!Directory.Exists(stylesDir))
            {
                return Results.Ok(new { files = Array.Empty<object>(), active = config.WritingStyle.Active });
            }

            var files = new List<object>();
            foreach (var p in Directory.GetFiles(stylesDir, "*.md").OrderBy(f => f))
            {
                var content = File.ReadAllText(p);
                files.Add(new
                {
                    path = Path.GetFileName(p),
                    name = Path.GetFileNameWithoutExtension(p),
                    tokens = content.Length / 4,
                    size = content.Length,
                });
            }

            return Results.Ok(new { files, active = config.WritingStyle.Active });
        });

        app.MapGet("/api/writing-styles/{name}", async (string name, IWritingStyleStore store, CancellationToken ct) =>
        {
            var content = await store.LoadAsync(name, ct);
            return Results.Ok(new { path = name, content, tokens = content.Length / 4 });
        });

        // Layout switch
        app.MapPost("/api/layout", async (
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var layout = GetString(body.RootElement, "layout", "name", "active") ?? "default";
            return Results.Ok(new { layout });
        });

        // Forge prompts
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
                    prompts.Add(new { name, content });
                }
                catch { }
            }
            return Results.Ok(new { prompts });
        });

        // Prompts (council advisors) endpoint
        app.MapGet("/api/council", async (IContentFileService fileService, CancellationToken ct) =>
        {
            var files = await fileService.ListAsync("council", "*.md", ct);
            var advisors = new List<object>();
            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file.Split('/').Last());
                try
                {
                    var content = await fileService.ReadAsync(file, ct);
                    advisors.Add(new { name, content });
                }
                catch { /* skip unreadable */ }
            }
            return Results.Ok(new { advisors });
        });

        // Portraits
        app.MapGet("/api/portraits", () =>
        {
            var dir = Path.Combine(contentRoot, "character-cards");
            if (!Directory.Exists(dir))
            {
                return Results.Ok(new { portraits = Array.Empty<object>(), current = (string?)null });
            }

            var portraits = Directory.GetFiles(dir, "*.png")
                .Concat(Directory.GetFiles(dir, "*.jpg"))
                .Concat(Directory.GetFiles(dir, "*.webp"))
                .Select(f => new
                {
                    filename = Path.GetFileName(f),
                    url = $"/content/character-cards/{Path.GetFileName(f)}",
                })
                .ToList();

            return Results.Ok(new { portraits, current = (string?)null });
        });

        // Character cards
        app.MapGet("/api/character-cards", () =>
        {
            var dir = Path.Combine(contentRoot, "character-cards");
            if (!Directory.Exists(dir))
            {
                return Results.Ok(new { cards = Array.Empty<object>() });
            }

            var cards = Directory.GetFiles(dir, "*.json")
                .Select(f => new { name = Path.GetFileNameWithoutExtension(f) })
                .ToList();

            return Results.Ok(new { cards });
        });

        // Conversation history (the frontend calls this instead of sessions/load)
        app.MapGet("/api/conversation/history", async (
            ISessionStore sessionStore,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            // Return empty history if no active session
            return Results.Ok(new
            {
                messages = Array.Empty<object>(),
                session_id = (string?)null,
            });
        });

        // Provider model fetching (the frontend calls this global endpoint)
        app.MapPost("/api/providers/fetch-models", async (
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var url = GetString(root, "url", "base_url", "baseUrl") ?? "";
            var apiKey = GetString(root, "api_key", "apiKey", "key") ?? "";

            if (string.IsNullOrEmpty(url))
            {
                return Results.Ok(new { models = Array.Empty<object>() });
            }

            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                if (!string.IsNullOrEmpty(apiKey))
                {
                    httpClient.DefaultRequestHeaders.Authorization = new("Bearer", apiKey);
                }

                // Try /v1/models first, then /api/tags (Ollama)
                var modelsUrl = url.TrimEnd('/');
                if (!modelsUrl.EndsWith("/v1/models") && !modelsUrl.EndsWith("/api/tags"))
                {
                    modelsUrl += "/v1/models";
                }

                var response = await httpClient.GetAsync(modelsUrl, ct);
                if (!response.IsSuccessStatusCode)
                {
                    // Try Ollama format
                    modelsUrl = url.TrimEnd('/') + "/api/tags";
                    response = await httpClient.GetAsync(modelsUrl, ct);
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Results.Ok(new { models = Array.Empty<object>(), error = $"{response.StatusCode}" });
                }

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                // OpenAI format
                if (doc.RootElement.TryGetProperty("data", out var data))
                {
                    var models = data.EnumerateArray()
                        .Select(m => m.GetProperty("id").GetString())
                        .ToList();
                    return Results.Ok(new { models });
                }

                // Ollama format
                if (doc.RootElement.TryGetProperty("models", out var ollamaModels))
                {
                    var models = ollamaModels.EnumerateArray()
                        .Select(m => m.GetProperty("name").GetString())
                        .ToList();
                    return Results.Ok(new { models });
                }

                return Results.Ok(new { models = Array.Empty<object>() });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { models = Array.Empty<object>(), error = ex.Message });
            }
        });

        // Session new (the frontend also calls this path)
        app.MapPost("/api/session/new", async (
            ISessionStore store,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var tree = new QuillForge.Core.Models.ConversationTree(
                Guid.CreateVersion7(),
                "New Session",
                loggerFactory.CreateLogger<QuillForge.Core.Models.ConversationTree>());
            await store.SaveAsync(tree, ct);
            return Results.Ok(new { session_id = tree.SessionId, name = tree.Name });
        });

        // Forge create
        app.MapPost("/api/forge/create", async (
            HttpContext httpContext,
            IContentFileService fileService,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var name = GetString(body.RootElement, "name", "project", "project_name") ?? "untitled";

            // Create the forge project directory structure
            await fileService.WriteAsync($"forge/{name}/plan/.gitkeep", "", ct);
            await fileService.WriteAsync($"forge/{name}/drafts/.gitkeep", "", ct);
            await fileService.WriteAsync($"forge/{name}/output/.gitkeep", "", ct);

            return Results.Ok(new { name, created = true });
        });

        // Projects (story projects)
        app.MapGet("/api/projects", async (IStoryStore store, CancellationToken ct) =>
        {
            var projects = await store.ListProjectsAsync(ct);
            return Results.Ok(new { projects });
        });

        // Stub endpoints that return empty data to prevent 404/405 crashes
        app.MapGet("/api/artifact/current", () => Results.Ok(new { artifact = (object?)null }));
        app.MapGet("/api/artifact/formats", () => Results.Ok(new { formats = Array.Empty<string>() }));
        app.MapGet("/api/tts/providers", () => Results.Ok(new { providers = Array.Empty<object>() }));
    }

    private static string? GetString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString();
            }
        }
        return null;
    }
}
