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
        // If a structured scene brief is provided, use it as the primary source
        // for scene description, tone notes, and roleplay protocol directives.
        var sceneDescription = input.SceneBrief?.SceneDescription ?? input.SceneDescription;
        var toneNotes = input.SceneBrief?.ToneNotes ?? input.ToneNotes;

        // Build story context enriched with structured brief directives, if available
        var sessionContext = context.SessionContext ?? await _sessionContextService.LoadAsync(context.SessionId, ct);
        var storyStateData = await _storyState.LoadAsync(sessionContext.StoryStatePath, ct);
        var storyContext = BuildStoryContext(sessionContext, storyStateData, input.SceneBrief);

        _logger.LogDebug("WriteProseHandler: generating prose with style \"{Style}\" for project \"{Project}\"",
            context.ActiveWritingStyle, sessionContext.ProjectName);

        if (string.IsNullOrWhiteSpace(sceneDescription))
        {
            return ToolResult.Fail("scene_description or scene_brief.scene_description is required.");
        }

        var request = new ProseRequest
        {
            SceneDescription = sceneDescription,
            StoryContext = storyContext,
            ToneNotes = toneNotes,
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
        IReadOnlyDictionary<string, object> storyStateData,
        StructuredSceneBrief? sceneBrief = null)
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

        // Add structured roleplay protocol section if scene brief provides directives
        if (sceneBrief is not null)
        {
            AddRoleplayDirectivesSection(sections, sceneBrief);
        }

        return string.Join("\n\n", sections);
    }

    /// <summary>
    /// Add a structured roleplay knowledge directives section to the story context,
    /// telling the prose writer how to handle active-subject vs background knowledge.
    /// This implements the active-subject/applicability/allowed-use protocol without
    /// exposing raw lore frontmatter/metadata to the normal editing UI.
    /// </summary>
    private static void AddRoleplayDirectivesSection(
        List<string> sections,
        StructuredSceneBrief brief)
    {
        var directiveLines = new List<string>();

        if (!string.IsNullOrWhiteSpace(brief.ActiveSubject))
        {
            directiveLines.Add($"- **Active Character**: {brief.ActiveSubject}");
        }

        if (brief.ExcludedSubjects is { Count: > 0 })
        {
            directiveLines.Add($"- **Excluded Characters**: {string.Join(", ", brief.ExcludedSubjects)}");
        }

        if (brief.KnowledgePackets is { Count: > 0 })
        {
            var inlineSubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var contextSubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var excludedSubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var packet in brief.KnowledgePackets)
            {
                foreach (var evidence in packet.Evidence)
                {
                    switch (evidence.AllowedUse)
                    {
                        case AllowedUse.AssertAsFact:
                            if (evidence.SubjectRef is not null)
                                inlineSubjects.Add(evidence.SubjectRef.Name);
                            break;
                        case AllowedUse.BackgroundOnly:
                            if (evidence.SubjectRef is not null)
                                contextSubjects.Add(evidence.SubjectRef.Name);
                            break;
                        case AllowedUse.RejectForActiveSubject:
                            if (evidence.SubjectRef is not null)
                                excludedSubjects.Add(evidence.SubjectRef.Name);
                            break;
                    }
                }
            }

            if (inlineSubjects.Count > 0)
                directiveLines.Add($"- **Inline Knowledge (may use as character facts)**: {string.Join(", ", inlineSubjects)}");

            if (contextSubjects.Count > 0)
                directiveLines.Add($"- **Background Knowledge (context only, not inline facts)**: {string.Join(", ", contextSubjects)}");

            if (excludedSubjects.Count > 0)
                directiveLines.Add($"- **Excluded Knowledge (must not use)**: {string.Join(", ", excludedSubjects)}");
        }

        if (brief.Directives is { Count: > 0 })
        {
            directiveLines.Add("- **Knowledge Directives**:");
            foreach (var d in brief.Directives)
            {
                directiveLines.Add($"  - For {d.ForSubject ?? "all"}: use={d.AllowedUse}, scope={d.KnowledgeScope}{(d.Reason is not null ? $", reason={d.Reason}" : "")}");
            }
        }

        // Core protocol rule: off-subject/background-only facts must not be grafted into
        // active-character narration unless the active subject is explicitly being discussed
        // in a context where that knowledge is relevant.
        directiveLines.Add("");
        directiveLines.Add("**Roleplay Knowledge Protocol**:");
        directiveLines.Add("- Knowledge classified as 'Background/Context' is available for general narration but MUST NOT be presented as inline facts or unique personal details of the active character.");
        directiveLines.Add("- Knowledge classified as 'Excluded' for a subject MUST NOT appear in narration about that subject.");
        directiveLines.Add("- If shared/background body-tech or world knowledge is the only available context, make it clear it is common/shared, not unique to the active character.");

        if (directiveLines.Count > 0)
        {
            var section = "## Roleplay Knowledge Directives\n\n" + string.Join("\n", directiveLines);
            sections.Add(section);
        }
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

    /// <summary>
    /// Optional structured scene brief for roleplay protocol consumers.
    /// Carries active-subject context, directives, and knowledge references
    /// so the prose writer can obey allowed-use boundaries.
    /// When set, scene_description and tone_notes are derived from this brief.
    /// </summary>
    public StructuredSceneBrief? SceneBrief { get; init; }
}
