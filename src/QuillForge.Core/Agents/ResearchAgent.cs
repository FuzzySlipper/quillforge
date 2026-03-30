using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents;

/// <summary>
/// A multi-turn research sub-agent. Uses ToolLoop with web_search and file tools
/// to investigate a topic, refine searches, and write findings to markdown.
/// </summary>
public sealed partial class ResearchAgent
{
    private readonly ToolLoop _toolLoop;
    private readonly IReadOnlyList<IToolHandler> _tools;
    private readonly ILogger<ResearchAgent> _logger;
    private readonly string _model;
    private readonly ResearchBudget _budget;

    public ResearchAgent(
        ToolLoop toolLoop,
        IReadOnlyList<IToolHandler> tools,
        AppConfig appConfig,
        ILogger<ResearchAgent> logger)
    {
        _toolLoop = toolLoop;
        _tools = tools;
        _logger = logger;
        _model = appConfig.Models.Research;
        _budget = appConfig.Agents.Research;
    }

    public async Task<ResearchAgentResult> ResearchAsync(
        string topic,
        string? focus,
        string project,
        AgentContext context,
        CancellationToken ct = default)
    {
        var slug = Slugify(topic);
        var filePath = $"research/{project}/{slug}.md";

        _logger.LogInformation("ResearchAgent starting: topic=\"{Topic}\", project={Project}", topic, project);

        var systemPrompt = BuildSystemPrompt(topic, focus, filePath);

        var config = new AgentConfig
        {
            Model = _model,
            MaxTokens = _budget.MaxTokens,
            SystemPrompt = systemPrompt,
            MaxToolRounds = _budget.MaxToolRounds,
            Temperature = _budget.Temperature,
        };

        var userPrompt = string.IsNullOrWhiteSpace(focus)
            ? $"Research the following topic thoroughly: {topic}"
            : $"Research the following topic: {topic}\n\nSpecific focus: {focus}";

        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent(userPrompt)),
        };

        try
        {
            var response = await _toolLoop.RunAsync(config, _tools, messages, context, ct);
            var text = response.Content.GetText();

            // Extract sources from the agent's response (lines starting with "- http" or "- [")
            var sources = ExtractSources(text);

            _logger.LogInformation(
                "ResearchAgent completed: topic=\"{Topic}\", {Rounds} rounds, {Sources} sources",
                topic, response.ToolRoundsUsed, sources.Count);

            return new ResearchAgentResult
            {
                Topic = topic,
                Summary = text,
                Sources = sources,
                FilePath = filePath,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "ResearchAgent failed for topic \"{Topic}\"", topic);
            return new ResearchAgentResult
            {
                Topic = topic,
                Summary = "",
                Sources = [],
                FilePath = filePath,
                Error = ex.Message,
            };
        }
    }

    private static string BuildSystemPrompt(string topic, string? focus, string filePath)
    {
        var focusSection = string.IsNullOrWhiteSpace(focus) ? "" : $"\nSpecific focus area: {focus}\n";

        return $"""
            You are a thorough research agent. Your job is to investigate a topic using web searches,
            collect findings, and produce a well-organized research brief.

            ## Research Process

            1. Start with a broad web search on the topic
            2. Read and analyze the search results
            3. Identify gaps or areas that need deeper investigation
            4. Perform follow-up searches with refined queries
            5. Once you have enough information, write your findings to `{filePath}` using the write_file tool
            6. End with a concise summary of your findings

            ## Output Format

            When writing findings to file, use this markdown structure:
            - Title (# heading)
            - Key findings (## sections)
            - Sources list at the end

            When responding after writing, provide:
            - A brief summary of what you found
            - List of source URLs

            ## Topic
            {topic}{focusSection}

            ## Important Rules
            - Always use web_search before drawing conclusions
            - Perform at least 2 searches to get diverse perspectives
            - Write findings to the file path specified above
            - Be factual and cite sources
            """;
    }

    private static List<string> ExtractSources(string text)
    {
        var sources = new List<string>();
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("- http", StringComparison.OrdinalIgnoreCase))
            {
                sources.Add(trimmed[2..].Trim());
            }
            else if (UrlPattern().IsMatch(trimmed))
            {
                var match = UrlPattern().Match(trimmed);
                sources.Add(match.Value);
            }
        }
        return sources;
    }

    internal static string Slugify(string text)
    {
        var slug = text.ToLowerInvariant().Trim();
        slug = SlugCleanPattern().Replace(slug, "-");
        slug = SlugCollapsePattern().Replace(slug, "-");
        return slug.Trim('-');
    }

    [GeneratedRegex(@"[^a-z0-9\-]")]
    private static partial Regex SlugCleanPattern();

    [GeneratedRegex(@"-{2,}")]
    private static partial Regex SlugCollapsePattern();

    [GeneratedRegex(@"https?://\S+")]
    private static partial Regex UrlPattern();
}
