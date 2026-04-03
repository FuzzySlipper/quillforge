using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Delegates prose generation to the ProseWriter agent.
/// Resolves the active writing style and story path from prepared interactive
/// session context at call time, not from values captured at construction.
/// </summary>
public sealed class WriteProseHandler : IToolHandler
{
    private readonly ProseWriterAgent _proseWriter;
    private readonly IInteractiveSessionContextService _sessionContextService;
    private readonly IStoryStateService _storyState;
    private readonly ILogger<WriteProseHandler> _logger;

    public WriteProseHandler(
        ProseWriterAgent proseWriter,
        IInteractiveSessionContextService sessionContextService,
        IStoryStateService storyState,
        ILogger<WriteProseHandler> logger)
    {
        _proseWriter = proseWriter;
        _sessionContextService = sessionContextService;
        _storyState = storyState;
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

        var sessionContext = context.SessionContext ?? await _sessionContextService.LoadAsync(context.SessionId, ct);
        var storyStateData = await _storyState.LoadAsync(sessionContext.StoryStatePath, ct);
        var storyContext = storyStateData.Count > 0 ? JsonSerializer.Serialize(storyStateData) : "";

        _logger.LogDebug("WriteProseHandler: generating prose with style \"{Style}\" for project \"{Project}\"",
            context.ActiveWritingStyle, sessionContext.ProjectName);

        var request = new ProseRequest
        {
            SceneDescription = sceneDescription,
            StoryContext = storyContext,
            ToneNotes = toneNotes,
        };

        var result = await _proseWriter.WriteAsync(request, context.ActiveWritingStyle, storyContext, context, ct);
        return ToolResult.Ok(result.GeneratedText);
    }
}
