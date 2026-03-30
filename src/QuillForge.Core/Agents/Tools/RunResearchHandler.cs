using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

public sealed class RunResearchHandler : IToolHandler
{
    private readonly ResearchPool _pool;
    private readonly ILogger<RunResearchHandler> _logger;
    private readonly int _timeoutMinutes;

    public RunResearchHandler(ResearchPool pool, AppConfig appConfig, ILogger<RunResearchHandler> logger)
    {
        _pool = pool;
        _logger = logger;
        // Research runs full agent loops inside a single tool dispatch — needs much longer
        // than the standard tool timeout. Default 10 minutes.
        _timeoutMinutes = Math.Max(appConfig.Timeouts.ToolExecutionSeconds / 6, 10);
    }

    public string Name => "run_research";

    public ToolDefinition Definition => new(Name,
        "Run parallel research agents on multiple topics. Each agent performs multi-step web searches and writes findings to markdown files.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "topics": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "topic": {
                                    "type": "string",
                                    "description": "The research topic to investigate"
                                },
                                "focus": {
                                    "type": "string",
                                    "description": "Optional specific focus or angle for the research"
                                }
                            },
                            "required": ["topic"]
                        },
                        "description": "List of research topics to investigate in parallel"
                    },
                    "project": {
                        "type": "string",
                        "description": "Research project name (files saved to research/{project}/)"
                    }
                },
                "required": ["topics", "project"]
            }
            """).RootElement);

    public async Task<ToolResult> HandleAsync(JsonElement input, AgentContext context, CancellationToken ct = default)
    {
        if (!input.TryGetProperty("topics", out var topicsEl) || topicsEl.GetArrayLength() == 0)
            return ToolResult.Fail("At least one topic is required.");

        var project = input.TryGetProperty("project", out var projEl)
            ? projEl.GetString() ?? "default"
            : "default";

        var topics = new List<ResearchTopic>();
        foreach (var item in topicsEl.EnumerateArray())
        {
            var topic = item.GetProperty("topic").GetString();
            if (string.IsNullOrWhiteSpace(topic)) continue;

            var focus = item.TryGetProperty("focus", out var f) ? f.GetString() : null;
            topics.Add(new ResearchTopic { Topic = topic, Focus = focus });
        }

        if (topics.Count == 0)
            return ToolResult.Fail("No valid topics provided.");

        _logger.LogInformation("RunResearchHandler: {Count} topics for project \"{Project}\"", topics.Count, project);

        // The ct from the orchestrator's tool dispatch has a short timeout (ToolExecutionSeconds).
        // Research agents need much longer, so create a separate scope.
        using var researchCts = new CancellationTokenSource(TimeSpan.FromMinutes(_timeoutMinutes));
        var result = await _pool.RunAsync(project, topics, context, researchCts.Token);
        var formatted = FormatResults(result);
        return ToolResult.Ok(formatted);
    }

    private static string FormatResults(ResearchPoolResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# Research Results — Project: {result.Project}");
        sb.AppendLine();
        sb.AppendLine($"{result.Results.Count} topics investigated. Results:\n");

        foreach (var r in result.Results)
        {
            sb.AppendLine($"## {r.Topic}");

            if (r.Error is not null)
            {
                sb.AppendLine($"**ERROR:** {r.Error}\n");
                continue;
            }

            sb.AppendLine($"**Saved to:** `{r.FilePath}`");
            sb.AppendLine();
            sb.AppendLine(r.Summary);

            if (r.Sources.Count > 0)
            {
                sb.AppendLine("\n**Sources:**");
                foreach (var source in r.Sources)
                {
                    sb.AppendLine($"- {source}");
                }
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }
}
