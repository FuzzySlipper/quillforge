using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;

namespace QuillForge.Providers.ImageGen;

public sealed class OpenAiImageGenerator : IImageGenerator
{
    private readonly HttpClient _httpClient;
    private readonly string _outputDir;
    private readonly string _defaultModel;
    private readonly ILogger<OpenAiImageGenerator> _logger;

    public OpenAiImageGenerator(HttpClient httpClient, string apiKey, string outputDir, ILogger<OpenAiImageGenerator> logger, string model = "dall-e-3")
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://api.openai.com/");
        _httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        _outputDir = outputDir;
        _defaultModel = model;
        _logger = logger;
    }

    public async Task<ImageResult> GenerateAsync(string prompt, ImageOptions? options = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_outputDir);

        var width = options?.Width ?? 1024;
        var height = options?.Height ?? 1024;
        var size = $"{width}x{height}";

        // DALL-E 3 only supports specific sizes
        if (_defaultModel == "dall-e-3")
        {
            size = (width, height) switch
            {
                ( >= 1792, _) => "1792x1024",
                (_, >= 1792) => "1024x1792",
                _ => "1024x1024",
            };
        }

        var payload = new
        {
            model = _defaultModel,
            prompt,
            n = 1,
            size,
            quality = "standard",
            response_format = "url",
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        _logger.LogDebug("OpenAI ImageGen: generating, size={Size}", size);

        var response = await _httpClient.PostAsync("v1/images/generations", content, ct);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(ct);
        using var doc = System.Text.Json.JsonDocument.Parse(responseJson);
        var imageUrl = doc.RootElement.GetProperty("data")[0].GetProperty("url").GetString()!;

        // Download the image
        var imageBytes = await _httpClient.GetByteArrayAsync(imageUrl, ct);
        var fileName = $"img-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.png";
        var filePath = Path.Combine(_outputDir, fileName);
        await File.WriteAllBytesAsync(filePath, imageBytes, ct);

        // Parse actual size from the size string
        var parts = size.Split('x');
        var actualWidth = int.Parse(parts[0]);
        var actualHeight = int.Parse(parts[1]);

        _logger.LogInformation("OpenAI ImageGen: saved to {Path}", filePath);
        return new ImageResult(filePath, actualWidth, actualHeight);
    }
}
