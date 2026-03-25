using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Delegates lore queries to the Librarian agent.
/// Used by ProseWriter, ForgeWriter, and the Orchestrator.
/// </summary>
public sealed class QueryLoreHandler : IToolHandler
{
    private readonly LibrarianAgent _librarian;
    private readonly string _activeLoreSet;
    private readonly ILogger<QueryLoreHandler> _logger;

    public QueryLoreHandler(LibrarianAgent librarian, string activeLoreSet, ILogger<QueryLoreHandler> logger)
    {
        _librarian = librarian;
        _activeLoreSet = activeLoreSet;
        _logger = logger;
    }

    public string Name => "query_lore";

    public ToolDefinition Definition => new(Name,
        "Query the Librarian for lore details about characters, locations, events, or world-building.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "query": {
                        "type": "string",
                        "description": "The lore question to research"
                    }
                },
                "required": ["query"]
            }
            """).RootElement);

    public async Task<ToolResult> HandleAsync(JsonElement input, AgentContext context, CancellationToken ct = default)
    {
        var query = input.GetProperty("query").GetString();
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolResult.Fail("Query is required.");
        }

        _logger.LogDebug("QueryLoreHandler: querying \"{Query}\"", query);

        var bundle = await _librarian.QueryAsync(query, _activeLoreSet, context, ct);
        return ToolResult.Ok(JsonSerializer.Serialize(bundle));
    }
}
