using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents;

/// <summary>
/// The Forge Writer drafts a single chapter based on its brief.
/// Has access to query_lore for lore-document verification.
/// Deliberately limited cognition: only sees its chapter brief + previous chapter tail.
/// </summary>
public sealed class ForgeWriterAgent
{
    private readonly ToolLoop _toolLoop;
    private readonly ILogger<ForgeWriterAgent> _logger;
    private readonly AppConfig _appConfig;

    public ForgeWriterAgent(ToolLoop toolLoop, AppConfig appConfig, ILogger<ForgeWriterAgent> logger)
    {
        _toolLoop = toolLoop;
        _logger = logger;
        _appConfig = appConfig;
    }

    /// <summary>
    /// Writes a single chapter based on a brief and continuity context.
    /// Receives the story premise for grounding, plus the full previous chapter
    /// for detail-level continuity.
    /// </summary>
    public async Task<ProseResult> WriteChapterAsync(
        string chapterBrief,
        string previousChapter,
        string writingStyle,
        string premise,
        IReadOnlyList<IToolHandler> tools,
        AgentContext context,
        string? customPrompt = null,
        CancellationToken ct = default,
        Action<string>? progress = null)
    {
        var budget = _appConfig.Agents.ForgeWriter;
        var model = _appConfig.Models.ForgeWriter;
        _logger.LogInformation("ForgeWriter starting chapter for session {SessionId}", context.SessionId);
        progress?.Invoke($"ForgeWriter starting (model={model}, maxTokens={budget.MaxTokens})");

        var systemPrompt = BuildSystemPrompt(writingStyle, customPrompt);

        var config = new AgentConfig
        {
            Model = model,
            MaxTokens = budget.MaxTokens,
            SystemPrompt = systemPrompt,
            MaxToolRounds = budget.MaxToolRounds,
            AgentName = "forge-writer",
        };

        var sections = new List<string>();
        if (!string.IsNullOrWhiteSpace(premise))
            sections.Add($"## Story Premise\n\n{premise}");
        if (!string.IsNullOrWhiteSpace(previousChapter))
            sections.Add($"## Previous Chapter\n\n{previousChapter}");
        sections.Add($"## Chapter Brief\n\n{chapterBrief}");
        var userPrompt = string.Join("\n\n", sections);

        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent(userPrompt)),
        };

        var response = await _toolLoop.RunAsync(config, tools, messages, context, ct, progress);
        var text = response.Content.GetText();

        var loreQueries = messages
            .SelectMany(m => m.Content.Blocks.OfType<ToolUseBlock>())
            .Where(b => b.Name == "query_lore")
            .Select(b => b.Input.GetOptionalString("query") ?? "")
            .Where(q => !string.IsNullOrEmpty(q))
            .ToList();

        var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

        _logger.LogInformation(
            "ForgeWriter completed: {WordCount} words, {LoreQueries} lore queries",
            wordCount, loreQueries.Count);

        return new ProseResult
        {
            GeneratedText = text,
            LoreQueriesMade = loreQueries,
            WordCount = wordCount,
            Usage = response.Usage,
        };
    }

    private static string BuildSystemPrompt(string writingStyle, string? customPrompt)
    {
        var basePrompt = customPrompt ?? DefaultWriterPrompt;
        return $"{basePrompt}\n\n## Writing Style\n\n{writingStyle}";
    }

    internal const string DefaultWriterPrompt = """
        You are a skilled prose writer implementing a single chapter of a larger story.

        Rules:
        1. Follow the chapter brief faithfully — include all required plot beats.
        2. Use the query_lore tool to verify facts that should come from the active lore documents.
        3. Maintain continuity with the previous chapter's ending.
        4. Do NOT reveal future plot points or spoil later chapters.
        5. Write prose only — no metadata, no scene headings unless the brief specifies them.
        6. Aim for the target word count specified in the brief.
        """;
}
