using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Delegates lore queries to the Librarian agent.
/// Resolves the active lore set from AgentContext at call time,
/// not from a value captured at construction.
/// </summary>
public sealed class QueryLoreHandler : IToolHandler
{
    private readonly LibrarianAgent _librarian;
    private readonly ILoreStore _loreStore;
    private readonly IContentFileService _fileService;
    private readonly ILogger<QueryLoreHandler> _logger;

    public QueryLoreHandler(LibrarianAgent librarian, ILoreStore loreStore, IContentFileService fileService, ILogger<QueryLoreHandler> logger)
    {
        _librarian = librarian;
        _loreStore = loreStore;
        _fileService = fileService;
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

        _logger.LogDebug("QueryLoreHandler: querying \"{Query}\" in lore set \"{LoreSet}\"", query, context.ActiveLoreSet);

        // Load run-specific lore if available (forge pipeline runs)
        string? runLore = null;
        if (!string.IsNullOrEmpty(context.RunLorePath))
        {
            try
            {
                runLore = await _fileService.ReadAsync(context.RunLorePath, ct);
            }
            catch (FileNotFoundException)
            {
                _logger.LogDebug("No run lore file at {Path}", context.RunLorePath);
            }
        }

        // Short-circuit: if the lore set is empty and there's no run-specific lore,
        // skip the Librarian LLM call entirely — there's nothing to query.
        if (string.IsNullOrEmpty(runLore))
        {
            var loreContent = await _loreStore.LoadLoreSetAsync(context.ActiveLoreSet, ct);
            if (loreContent.Count == 0)
            {
                _logger.LogDebug("QueryLoreHandler: lore set \"{LoreSet}\" is empty, skipping Librarian", context.ActiveLoreSet);
                var empty = new LoreBundle
                {
                    RelevantPassages = [],
                    SourceFiles = [],
                    Confidence = LoreConfidence.Low,
                };
                return ToolResult.Ok(JsonSerializer.Serialize(empty));
            }
        }

        var bundle = await _librarian.QueryAsync(query, context.ActiveLoreSet, context, runLore, ct);
        return ToolResult.Ok(JsonSerializer.Serialize(bundle));
    }
}
