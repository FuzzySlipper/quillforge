using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Delegates prose generation to the ProseWriter agent.
/// </summary>
public sealed class WriteProseHandler : IToolHandler
{
    private readonly ProseWriterAgent _proseWriter;
    private readonly string _activeWritingStyle;
    private readonly Func<string> _storyContextProvider;
    private readonly ILogger<WriteProseHandler> _logger;

    public WriteProseHandler(
        ProseWriterAgent proseWriter,
        string activeWritingStyle,
        Func<string> storyContextProvider,
        ILogger<WriteProseHandler> logger)
    {
        _proseWriter = proseWriter;
        _activeWritingStyle = activeWritingStyle;
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

        _logger.LogDebug("WriteProseHandler: generating prose for scene");

        var request = new ProseRequest
        {
            SceneDescription = sceneDescription,
            StoryContext = _storyContextProvider(),
            ToneNotes = toneNotes,
        };

        var result = await _proseWriter.WriteAsync(request, _activeWritingStyle, request.StoryContext, context, ct);
        return ToolResult.Ok(result.GeneratedText);
    }
}
