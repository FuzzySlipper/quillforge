using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

public sealed class RunCouncilHandler : IToolHandler
{
    private readonly ICouncilService _council;
    private readonly ILogger<RunCouncilHandler> _logger;

    public RunCouncilHandler(ICouncilService council, ILogger<RunCouncilHandler> logger)
    {
        _council = council;
        _logger = logger;
    }

    public string Name => "run_council";

    public ToolDefinition Definition => new(Name,
        "Fan a query out to multiple AI council advisors in parallel, then return their collected perspectives for synthesis.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "query": {
                        "type": "string",
                        "description": "The question or topic to present to all council advisors"
                    }
                },
                "required": ["query"]
            }
            """).RootElement);

    public async Task<ToolResult> HandleAsync(ToolInput input, AgentContext context, CancellationToken ct = default)
    {
        var query = input.GetOptionalString("query");
        if (string.IsNullOrWhiteSpace(query))
            return ToolResult.Fail("Query is required.");

        _logger.LogDebug("RunCouncilHandler: dispatching \"{Query}\"", query);

        var result = await _council.RunCouncilAsync(query, ct);
        var formatted = _council.FormatForOrchestrator(result);
        return ToolResult.Ok(formatted);
    }
}
