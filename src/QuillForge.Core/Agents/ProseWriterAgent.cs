using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents;

/// <summary>
/// The Prose Writer agent generates scenes and prose. It has a query_lore tool that
/// delegates to the Librarian for world-building details during writing.
/// </summary>
public sealed class ProseWriterAgent
{
    private readonly ToolLoop _toolLoop;
    private readonly IToolHandler _queryLoreHandler;
    private readonly IWritingStyleStore _writingStyleStore;
    private readonly ILogger<ProseWriterAgent> _logger;
    private readonly string _model;

    public ProseWriterAgent(
        ToolLoop toolLoop,
        IToolHandler queryLoreHandler,
        IWritingStyleStore writingStyleStore,
        AppConfig appConfig,
        ILogger<ProseWriterAgent> logger)
    {
        _toolLoop = toolLoop;
        _queryLoreHandler = queryLoreHandler;
        _writingStyleStore = writingStyleStore;
        _logger = logger;
        _model = appConfig.Models.ProseWriter;
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

        var writingStyle = await _writingStyleStore.LoadAsync(writingStyleName, ct);
        var systemPrompt = BuildSystemPrompt(writingStyle, storyContext, request.ToneNotes);

        var config = new AgentConfig
        {
            Model = _model,
            MaxTokens = 8192,
            SystemPrompt = systemPrompt,
            MaxToolRounds = 10,
        };

        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent(request.SceneDescription)),
        };

        var response = await _toolLoop.RunAsync(config, [_queryLoreHandler], messages, context, ct);
        var generatedText = response.Content.GetText();

        // Count lore queries made by extracting from messages
        var loreQueries = messages
            .SelectMany(m => m.Content.Blocks.OfType<ToolUseBlock>())
            .Where(b => b.Name == "query_lore")
            .Select(b => b.Input.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "")
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

        return $"""
            You are a skilled prose writer. Your job is to write compelling, immersive fiction that
            stays faithful to the established world and characters.

            Rules:
            1. Before writing, use the query_lore tool to verify character details, locations, and
               world-building facts relevant to the scene.
            2. Stay faithful to established lore. Do not contradict existing world-building.
            3. Maintain consistency with the story so far.
            4. Write prose only — no metadata, no commentary, no scene headings unless requested.
            5. If the scene requires details not in the lore, write around them naturally.

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
