using System.Text.Json;
using QuillForge.Core;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Web.Endpoints;

public static class CharacterCardEndpoints
{
    public static void MapCharacterCardEndpoints(this WebApplication app, string contentRoot)
    {
        // Portraits
        app.MapGet("/api/portraits", () =>
        {
            var dir = Path.Combine(contentRoot, ContentPaths.CharacterCards);
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
            var cardsDir = Path.Combine(contentRoot, ContentPaths.CharacterCards);
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
            AppConfig runtimeConfig,
            IAppConfigStore configStore,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var aiCharacter = root.TryGetProperty("aiCharacter", out var ai) ? ai.GetString() : null;
            var userCharacter = root.TryGetProperty("userCharacter", out var user) ? user.GetString() : null;

            var updatedConfig = await configStore.UpdateAsync(current => current with
            {
                Roleplay = current.Roleplay with
                {
                    AiCharacter = aiCharacter ?? current.Roleplay.AiCharacter,
                    UserCharacter = userCharacter ?? current.Roleplay.UserCharacter,
                }
            }, ct);
            AppConfigRuntimeSync.CopyFrom(runtimeConfig, updatedConfig);

            return Results.Ok(new
            {
                Status = "ok",
                AiCharacter = updatedConfig.Roleplay.AiCharacter,
                UserCharacter = updatedConfig.Roleplay.UserCharacter
            });
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
    }
}
