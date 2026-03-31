using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents;

/// <summary>
/// The Forge Writer drafts a single chapter based on its brief.
/// Has access to query_lore for world-building verification.
/// Deliberately limited cognition: only sees its chapter brief + previous chapter tail.
/// </summary>
public sealed class ForgeWriterAgent
{
    private readonly ToolLoop _toolLoop;
    private readonly ILogger<ForgeWriterAgent> _logger;
    private readonly string _model;
    private readonly ForgeWriterBudget _budget;

    public ForgeWriterAgent(ToolLoop toolLoop, AppConfig appConfig, ILogger<ForgeWriterAgent> logger)
    {
        _toolLoop = toolLoop;
        _logger = logger;
        _model = appConfig.Models.ForgeWriter;
        _budget = appConfig.Agents.ForgeWriter;
    }

    /// <summary>
    /// Writes a single chapter based on a brief and continuity context.
    /// Receives the full previous chapter for detail-level continuity,
    /// not just a tail snippet.
    /// </summary>
    public async Task<ProseResult> WriteChapterAsync(
        string chapterBrief,
        string previousChapter,
        string writingStyle,
        IReadOnlyList<IToolHandler> tools,
        AgentContext context,
        string? customPrompt = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation("ForgeWriter starting chapter for session {SessionId}", context.SessionId);

        var systemPrompt = BuildSystemPrompt(writingStyle, customPrompt);

        var config = new AgentConfig
        {
            Model = _model,
            MaxTokens = _budget.MaxTokens,
            SystemPrompt = systemPrompt,
            MaxToolRounds = _budget.MaxToolRounds,
        };

        var userPrompt = string.IsNullOrWhiteSpace(previousChapter)
            ? $"## Chapter Brief\n\n{chapterBrief}"
            : $"## Previous Chapter\n\n{previousChapter}\n\n## Chapter Brief\n\n{chapterBrief}";

        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent(userPrompt)),
        };

        var response = await _toolLoop.RunAsync(config, tools, messages, context, ct);
        var text = response.Content.GetText();

        var loreQueries = messages
            .SelectMany(m => m.Content.Blocks.OfType<ToolUseBlock>())
            .Where(b => b.Name == "query_lore")
            .Select(b => b.Input.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "")
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
        2. Use the query_lore tool to verify character details before writing.
        3. Maintain continuity with the previous chapter's ending.
        4. Do NOT reveal future plot points or spoil later chapters.
        5. Write prose only — no metadata, no scene headings unless the brief specifies them.
        6. Aim for the target word count specified in the brief.
        """;
}
