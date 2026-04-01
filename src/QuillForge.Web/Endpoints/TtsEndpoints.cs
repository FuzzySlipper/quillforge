using System.Text.Json;
using QuillForge.Core.Services;

namespace QuillForge.Web.Endpoints;

public static class TtsEndpoints
{
    public static void MapTtsEndpoints(this WebApplication app)
    {
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
}
