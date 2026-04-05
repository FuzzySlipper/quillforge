using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Generates an image from a text prompt.
/// </summary>
public sealed class GenerateImageHandler : IToolHandler
{
    private readonly IImageGenerator _imageGenerator;
    private readonly ILogger<GenerateImageHandler> _logger;

    public GenerateImageHandler(IImageGenerator imageGenerator, ILogger<GenerateImageHandler> logger)
    {
        _imageGenerator = imageGenerator;
        _logger = logger;
    }

    public string Name => "generate_image";

    public ToolDefinition Definition => new(Name,
        "Generate an image from a text description.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "prompt": { "type": "string", "description": "Text description of the image to generate" },
                    "width": { "type": "integer", "description": "Optional width in pixels" },
                    "height": { "type": "integer", "description": "Optional height in pixels" }
                },
                "required": ["prompt"]
            }
            """).RootElement);

    public async Task<ToolResult> HandleAsync(ToolInput input, AgentContext context, CancellationToken ct = default)
    {
        var prompt = input.GetRequiredString("prompt");
        _logger.LogDebug("GenerateImageHandler: generating image for prompt");

        var options = new ImageOptions
        {
            Width = input.GetOptionalInt("width"),
            Height = input.GetOptionalInt("height"),
        };

        var result = await _imageGenerator.GenerateAsync(prompt, options, ct);
        return ToolResult.Ok(JsonSerializer.Serialize(new { path = result.FilePath, width = result.Width, height = result.Height }));
    }
}
