using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Delegates prose generation to the ProseWriter agent.
/// Resolves the active writing style from AgentContext at call time,
/// not from a value captured at construction.
/// </summary>
public sealed class WriteProseHandler : IToolHandler
{
    private readonly ProseWriterAgent _proseWriter;
    private readonly Func<string> _storyContextProvider;
    private readonly ILogger<WriteProseHandler> _logger;

    public WriteProseHandler(
        ProseWriterAgent proseWriter,
        Func<string> storyContextProvider,
        ILogger<WriteProseHandler> logger)
    {
        _proseWriter = proseWriter;
        _storyContextProvider = storyContextProvider;
        _logger = logger;
    }

    public string Name => "write_prose";

    public ToolDefinition Definition => new(Name,
        "Generate prose for a scene. Returns the generated text.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "scene_description": {
                        "type": "string",
                        "description": "Detailed description of the scene to write"
                    },
                    "tone_notes": {
                        "type": "string",
                        "description": "Optional tone/mood guidance for this scene"
                    }
                },
                "required": ["scene_description"]
            }
            """).RootElement);

    public async Task<ToolResult> HandleAsync(JsonElement input, AgentContext context, CancellationToken ct = default)
    {
        var sceneDescription = input.GetProperty("scene_description").GetString();
        if (string.IsNullOrWhiteSpace(sceneDescription))
        {
            return ToolResult.Fail("scene_description is required.");
        }

        var toneNotes = input.TryGetProperty("tone_notes", out var tn) ? tn.GetString() : null;

        _logger.LogDebug("WriteProseHandler: generating prose with style \"{Style}\"", context.ActiveWritingStyle);

        var request = new ProseRequest
        {
            SceneDescription = sceneDescription,
            StoryContext = _storyContextProvider(),
            ToneNotes = toneNotes,
        };

        var result = await _proseWriter.WriteAsync(request, context.ActiveWritingStyle, request.StoryContext, context, ct);
        return ToolResult.Ok(result.GeneratedText);
    }
}
