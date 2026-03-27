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

            var persona = root.TryGetProperty("persona", out var pEl) ? pEl.GetString() ?? config.Persona.Active : config.Persona.Active;
            var lore = root.TryGetProperty("lore", out var lEl) ? lEl.GetString() ?? config.Lore.Active : config.Lore.Active;
            var style = root.TryGetProperty("writingStyle", out var sEl) ? sEl.GetString() ?? config.WritingStyle.Active : config.WritingStyle.Active;
            var layout = root.TryGetProperty("layout", out var layEl) ? layEl.GetString() ?? config.Layout.Active : config.Layout.Active;

            config.Persona = config.Persona with { Active = persona };
            config.Lore = new LoreConfig { Active = lore };
            config.WritingStyle = new WritingStyleConfig { Active = style };
            config.Layout = new LayoutConfig { Active = layout };

            await writer.WriteAsync(configPath, ConfigurationLoader.Serialize(config), ct);

            logger.LogInformation(
                "Profile switched: persona={Persona}, lore={Lore}, style={Style}",
                persona, lore, style);

            return Results.Ok(new
            {
                Status = "ok",
                ActivePersona = persona,
                ActiveLore = lore,
                ActiveWritingStyle = style,
                LoreFiles = 0,
            });
        });

        // Persona endpoints
        app.MapGet("/api/persona", (AppConfig config) =>
        {
            var personaDir = Path.Combine(contentRoot, "persona");
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
            var resolved = Path.GetFullPath(Path.Combine(contentRoot, "persona", filePath));
            if (!resolved.StartsWith(Path.Combine(contentRoot, "persona")) || !File.Exists(resolved))
            {
                return Results.NotFound(new { Error = "File not found" });
            }
            var content = await File.ReadAllTextAsync(resolved, ct);
            return Results.Ok(new { Path = filePath, Content = content, Tokens = content.Length / 4 });
        });

        // Writing style endpoints
        app.MapGet("/api/writing-styles", (AppConfig config) =>
        {
            var stylesDir = Path.Combine(contentRoot, "writing-styles");
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

        // Layout switch — persists to config.yaml
        app.MapPost("/api/layout", async (
            HttpContext httpContext,
            AppConfig config,
            AtomicFileWriter writer,
            ILogger<ConfigurationLoader> logger,
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

            var configPath = Path.Combine(contentRoot, "config.yaml");
            await writer.WriteAsync(configPath, ConfigurationLoader.Serialize(config), ct);

            logger.LogInformation("Layout switched to {Layout}", layout);

            return Results.Ok(new { Layout = layout });
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
                    prompts.Add(new { Name = name, Content = content });
                }
                catch { }
            }
            return Results.Ok(new { Prompts = prompts });
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
                    advisors.Add(new { Name = name, Content = content });
                }
                catch { /* skip unreadable */ }
            }
            return Results.Ok(new { Advisors = advisors });
        });

        // Portraits
        app.MapGet("/api/portraits", () =>
        {
            var dir = Path.Combine(contentRoot, "character-cards");
            if (!Directory.Exists(dir))
            {
                return Results.Ok(new { Portraits = Array.Empty<object>(), Current = (string?)null });
            }

            var portraits = Directory.GetFiles(dir, "*.png")
                .Concat(Directory.GetFiles(dir, "*.jpg"))
                .Concat(Directory.GetFiles(dir, "*.webp"))
                .Select(f => new
                {
                    Filename = Path.GetFileName(f),
                    Url = $"/content/character-cards/{Path.GetFileName(f)}",
                })
                .ToList();

            return Results.Ok(new { Portraits = portraits, Current = (string?)null });
        });

        // Character cards (YAML-backed via ICharacterCardStore)
        app.MapGet("/api/character-cards", async (ICharacterCardStore store, CancellationToken ct) =>
        {
            var cards = await store.ListAsync(ct);
            return Results.Ok(new { Cards = cards });
        });

        app.MapGet("/api/character-cards/{name}", async (string name, ICharacterCardStore store, CancellationToken ct) =>
        {
            var card = await store.LoadAsync(name, ct);
            return card is not null ? Results.Ok(card) : Results.NotFound();
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
                Messages = Array.Empty<object>(),
                SessionId = (string?)null,
            });
        });

        // Provider model fetching (the frontend calls this global endpoint)
        app.MapPost("/api/providers/fetch-models", async (
            HttpContext httpContext,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var url = root.TryGetProperty("baseUrl", out var urlEl) ? urlEl.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(url))
                url = root.TryGetProperty("url", out var urlEl2) ? urlEl2.GetString() ?? "" : "";
            var apiKey = root.TryGetProperty("apiKey", out var keyEl) ? keyEl.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(url))
            {
                return Results.Ok(new { Models = Array.Empty<object>() });
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
                    return Results.Ok(new { Models = Array.Empty<object>(), Error = $"{response.StatusCode}" });
                }

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                // OpenAI format
                if (doc.RootElement.TryGetProperty("data", out var data))
                {
                    var models = data.EnumerateArray()
                        .Select(m => m.GetProperty("id").GetString())
                        .ToList();
                    return Results.Ok(new { Models = models });
                }

                // Ollama format
                if (doc.RootElement.TryGetProperty("models", out var ollamaModels))
                {
                    var models = ollamaModels.EnumerateArray()
                        .Select(m => m.GetProperty("name").GetString())
                        .ToList();
                    return Results.Ok(new { Models = models });
                }

                return Results.Ok(new { Models = Array.Empty<object>() });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { Models = Array.Empty<object>(), Error = ex.Message });
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
            return Results.Ok(new { SessionId = tree.SessionId, Name = tree.Name });
        });

        // Forge create
        app.MapPost("/api/forge/create", async (
            HttpContext httpContext,
            IContentFileService fileService,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var name = body.RootElement.TryGetProperty("name", out var el) ? el.GetString() ?? "untitled" : "untitled";

            // Create the forge project directory structure
            await fileService.WriteAsync($"forge/{name}/plan/.gitkeep", "", ct);
            await fileService.WriteAsync($"forge/{name}/drafts/.gitkeep", "", ct);
            await fileService.WriteAsync($"forge/{name}/output/.gitkeep", "", ct);

            return Results.Ok(new { Name = name, Created = true });
        });

        // Projects (story projects)
        app.MapGet("/api/projects", async (IStoryStore store, CancellationToken ct) =>
        {
            var projects = await store.ListProjectsAsync(ct);
            return Results.Ok(new { Projects = projects });
        });

        // Artifact endpoints
        app.MapGet("/api/artifact/current", (IArtifactService artifacts) =>
        {
            var current = artifacts.GetCurrent();
            return Results.Ok(new { Artifact = current });
        });

        app.MapGet("/api/artifact/formats", (IArtifactService artifacts) =>
        {
            var formats = artifacts.GetFormatInstructions()
                .Select(kvp => new { Name = kvp.Key.ToString().ToLowerInvariant(), Description = kvp.Value })
                .ToList();
            return Results.Ok(new { Formats = formats });
        });

        app.MapGet("/api/artifacts", async (IArtifactService artifacts, CancellationToken ct) =>
        {
            var list = await artifacts.ListAsync(ct);
            return Results.Ok(new { Artifacts = list });
        });

        app.MapPost("/api/artifact/clear", (IArtifactService artifacts) =>
        {
            artifacts.ClearCurrent();
            return Results.Ok(new { Status = "cleared" });
        });
        app.MapGet("/api/tts/providers", (IServiceProvider sp) =>
        {
            var tts = sp.GetService<ITtsGenerator>();
            if (tts is QuillForge.Providers.Tts.FallbackTtsGenerator fallback)
            {
                var providerNames = fallback.Providers
                    .Select(p => p.GetType().Name.Replace("TtsGenerator", ""))
                    .ToList();
                return Results.Ok(new { Available = true, Providers = providerNames });
            }

            return Results.Ok(new { Available = tts is not null, Providers = Array.Empty<string>() });
        });

        app.MapPost("/api/tts/generate", async (
            HttpContext httpContext,
            IServiceProvider sp,
            CancellationToken ct) =>
        {
            var tts = sp.GetService<ITtsGenerator>();
            if (tts is null)
            {
                return Results.BadRequest(new { Error = "No TTS provider configured. Set OPENAI_API_KEY or ELEVENLABS_API_KEY." });
            }

            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var text = root.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(text))
            {
                return Results.BadRequest(new { Error = "Text is required." });
            }

            var options = new TtsOptions
            {
                Voice = root.TryGetProperty("voice", out var voiceEl) ? voiceEl.GetString() : null,
                Speed = root.TryGetProperty("speed", out var speedEl) && speedEl.TryGetDouble(out var spd) ? spd : null,
            };

            try
            {
                var result = await tts.GenerateAsync(text, options, ct);
                return Results.Ok(new
                {
                    FilePath = result.FilePath,
                    Duration = result.Duration.TotalSeconds,
                    FileName = Path.GetFileName(result.FilePath),
                });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 502, title: "TTS generation failed");
            }
        });
    }
}
