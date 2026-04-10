using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Lets the LLM look up system documentation about modes, tools, profiles, and workflows.
/// Available in all modes so the LLM can answer user questions about system behavior
/// from an authoritative source rather than guessing.
/// </summary>
public sealed class QueryDocsHandler : TypedToolHandler<QueryDocsArgs>
{
    private readonly IDocsService _docsService;
    private readonly ILogger<QueryDocsHandler> _logger;

    public QueryDocsHandler(IDocsService docsService, ILogger<QueryDocsHandler> logger)
    {
        _docsService = docsService;
        _logger = logger;
    }

    public override string Name => "query_docs";

    public override ToolDefinition Definition => new(Name,
        "Look up system documentation about QuillForge modes, tools, profiles, slash commands, and workflows. " +
        "Use this when the user asks about system behavior, mode boundaries, or how to use a feature — " +
        "consult the docs rather than guessing.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "topic": {
                        "type": "string",
                        "description": "Exact topic slug to retrieve (e.g. 'modes-overview', 'tools', 'profiles'). Returns the full document."
                    },
                    "query": {
                        "type": "string",
                        "description": "Free-text search query to find relevant sections across all docs. Use when you don't know the exact topic."
                    }
                }
            }
            """).RootElement);

    protected override async Task<ToolResult> HandleTypedAsync(QueryDocsArgs input, AgentContext context, CancellationToken ct = default)
    {
        // Topic lookup takes priority if provided
        if (!string.IsNullOrWhiteSpace(input.Topic))
        {
            _logger.LogDebug("QueryDocsHandler: looking up topic \"{Topic}\"", input.Topic);
            var entry = await _docsService.GetTopicAsync(input.Topic, ct);
            if (entry is null)
            {
                // Fall back to search if topic not found
                var topics = await _docsService.ListTopicsAsync(ct);
                var topicList = string.Join(", ", topics.Select(t => t.Slug));
                return ToolResult.Fail($"Topic '{input.Topic}' not found. Available topics: {topicList}");
            }

            return ToolResult.Ok(entry.Content);
        }

        if (!string.IsNullOrWhiteSpace(input.Query))
        {
            _logger.LogDebug("QueryDocsHandler: searching for \"{Query}\"", input.Query);
            var results = await _docsService.SearchAsync(input.Query, ct);
            if (results.Count == 0)
            {
                return ToolResult.Ok("No matching documentation found for that query.");
            }

            var output = new List<string>();
            foreach (var result in results)
            {
                output.Add($"## {result.Name} ({result.Slug})");
                foreach (var snippet in result.Snippets)
                {
                    output.Add(snippet);
                    output.Add("");
                }
            }

            return ToolResult.Ok(string.Join('\n', output));
        }

        // Neither provided — return topic listing
        _logger.LogDebug("QueryDocsHandler: listing all topics");
        var allTopics = await _docsService.ListTopicsAsync(ct);
        var listing = allTopics.Select(t => $"- **{t.Name}** (`{t.Slug}`): {t.Summary}");
        return ToolResult.Ok("Available documentation topics:\n" + string.Join('\n', listing));
    }
}

public sealed record QueryDocsArgs
{
    public string? Topic { get; init; }
    public string? Query { get; init; }
}
