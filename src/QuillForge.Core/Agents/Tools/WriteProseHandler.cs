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
public sealed class WriteProseHandler : TypedToolHandler<WriteProseArgs>
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

    public override string Name => "write_prose";

    public override ToolDefinition Definition => new(Name,
        "Generate prose from a grounded scene brief. This is the renderer used by higher-level flows such as the Narrative Director.",
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

    protected override async Task<ToolResult> HandleTypedAsync(WriteProseArgs input, AgentContext context, CancellationToken ct = default)
    {
        var sceneDescription = input.SceneDescription;
        if (string.IsNullOrWhiteSpace(sceneDescription))
        {
            return ToolResult.Fail("scene_description is required.");
        }

        var sessionContext = context.SessionContext ?? await _sessionContextService.LoadAsync(context.SessionId, ct);
        var storyStateData = await _storyState.LoadAsync(sessionContext.StoryStatePath, ct);
        var storyContext = BuildStoryContext(sessionContext, storyStateData);

        _logger.LogDebug("WriteProseHandler: generating prose with style \"{Style}\" for project \"{Project}\"",
            context.ActiveWritingStyle, sessionContext.ProjectName);

        var request = new ProseRequest
        {
            SceneDescription = sceneDescription,
            StoryContext = storyContext,
            ToneNotes = input.ToneNotes,
        };

        try
        {
            var result = await _proseWriter.WriteAsync(request, context.ActiveWritingStyle, storyContext, context, ct);
            return ToolResult.Ok(result.GeneratedText);
        }
        catch (CanonPrerequisiteException ex)
        {
            _logger.LogWarning(
                ex,
                "WriteProseHandler rejected prose generation for session {SessionId}",
                context.SessionId);
            return ToolResult.Fail(ex.Message);
        }
    }

    private static string BuildStoryContext(
        InteractiveSessionContext sessionContext,
        IReadOnlyDictionary<string, object> storyStateData)
    {
        var sections = new List<string>();

        AddSection(sections, "Character Context", sessionContext.CharacterSection);
        AddSection(sections, "User Character Context", sessionContext.UserCharacterSection);
        AddSection(sections, "Current Story State", sessionContext.StoryStateSummary);

        if (string.IsNullOrWhiteSpace(sessionContext.StoryStateSummary) && storyStateData.Count > 0)
        {
            AddSection(sections, "Current Story State", JsonSerializer.Serialize(storyStateData));
        }

        AddSection(sections, "Sticky Session Canon", sessionContext.StickySessionCanon);
        AddSection(sections, "Director Notes From Prior Turns", sessionContext.DirectorNotes);
        AddSection(sections, "Recent Session Conversation", sessionContext.RecentConversationSummary);
        AddSection(sections, "Active Plot Content", sessionContext.ActivePlotContent);
        AddSection(sections, "Plot Progress In This Session", sessionContext.PlotProgressSummary);
        AddSection(sections, "Recent File Context", sessionContext.FileContext);

        return string.Join("\n\n", sections);
    }

    private static void AddSection(List<string> sections, string title, string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return;
        }

        sections.Add($"## {title}\n\n{content}");
    }
}

public sealed record WriteProseArgs
{
    public string SceneDescription { get; init; } = "";
    public string? ToneNotes { get; init; }
}
