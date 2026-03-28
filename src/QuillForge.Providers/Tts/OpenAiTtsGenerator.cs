using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Providers.Tts;

public sealed class OpenAiTtsGenerator : ITtsGenerator
{
    private readonly HttpClient _httpClient;
    private readonly string _outputDir;
    private readonly ILogger<OpenAiTtsGenerator> _logger;
    private readonly OpenAiTtsConfig _config;

    public OpenAiTtsGenerator(HttpClient httpClient, string apiKey, string outputDir, OpenAiTtsConfig config, ILogger<OpenAiTtsGenerator> logger)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://api.openai.com/");
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        _outputDir = outputDir;
        _logger = logger;
        _config = config;
    }

    public async Task<TtsResult> GenerateAsync(string text, TtsOptions? options = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_outputDir);

        var voice = options?.Voice ?? _config.Voice;
        var speed = options?.Speed ?? _config.Speed;

        var payload = new
        {
            model = _config.Model,
            input = text,
            voice,
            speed,
            response_format = "mp3",
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        _logger.LogDebug("OpenAI TTS: generating {Length} chars, voice={Voice}", text.Length, voice);

        var response = await _httpClient.PostAsync("v1/audio/speech", content, ct);
        response.EnsureSuccessStatusCode();

        var shortId = Guid.NewGuid().ToString("N")[..8];
        var fileName = $"tts-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{shortId}.mp3";
        var filePath = Path.Combine(_outputDir, fileName);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = File.Create(filePath);
        await stream.CopyToAsync(fileStream, ct);

        // Rough duration estimate: ~150 words/min for speech, ~5 chars/word
        var estimatedSeconds = text.Length / 5.0 / 150.0 * 60.0;

        _logger.LogInformation("OpenAI TTS: saved to {Path}", filePath);
        return new TtsResult(filePath, TimeSpan.FromSeconds(estimatedSeconds));
    }
}
