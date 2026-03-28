using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Providers.ImageGen;

public sealed class ComfyUiImageGenerator : IImageGenerator
{
    private readonly HttpClient _httpClient;
    private readonly string _outputDir;
    private readonly string _baseUrl;
    private readonly ILogger<ComfyUiImageGenerator> _logger;
    private readonly ComfyUiConfig _config;

    public ComfyUiImageGenerator(HttpClient httpClient, string baseUrl, string outputDir, ComfyUiConfig config, ILogger<ComfyUiImageGenerator> logger)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _outputDir = outputDir;
        _logger = logger;
        _config = config;
    }

    public async Task<ImageResult> GenerateAsync(string prompt, ImageOptions? options = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(_outputDir);

        var width = options?.Width ?? _config.Width;
        var height = options?.Height ?? _config.Height;

        // Build a minimal ComfyUI workflow API payload
        var workflow = BuildWorkflow(prompt, width, height);
        var payload = JsonSerializer.Serialize(new { prompt = workflow });
        var content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json");

        _logger.LogDebug("ComfyUI: queuing prompt, size={Width}x{Height}", width, height);

        // Queue the prompt
        var queueResponse = await _httpClient.PostAsync($"{_baseUrl}/prompt", content, ct);
        queueResponse.EnsureSuccessStatusCode();

        var queueJson = await queueResponse.Content.ReadAsStringAsync(ct);
        using var queueDoc = JsonDocument.Parse(queueJson);
        var promptId = queueDoc.RootElement.GetProperty("prompt_id").GetString()!;

        // Poll for completion
        string? outputFileName = null;
        string? subfolder = null;
        for (int i = 0; i < _config.MaxPollIterations; i++)
        {
            await Task.Delay(_config.PollIntervalMs, ct);

            var historyResponse = await _httpClient.GetAsync($"{_baseUrl}/history/{promptId}", ct);
            if (!historyResponse.IsSuccessStatusCode) continue;

            var historyJson = await historyResponse.Content.ReadAsStringAsync(ct);
            using var historyDoc = JsonDocument.Parse(historyJson);

            if (historyDoc.RootElement.TryGetProperty(promptId, out var entry)
                && entry.TryGetProperty("outputs", out var outputs))
            {
                // Find the first image output
                foreach (var node in outputs.EnumerateObject())
                {
                    if (node.Value.TryGetProperty("images", out var images) && images.GetArrayLength() > 0)
                    {
                        outputFileName = images[0].GetProperty("filename").GetString();
                        subfolder = images[0].TryGetProperty("subfolder", out var sf) ? sf.GetString() ?? "" : "";
                        break;
                    }
                }
                if (outputFileName is not null) break;
            }
        }

        if (outputFileName is null)
            throw new TimeoutException("ComfyUI did not produce output within timeout.");

        // Download the output image
        var viewUrl = $"{_baseUrl}/view?filename={Uri.EscapeDataString(outputFileName)}&subfolder={Uri.EscapeDataString(subfolder ?? "")}";
        var imageBytes = await _httpClient.GetByteArrayAsync(viewUrl, ct);

        var localFileName = $"img-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..8]}.png";
        var filePath = Path.Combine(_outputDir, localFileName);
        await File.WriteAllBytesAsync(filePath, imageBytes, ct);

        _logger.LogInformation("ComfyUI: saved to {Path}", filePath);
        return new ImageResult(filePath, width, height);
    }

    private JsonElement BuildWorkflow(string prompt, int width, int height)
    {
        // Minimal txt2img workflow with configurable parameters
        var workflowJson = $$"""
        {
            "1": {
                "class_type": "CheckpointLoaderSimple",
                "inputs": { "ckpt_name": "{{_config.Checkpoint}}" }
            },
            "2": {
                "class_type": "CLIPTextEncode",
                "inputs": { "text": "{{prompt}}", "clip": ["1", 1] }
            },
            "3": {
                "class_type": "CLIPTextEncode",
                "inputs": { "text": "", "clip": ["1", 1] }
            },
            "4": {
                "class_type": "EmptyLatentImage",
                "inputs": { "width": {{width}}, "height": {{height}}, "batch_size": 1 }
            },
            "5": {
                "class_type": "KSampler",
                "inputs": {
                    "model": ["1", 0], "positive": ["2", 0], "negative": ["3", 0],
                    "latent_image": ["4", 0], "seed": {{Random.Shared.NextInt64()}},
                    "steps": {{_config.Steps}}, "cfg": {{_config.CfgScale}}, "sampler_name": "{{_config.Sampler}}",
                    "scheduler": "{{_config.Scheduler}}", "denoise": 1.0
                }
            },
            "6": {
                "class_type": "VAEDecode",
                "inputs": { "samples": ["5", 0], "vae": ["1", 2] }
            },
            "7": {
                "class_type": "SaveImage",
                "inputs": { "images": ["6", 0], "filename_prefix": "quillforge" }
            }
        }
        """;
        return JsonDocument.Parse(workflowJson).RootElement.Clone();
    }
}
