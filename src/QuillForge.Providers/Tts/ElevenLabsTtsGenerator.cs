using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Providers.Tts;

public sealed class ElevenLabsTtsGenerator : ITtsGenerator
{
    private readonly HttpClient _httpClient;
    private readonly string _outputDir;
    private readonly ILogger<ElevenLabsTtsGenerator> _logger;
    private readonly ElevenLabsConfig _config;

    public ElevenLabsTtsGenerator(HttpClient httpClient, string apiKey, string outputDir, ElevenLabsConfig config, ILogger<ElevenLabsTtsGenerator> logger)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://api.elevenlabs.io/");
        _httpClient.DefaultRequestHeaders.Add("xi-api-key", apiKey);
        _outputDir = outputDir;
        _logger = logger;
        _config = config;
    }

    public async Task<TtsResult> GenerateAsync(string text, TtsOptions? options = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_outputDir);

        var voiceId = options?.Voice ?? _config.VoiceId;

        var payload = new
        {
            text,
            model_id = _config.ModelId,
            voice_settings = new { stability = _config.Stability, similarity_boost = _config.SimilarityBoost },
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        _logger.LogDebug("ElevenLabs TTS: generating {Length} chars, voice={Voice}", text.Length, voiceId);

        var response = await _httpClient.PostAsync($"v1/text-to-speech/{voiceId}", content, ct);
        response.EnsureSuccessStatusCode();

        var shortId = Guid.NewGuid().ToString("N")[..8];
        var fileName = $"tts-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{shortId}.mp3";
        var filePath = Path.Combine(_outputDir, fileName);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = File.Create(filePath);
        await stream.CopyToAsync(fileStream, ct);

        var estimatedSeconds = text.Length / 5.0 / 150.0 * 60.0;

        _logger.LogInformation("ElevenLabs TTS: saved to {Path}", filePath);
        return new TtsResult(filePath, TimeSpan.FromSeconds(estimatedSeconds));
    }
}
