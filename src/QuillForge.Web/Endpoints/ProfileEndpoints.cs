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
            var personaRoot = Path.Combine(contentRoot, "persona") + Path.DirectorySeparatorChar;
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

        // Council member CRUD
        app.MapGet("/api/council/members", async (ICouncilService councilService, CancellationToken ct) =>
        {
            var members = await councilService.LoadMembersAsync(ct);
            return Results.Ok(new
            {
                Members = members.Select(m => new
                {
                    m.Name,
                    m.Model,
                    m.ProviderAlias,
                    m.SystemPrompt,
                })
            });
        });

        app.MapPost("/api/council/members", async (HttpContext httpContext, IContentFileService fileService, CancellationToken ct) =>
        {
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(httpContext.Request.Body, cancellationToken: ct);
            var name = body.GetProperty("name").GetString()?.Trim().ToLowerInvariant() ?? "";

            if (string.IsNullOrEmpty(name) || !System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-z0-9\-]+$"))
                return Results.BadRequest(new { Error = "Name must be lowercase alphanumeric with hyphens only" });

            var path = $"council/{name}.md";
            if (await fileService.ExistsAsync(path, ct))
                return Results.Conflict(new { Error = $"Member '{name}' already exists" });

            var model = body.TryGetProperty("model", out var m) ? m.GetString() : null;
            var provider = body.TryGetProperty("providerAlias", out var p) ? p.GetString() : null;
            var systemPrompt = body.TryGetProperty("systemPrompt", out var sp) ? sp.GetString() ?? "" : "";

            var content = SerializeMemberFile(model, provider, systemPrompt);
            await fileService.WriteAsync(path, content, ct);
            return Results.Ok(new { Name = name });
        });

        app.MapPut("/api/council/members/{name}", async (string name, HttpContext httpContext, IContentFileService fileService, CancellationToken ct) =>
        {
            var path = $"council/{name}.md";
            if (!await fileService.ExistsAsync(path, ct))
                return Results.NotFound(new { Error = $"Member '{name}' not found" });

            var body = await JsonSerializer.DeserializeAsync<JsonElement>(httpContext.Request.Body, cancellationToken: ct);
            var model = body.TryGetProperty("model", out var m) ? m.GetString() : null;
            var provider = body.TryGetProperty("providerAlias", out var p) ? p.GetString() : null;
            var systemPrompt = body.TryGetProperty("systemPrompt", out var sp) ? sp.GetString() ?? "" : "";

            var content = SerializeMemberFile(model, provider, systemPrompt);
            await fileService.WriteAsync(path, content, ct);
            return Results.Ok(new { Name = name });
        });

        app.MapDelete("/api/council/members/{name}", async (string name, IContentFileService fileService, CancellationToken ct) =>
        {
            var path = $"council/{name}.md";
            if (!await fileService.ExistsAsync(path, ct))
                return Results.NotFound(new { Error = $"Member '{name}' not found" });

            await fileService.DeleteAsync(path, ct);
            return Results.Ok(new { Deleted = name });
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
        app.MapGet("/api/character-cards", async (ICharacterCardStore store, AppConfig config, CancellationToken ct) =>
        {
            var cards = await store.ListAsync(ct);
            return Results.Ok(new
            {
                Cards = cards,
                ActiveAi = config.Roleplay.AiCharacter,
                ActiveUser = config.Roleplay.UserCharacter,
            });
        });

        app.MapGet("/api/character-cards/{name}", async (string name, ICharacterCardStore store, CancellationToken ct) =>
        {
            var card = await store.LoadAsync(name, ct);
            return card is not null ? Results.Ok(card) : Results.NotFound();
        });

        app.MapPost("/api/character-cards", async (
            HttpContext httpContext,
            ICharacterCardStore store,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var name = root.GetProperty("name").GetString() ?? "New Character";
            var card = new CharacterCard
            {
                Name = name,
                Portrait = root.TryGetProperty("portrait", out var p) ? p.GetString() : null,
                Personality = root.TryGetProperty("personality", out var per) ? per.GetString() : null,
                Description = root.TryGetProperty("description", out var d) ? d.GetString() : null,
                Scenario = root.TryGetProperty("scenario", out var s) ? s.GetString() : null,
                Greeting = root.TryGetProperty("greeting", out var g) ? g.GetString() : null,
            };

            var fileName = store.NewTemplate(name).FileName!;
            await store.SaveAsync(fileName, card, ct);
            return Results.Ok(new { Status = "ok", Filename = fileName });
        });

        app.MapPut("/api/character-cards/{name}", async (
            string name,
            HttpContext httpContext,
            ICharacterCardStore store,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var card = new CharacterCard
            {
                Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? name : name,
                Portrait = root.TryGetProperty("portrait", out var p) ? p.GetString() : null,
                Personality = root.TryGetProperty("personality", out var per) ? per.GetString() : null,
                Description = root.TryGetProperty("description", out var d) ? d.GetString() : null,
                Scenario = root.TryGetProperty("scenario", out var s) ? s.GetString() : null,
                Greeting = root.TryGetProperty("greeting", out var g) ? g.GetString() : null,
                FileName = name,
            };

            await store.SaveAsync(name, card, ct);
            return Results.Ok(new { Status = "ok" });
        });

        app.MapDelete("/api/character-cards/{name}", (string name, ICharacterCardStore store) =>
        {
            var cardsDir = Path.Combine(contentRoot, "character-cards");
            var path = Path.Combine(cardsDir, name + ".yaml");
            if (!File.Exists(path))
            {
                return Results.NotFound(new { Error = "Card not found" });
            }
            File.Delete(path);
            return Results.Ok(new { Deleted = name });
        });

        app.MapPost("/api/character-cards/activate", async (
            HttpContext httpContext,
            AppConfig config,
            AtomicFileWriter writer,
            ILogger<ConfigurationLoader> logger,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var aiCharacter = root.TryGetProperty("aiCharacter", out var ai) ? ai.GetString() : config.Roleplay.AiCharacter;
            var userCharacter = root.TryGetProperty("userCharacter", out var user) ? user.GetString() : config.Roleplay.UserCharacter;

            config.Roleplay = new RoleplayConfig
            {
                AiCharacter = aiCharacter,
                UserCharacter = userCharacter,
            };

            var configPath = Path.Combine(contentRoot, "config.yaml");
            await writer.WriteAsync(configPath, ConfigurationLoader.Serialize(config), ct);

            return Results.Ok(new { Status = "ok", AiCharacter = aiCharacter, UserCharacter = userCharacter });
        });

        // Bulk import: scan a directory for Tavern card PNGs
        app.MapPost("/api/character-cards/import-dir", async (
            HttpContext httpContext,
            ICharacterCardStore store,
            ILogger<ICharacterCardStore> logger,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var dir = body.RootElement.GetProperty("path").GetString();

            if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
                return Results.BadRequest(new { Error = $"Directory not found: {dir}" });

            var pngs = Directory.GetFiles(dir, "*.png");
            var imported = new List<object>();
            var skipped = new List<object>();

            foreach (var png in pngs)
            {
                try
                {
                    var card = await store.ImportTavernCardAsync(png, ct);
                    imported.Add(new { card.Name, card.FileName, card.Portrait });
                }
                catch (InvalidOperationException)
                {
                    skipped.Add(new { File = Path.GetFileName(png), Reason = "No card data in PNG" });
                }
                catch (Exception ex)
                {
                    skipped.Add(new { File = Path.GetFileName(png), Reason = ex.Message });
                    logger.LogWarning(ex, "Failed to import {File}", png);
                }
            }

            return Results.Ok(new { Imported = imported, Skipped = skipped });
        });

        app.MapPost("/api/character-cards/import", async (
            HttpContext httpContext,
            ICharacterCardStore store,
            CancellationToken ct) =>
        {
            if (!httpContext.Request.HasFormContentType)
            {
                return Results.BadRequest(new { Error = "Expected multipart form data" });
            }

            var form = await httpContext.Request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0)
            {
                return Results.BadRequest(new { Error = "No file uploaded" });
            }

            // Save to temp file for import
            var tempPath = Path.Combine(Path.GetTempPath(), $"card-import-{Guid.NewGuid():N}.png");
            try
            {
                await using (var stream = File.Create(tempPath))
                {
                    await file.CopyToAsync(stream, ct);
                }

                try
                {
                    var card = await store.ImportTavernCardAsync(tempPath, ct);
                    return Results.Ok(new
                    {
                        Status = "ok",
                        Card = new { Filename = card.FileName, card.Name, card.Portrait },
                    });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { Error = ex.Message });
                }
            }
            finally
            {
                try { File.Delete(tempPath); } catch { /* best effort */ }
            }
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
            AppConfig appConfig,
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
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(appConfig.Timeouts.ProviderHttpSeconds) };
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

            return Results.Ok(new { Status = "ok", Name = name, Created = true });
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
        // TTS — returns audio blob directly (matches frontend expectation)
        app.MapPost("/api/tts", async (
            HttpContext httpContext,
            IServiceProvider sp,
            CancellationToken ct) =>
        {
            var tts = sp.GetService<ITtsGenerator>();
            if (tts is null)
            {
                return Results.BadRequest(new { Error = "No TTS provider configured." });
            }

            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var text = body.RootElement.TryGetProperty("text", out var textEl) ? textEl.GetString() ?? "" : "";
            if (string.IsNullOrWhiteSpace(text))
            {
                return Results.BadRequest(new { Error = "Text is required." });
            }

            var options = new TtsOptions
            {
                Voice = body.RootElement.TryGetProperty("voice", out var v) ? v.GetString() : null,
                Speed = body.RootElement.TryGetProperty("speed", out var s) && s.TryGetDouble(out var spd) ? spd : null,
            };

            try
            {
                var result = await tts.GenerateAsync(text, options, ct);
                var ext = Path.GetExtension(result.FilePath).ToLowerInvariant();
                var contentType = ext switch
                {
                    ".wav" => "audio/wav",
                    ".ogg" => "audio/ogg",
                    _ => "audio/mpeg",
                };
                return Results.File(result.FilePath, contentType);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 502, title: "TTS generation failed");
            }
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

    private static string SerializeMemberFile(string? model, string? provider, string systemPrompt)
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(model)) lines.Add($"model: {model}");
        if (!string.IsNullOrEmpty(provider)) lines.Add($"provider: {provider}");
        lines.Add("");
        lines.Add(systemPrompt.Trim());
        return string.Join("\n", lines);
    }
}
