using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;

namespace QuillForge.Providers.Tts;

public sealed class ElevenLabsTtsGenerator : ITtsGenerator
{
    private readonly HttpClient _httpClient;
    private readonly string _outputDir;
    private readonly string _defaultVoiceId;
    private readonly ILogger<ElevenLabsTtsGenerator> _logger;

    public ElevenLabsTtsGenerator(HttpClient httpClient, string apiKey, string outputDir, ILogger<ElevenLabsTtsGenerator> logger, string voiceId = "21m00Tcm4TlvDq8ikWAM")
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://api.elevenlabs.io/");
        _httpClient.DefaultRequestHeaders.Add("xi-api-key", apiKey);
        _outputDir = outputDir;
        _defaultVoiceId = voiceId;
        _logger = logger;
    }

    public async Task<TtsResult> GenerateAsync(string text, TtsOptions? options = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_outputDir);

        var voiceId = options?.Voice ?? _defaultVoiceId;

        var payload = new
        {
            text,
            model_id = "eleven_monolingual_v1",
            voice_settings = new { stability = 0.5, similarity_boost = 0.75 },
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
