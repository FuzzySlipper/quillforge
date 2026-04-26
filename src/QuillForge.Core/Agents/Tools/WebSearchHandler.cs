using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Searches the web for real-world information outside the lore corpus.
/// </summary>
public sealed class WebSearchHandler : IToolHandler
{
    private readonly IWebSearchService _webSearch;
    private readonly ILogger<WebSearchHandler> _logger;

    public WebSearchHandler(IWebSearchService webSearch, ILogger<WebSearchHandler> logger)
    {
        _webSearch = webSearch;
        _logger = logger;
    }

    public string Name => "web_search";

    public ToolDefinition Definition => new(Name,
        "Search the web for real-world information, research, or reference material.",
        JsonDocument.Parse("""
            {
                "type": "object",
                "properties": {
                    "query": { "type": "string", "description": "Search query" }
                },
                "required": ["query"]
            }
            """).RootElement);

    public async Task<ToolResult> HandleAsync(ToolInput input, AgentContext context, CancellationToken ct = default)
    {
        var query = input.GetOptionalString("query") ?? "";
        if (string.IsNullOrWhiteSpace(query))
        {
            return ToolResult.Fail("Missing required parameter: query");
        }
        _logger.LogDebug("WebSearchHandler: searching for \"{Query}\"", query);

        try
        {
            var results = await _webSearch.SearchAsync(query, ct);
            return ToolResult.Ok(JsonSerializer.Serialize(results));
        }
        catch (WebSearchProviderException ex)
        {
            _logger.LogWarning(
                ex,
                "Web search provider {Provider} failed: status={StatusCode}, canRetrySameRequest={CanRetrySameRequest}",
                ex.Provider,
                ex.StatusCode,
                ex.CanRetrySameRequest);

            return ToolResult.Fail(ex.Message, retryable: ex.CanRetrySameRequest);
        }
    }
}
