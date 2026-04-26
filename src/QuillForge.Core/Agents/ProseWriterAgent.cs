using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents;

/// <summary>
/// The Prose Writer agent generates scenes and prose. It has a query_lore tool that
/// delegates to the Librarian for lore-document details during writing.
/// </summary>
public sealed class ProseWriterAgent
{
    private readonly ToolLoop _toolLoop;
    private readonly IToolHandler _queryLoreHandler;
    private readonly IToolHandler? _queryContextHandler;
    private readonly CanonPrerequisiteGuard _canonGuard;
    private readonly ILogger<ProseWriterAgent> _logger;
    private readonly string _model;
    private readonly ProseWriterBudget _budget;

    public ProseWriterAgent(
        ToolLoop toolLoop,
        IToolHandler queryLoreHandler,
        CanonPrerequisiteGuard canonGuard,
        AppConfig appConfig,
        ILogger<ProseWriterAgent> logger,
        IToolHandler? queryContextHandler = null)
    {
        _toolLoop = toolLoop;
        _queryLoreHandler = queryLoreHandler;
        _queryContextHandler = queryContextHandler;
        _canonGuard = canonGuard;
        _logger = logger;
        _model = appConfig.Models.ProseWriter;
        _budget = appConfig.Agents.ProseWriter;
    }

    /// <summary>
    /// Generates prose for a scene, optionally querying lore during generation.
    /// </summary>
    public async Task<ProseResult> WriteAsync(
        ProseRequest request,
        string writingStyleName,
        string storyContext,
        AgentContext context,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "ProseWriter starting: scene=\"{Scene}\", style=\"{Style}\"",
            Truncate(request.SceneDescription, 80), writingStyleName);

        await _canonGuard.EnsureQueryableLoreAvailableAsync(context, "render grounded prose", ct);
        var writingStyle = await _canonGuard.RequireWritingStyleAsync(
            writingStyleName,
            context,
            "render grounded prose",
            ct);

        var systemPrompt = BuildSystemPrompt(writingStyle, storyContext, request.ToneNotes);

        var config = new AgentConfig
        {
            Model = _model,
            MaxTokens = _budget.MaxTokens,
            SystemPrompt = systemPrompt,
            MaxToolRounds = _budget.MaxToolRounds,
            AgentName = "prose-writer",
        };

        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent(request.SceneDescription)),
        };

        var tools = new List<IToolHandler>();
        if (_queryContextHandler is not null)
        {
            tools.Add(_queryContextHandler);
        }

        tools.Add(_queryLoreHandler);
        var response = await _toolLoop.RunAsync(config, tools, messages, context, ct);
        var generatedText = response.Content.GetText();

        // Count lore queries made by extracting from messages
        var loreQueries = messages
            .SelectMany(m => m.Content.Blocks.OfType<ToolUseBlock>())
            .Where(b => b.Name == "query_lore")
            .Select(b => b.Input.GetOptionalString("query") ?? "")
            .Where(q => !string.IsNullOrEmpty(q))
            .ToList();

        var wordCount = generatedText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        _logger.LogInformation(
            "ProseWriter completed: {WordCount} words, {LoreQueries} lore queries, {Rounds} tool rounds",
            wordCount, loreQueries.Count, response.ToolRoundsUsed);

        return new ProseResult
        {
            GeneratedText = generatedText,
            LoreQueriesMade = loreQueries,
            WordCount = wordCount,
        };
    }

    internal static string BuildSystemPrompt(string writingStyle, string storyContext, string? toneNotes)
    {
        var toneSection = string.IsNullOrWhiteSpace(toneNotes)
            ? ""
            : $"\n\n## Tone Notes\n\n{toneNotes}";

        var loreRules = """
            1. Treat the Story So Far, Character Context, session canon, director notes, and plot/story
               state as established context for this request.
            2. Use query_context for broad canon checks across supplied character context, session canon,
               plot/story state, recent conversation, and lore-document snippets.
            3. Use query_lore only for facts that should come from the active lore documents: lore-documented
               characters, locations, events, factions, history, rules, and world-building.
            4. Do not use query_lore to verify facts already supplied in Character Context, Sticky Session Canon,
               Director Notes, the current plot, or the current scene brief.
            5. Stay faithful to established lore and context. Do not contradict existing world-building.
            6. Maintain consistency with the story so far.
            7. Write prose only — no metadata, no commentary, no scene headings unless requested.
            8. If a required canon detail cannot be confirmed from the supplied context or lore documents,
               stop and disclose that gap rather than inventing it.
            """;

        return $"""
            You are a skilled prose writer. Your job is to write compelling, immersive fiction that
            stays faithful to the established world and characters.

            Rules:
            {loreRules}
            ## Writing Style

            {writingStyle}

            ## Story So Far

            {storyContext}{toneSection}
            """;
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}
