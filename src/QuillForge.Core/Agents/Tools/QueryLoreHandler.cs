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
public sealed class QueryLoreHandler : TypedToolHandler<QueryLoreArgs>
{
    private readonly LibrarianAgent _librarian;
    private readonly ILoreStore _loreStore;
    private readonly IContentFileService _fileService;
    private readonly CanonPrerequisiteGuard _canonGuard;
    private readonly ILogger<QueryLoreHandler> _logger;

    public QueryLoreHandler(
        LibrarianAgent librarian,
        ILoreStore loreStore,
        IContentFileService fileService,
        CanonPrerequisiteGuard canonGuard,
        ILogger<QueryLoreHandler> logger)
    {
        _librarian = librarian;
        _loreStore = loreStore;
        _fileService = fileService;
        _canonGuard = canonGuard;
        _logger = logger;
    }

    public override string Name => "query_lore";

    public override ToolDefinition Definition => new(Name,
        "Query the Librarian for facts from the active lore document corpus only. This does not search character cards, session canon, plot state, chat history, or web results.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "query": {
                        "type": "string",
                        "description": "Question to answer from lore documents in the active lore set"
                    }
                },
                "required": ["query"]
            }
            """).RootElement);

    protected override async Task<ToolResult> HandleTypedAsync(QueryLoreArgs input, AgentContext context, CancellationToken ct = default)
    {
        var query = input.Query;
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolResult.Fail("Query is required.");
        }

        _logger.LogDebug("QueryLoreHandler: querying \"{Query}\" in lore set \"{LoreSet}\"", query, context.ActiveLoreSet);

        try
        {
            await _canonGuard.EnsureQueryableLoreAvailableAsync(context, "query lore", ct);
        }
        catch (CanonPrerequisiteException ex)
        {
            return ToolResult.Fail(ex.Message);
        }

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

        var result = await _librarian.QueryAsync(query, context.ActiveLoreSet, context, runLore, ct);

        // Report librarian token usage to the forge stats tracker (if this is a forge run).
        // The ToolLoop only aggregates its own completion rounds; the librarian's nested
        // LLM call is invisible to it, so we report it through the callback.
        // Usage stays on LibrarianResult — only the Bundle is serialized to the tool result.
        if (result.Usage.TotalTokens > 0)
        {
            context.OnNestedCompletion?.Invoke("librarian", result.Usage, 0);
        }

        return ToolResult.Ok(JsonSerializer.Serialize(result.Bundle));
    }
}

public sealed record QueryLoreArgs
{
    public string Query { get; init; } = "";
}
