using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;

namespace QuillForge.Providers.WebSearch;

/// <summary>
/// Web search provider backed by Z.AI's remote Web Search MCP endpoint.
/// </summary>
public sealed class ZaiSearchProvider : IWebSearchService
{
    public const string DefaultEndpoint = "https://api.z.ai/api/mcp/web_search_prime/mcp";
    public const string DefaultToolName = "webSearchPrime";

    private const string ProviderName = "zai";
    private const string JsonRpcVersion = "2.0";
    private const string ProtocolVersion = "2025-03-26";
    private const int MaxErrorBodyChars = 2048;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly Uri _endpoint;
    private readonly string _toolName;
    private readonly int _maxResults;
    private readonly ILogger<ZaiSearchProvider> _logger;

    public ZaiSearchProvider(
        HttpClient httpClient,
        string apiKey,
        string? endpoint,
        string? toolName,
        int maxResults,
        ILogger<ZaiSearchProvider> logger)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("Z.AI API key must not be empty.", nameof(apiKey));
        }

        _httpClient = httpClient;
        _apiKey = apiKey;
        _endpoint = new Uri(string.IsNullOrWhiteSpace(endpoint) ? DefaultEndpoint : endpoint, UriKind.Absolute);
        _toolName = string.IsNullOrWhiteSpace(toolName) ? DefaultToolName : toolName.Trim();
        _maxResults = Math.Max(1, maxResults);
        _logger = logger;
    }

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new WebSearchProviderException(
                ProviderName,
                "Z.AI Web Search rejected an empty query before sending the request. Provide a non-empty web_search query.",
                canRetrySameRequest: false);
        }

        var normalizedQuery = query.Trim();
        _logger.LogDebug("Z.AI web search via MCP endpoint {Endpoint}: \"{Query}\"", _endpoint, normalizedQuery);

        var sessionId = await InitializeSessionAsync(ct);
        var tool = await ResolveToolAsync(sessionId, ct);
        var results = await CallToolAsync(sessionId, tool, normalizedQuery, ct);

        _logger.LogInformation("Z.AI Web Search: {Count} results for \"{Query}\"", results.Count, normalizedQuery);
        return results;
    }

    private async Task<string?> InitializeSessionAsync(CancellationToken ct)
    {
        using var initialize = await SendJsonRpcRequestAsync(
            id: 1,
            method: "initialize",
            parameters: new
            {
                protocolVersion = ProtocolVersion,
                capabilities = new { },
                clientInfo = new
                {
                    name = "QuillForge",
                    version = "1.0",
                },
            },
            sessionId: null,
            ct);

        ThrowIfJsonRpcError(initialize.Document.RootElement, "initialize");

        await SendJsonRpcNotificationAsync(
            "notifications/initialized",
            parameters: null,
            initialize.SessionId,
            ct);

        return initialize.SessionId;
    }

    private async Task<McpToolMetadata> ResolveToolAsync(string? sessionId, CancellationToken ct)
    {
        using var toolsList = await SendJsonRpcRequestAsync(
            id: 2,
            method: "tools/list",
            parameters: new { },
            sessionId,
            ct);

        ThrowIfJsonRpcError(toolsList.Document.RootElement, "tools/list");

        if (!toolsList.Document.RootElement.TryGetProperty("result", out var result)
            || !result.TryGetProperty("tools", out var tools)
            || tools.ValueKind != JsonValueKind.Array)
        {
            throw new WebSearchProviderException(
                ProviderName,
                "Z.AI Web Search MCP tools/list response did not include a tools array. Check the MCP endpoint configuration.",
                canRetrySameRequest: false);
        }

        JsonElement? selected = null;
        foreach (var tool in tools.EnumerateArray())
        {
            if (!tool.TryGetProperty("name", out var nameElement))
            {
                continue;
            }

            var name = nameElement.GetString();
            if (string.Equals(name, _toolName, StringComparison.OrdinalIgnoreCase))
            {
                selected = tool;
                break;
            }
        }

        if (selected is null)
        {
            throw new WebSearchProviderException(
                ProviderName,
                $"Z.AI Web Search MCP endpoint did not expose the configured tool '{_toolName}'. Check zai_mcp_tool_name or the endpoint URL.",
                canRetrySameRequest: false);
        }

        var selectedTool = selected.Value;
        var toolName = selectedTool.GetProperty("name").GetString() ?? _toolName;
        var queryArgumentName = InferQueryArgumentName(selectedTool);

        return new McpToolMetadata(toolName, queryArgumentName);
    }

    private async Task<IReadOnlyList<WebSearchResult>> CallToolAsync(
        string? sessionId,
        McpToolMetadata tool,
        string query,
        CancellationToken ct)
    {
        var arguments = new Dictionary<string, object?>
        {
            [tool.QueryArgumentName] = query,
        };

        using var callResult = await SendJsonRpcRequestAsync(
            id: 3,
            method: "tools/call",
            parameters: new
            {
                name = tool.Name,
                arguments,
            },
            sessionId,
            ct);

        ThrowIfJsonRpcError(callResult.Document.RootElement, "tools/call");

        if (!callResult.Document.RootElement.TryGetProperty("result", out var result))
        {
            throw new WebSearchProviderException(
                ProviderName,
                "Z.AI Web Search MCP tools/call response did not include a result object.",
                canRetrySameRequest: false);
        }

        if (result.TryGetProperty("isError", out var isError) && isError.ValueKind == JsonValueKind.True)
        {
            var errorText = BuildToolContentText(result);
            throw new WebSearchProviderException(
                ProviderName,
                "Z.AI Web Search tool returned an error. Do not retry this same web_search during the current tool loop; change the query, API key, endpoint, or wait for quota/rate-limit reset. " +
                $"Tool error: {BuildPreview(errorText, MaxErrorBodyChars, "<empty>")}",
                canRetrySameRequest: false);
        }

        var results = ExtractResults(result);
        return results;
    }

    private async Task<McpJsonResponse> SendJsonRpcRequestAsync(
        int id,
        string method,
        object? parameters,
        string? sessionId,
        CancellationToken ct)
    {
        var payload = parameters is null
            ? new Dictionary<string, object?>
            {
                ["jsonrpc"] = JsonRpcVersion,
                ["id"] = id,
                ["method"] = method,
            }
            : new Dictionary<string, object?>
            {
                ["jsonrpc"] = JsonRpcVersion,
                ["id"] = id,
                ["method"] = method,
                ["params"] = parameters,
            };

        using var response = await SendJsonAsync(payload, method, sessionId, ct);
        var body = await ReadSuccessfulBodyOrThrowAsync(response, method, ct);
        if (string.IsNullOrWhiteSpace(body))
        {
            throw new WebSearchProviderException(
                ProviderName,
                $"Z.AI Web Search MCP {method} returned an empty response body.",
                canRetrySameRequest: true);
        }

        return new McpJsonResponse(ParseJsonOrSse(body, method), GetSessionId(response));
    }

    private async Task SendJsonRpcNotificationAsync(
        string method,
        object? parameters,
        string? sessionId,
        CancellationToken ct)
    {
        var payload = parameters is null
            ? new Dictionary<string, object?>
            {
                ["jsonrpc"] = JsonRpcVersion,
                ["method"] = method,
            }
            : new Dictionary<string, object?>
            {
                ["jsonrpc"] = JsonRpcVersion,
                ["method"] = method,
                ["params"] = parameters,
            };

        using var response = await SendJsonAsync(payload, method, sessionId, ct);
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        await ThrowHttpErrorAsync(response, method, ct);
    }

    private async Task<HttpResponseMessage> SendJsonAsync(
        IReadOnlyDictionary<string, object?> payload,
        string method,
        string? sessionId,
        CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", ProtocolVersion);
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            request.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        }

        var json = JsonSerializer.Serialize(payload, SerializerOptions);
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");

        _logger.LogTrace("Sending Z.AI MCP {Method} request to {Endpoint}", method, _endpoint);
        return await _httpClient.SendAsync(request, ct);
    }

    private async Task<string> ReadSuccessfulBodyOrThrowAsync(
        HttpResponseMessage response,
        string method,
        CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            await ThrowHttpErrorAsync(response, method, ct);
        }

        return await response.Content.ReadAsStringAsync(ct);
    }

    private async Task ThrowHttpErrorAsync(
        HttpResponseMessage response,
        string method,
        CancellationToken ct)
    {
        var statusCode = (int)response.StatusCode;
        var body = await response.Content.ReadAsStringAsync(ct);
        var bodyPreview = BuildPreview(body.Trim(), MaxErrorBodyChars, fallback: "<empty>");
        var retryAfter = response.Headers.RetryAfter?.ToString();
        var canRetrySameRequest = statusCode is >= 500 or 408;
        var guidance = canRetrySameRequest
            ? "The remote MCP service may be temporarily unavailable; retrying later may help."
            : "Do not retry this same web_search during the current tool loop; check the Z.AI API key, endpoint, quota, or request shape.";
        var retryAfterText = string.IsNullOrWhiteSpace(retryAfter) ? "" : $" Retry-After: {retryAfter}.";

        _logger.LogWarning(
            "Z.AI Web Search MCP {Method} failed with HTTP {StatusCode}: retry_after={RetryAfter}, body={BodyPreview}",
            method,
            statusCode,
            retryAfter,
            bodyPreview);

        throw new WebSearchProviderException(
            ProviderName,
            $"Z.AI Web Search MCP {method} returned HTTP {statusCode} {response.ReasonPhrase}. {guidance}{retryAfterText} Response body: {bodyPreview}",
            statusCode,
            canRetrySameRequest);
    }

    private static string? GetSessionId(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("Mcp-Session-Id", out var values)
            ? values.FirstOrDefault()
            : null;
    }

    private static JsonDocument ParseJsonOrSse(string body, string method)
    {
        var trimmed = body.TrimStart();
        if (trimmed.StartsWith('{') || trimmed.StartsWith('['))
        {
            return JsonDocument.Parse(body);
        }

        var dataEvents = ParseSseDataEvents(body);
        foreach (var data in dataEvents)
        {
            var dataTrimmed = data.TrimStart();
            if (dataTrimmed.StartsWith('{') || dataTrimmed.StartsWith('['))
            {
                return JsonDocument.Parse(data);
            }
        }

        throw new WebSearchProviderException(
            ProviderName,
            $"Z.AI Web Search MCP {method} returned neither JSON nor parseable SSE JSON. Response preview: {BuildPreview(body, MaxErrorBodyChars, "<empty>")}",
            canRetrySameRequest: true);
    }

    private static List<string> ParseSseDataEvents(string body)
    {
        var events = new List<string>();
        var builder = new StringBuilder();
        using var reader = new StringReader(body);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Length == 0)
            {
                AddCurrentEvent(events, builder);
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(line[5..].TrimStart());
        }

        AddCurrentEvent(events, builder);
        return events;
    }

    private static void AddCurrentEvent(List<string> events, StringBuilder builder)
    {
        if (builder.Length == 0)
        {
            return;
        }

        events.Add(builder.ToString());
        builder.Clear();
    }

    private static void ThrowIfJsonRpcError(JsonElement root, string operation)
    {
        if (!root.TryGetProperty("error", out var error) || error.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return;
        }

        var code = error.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.Number
            ? codeElement.GetInt32()
            : 0;
        var message = error.TryGetProperty("message", out var messageElement)
            ? messageElement.GetString() ?? "<no message>"
            : "<no message>";
        var data = error.TryGetProperty("data", out var dataElement)
            ? dataElement.GetRawText()
            : "<none>";

        throw new WebSearchProviderException(
            ProviderName,
            $"Z.AI Web Search MCP {operation} failed with JSON-RPC error {code}: {message}. Error data: {BuildPreview(data, MaxErrorBodyChars, "<empty>")}",
            canRetrySameRequest: false);
    }

    private static string InferQueryArgumentName(JsonElement selectedTool)
    {
        if (!selectedTool.TryGetProperty("inputSchema", out var schema)
            || !schema.TryGetProperty("properties", out var properties)
            || properties.ValueKind != JsonValueKind.Object)
        {
            return "search_query";
        }

        if (properties.TryGetProperty("search_query", out _))
        {
            return "search_query";
        }

        if (properties.TryGetProperty("query", out _))
        {
            return "query";
        }

        if (properties.TryGetProperty("q", out _))
        {
            return "q";
        }

        var requiredNames = ReadRequiredNames(schema);
        foreach (var requiredName in requiredNames)
        {
            if (properties.TryGetProperty(requiredName, out var property) && IsStringLikeProperty(property))
            {
                return requiredName;
            }
        }

        foreach (var property in properties.EnumerateObject())
        {
            if (IsStringLikeProperty(property.Value))
            {
                return property.Name;
            }
        }

        return "search_query";
    }

    private IReadOnlyList<WebSearchResult> ExtractResults(JsonElement toolResult)
    {
        var results = new List<WebSearchResult>();

        if (toolResult.TryGetProperty("structuredContent", out var structuredContent))
        {
            AddSearchResultsFromJson(structuredContent, results);
        }

        var textParts = new List<string>();
        if (toolResult.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in content.EnumerateArray())
            {
                if (item.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
                {
                    var text = textElement.GetString();
                    if (string.IsNullOrWhiteSpace(text))
                    {
                        continue;
                    }

                    textParts.Add(text);
                    AddSearchResultsFromText(text, results);
                }
            }
        }

        if (results.Count == 0 && textParts.Count > 0)
        {
            var text = string.Join("\n\n", textParts).Trim();
            results.Add(new WebSearchResult("Z.AI Web Search Results", string.Empty, text));
        }

        return LimitResults(results);
    }

    private IReadOnlyList<WebSearchResult> LimitResults(List<WebSearchResult> results)
    {
        if (results.Count <= _maxResults)
        {
            return results;
        }

        return results.Take(_maxResults).ToList();
    }

    private static void AddSearchResultsFromText(string text, List<WebSearchResult> results)
    {
        if (!TryParseJsonFromText(text, out var document) || document is null)
        {
            return;
        }

        using (document)
        {
            AddSearchResultsFromJson(document.RootElement, results);
        }
    }

    private static bool TryParseJsonFromText(string text, out JsonDocument? document)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewline = trimmed.IndexOf('\n');
            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
            {
                trimmed = trimmed[(firstNewline + 1)..lastFence].Trim();
            }
        }

        if (TryParseJson(trimmed, out document))
        {
            return true;
        }

        var objectStart = trimmed.IndexOf('{');
        var objectEnd = trimmed.LastIndexOf('}');
        if (objectStart >= 0 && objectEnd > objectStart)
        {
            var candidate = trimmed[objectStart..(objectEnd + 1)];
            if (TryParseJson(candidate, out document))
            {
                return true;
            }
        }

        var arrayStart = trimmed.IndexOf('[');
        var arrayEnd = trimmed.LastIndexOf(']');
        if (arrayStart >= 0 && arrayEnd > arrayStart)
        {
            var candidate = trimmed[arrayStart..(arrayEnd + 1)];
            if (TryParseJson(candidate, out document))
            {
                return true;
            }
        }

        document = null;
        return false;
    }

    private static bool TryParseJson(string value, out JsonDocument? document)
    {
        try
        {
            document = JsonDocument.Parse(value);
            return true;
        }
        catch (JsonException)
        {
            document = null;
            return false;
        }
    }

    private static void AddSearchResultsFromJson(JsonElement element, List<WebSearchResult> results)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    AddSearchResultsFromJson(item, results);
                }

                break;

            case JsonValueKind.Object:
                if (TryAddSingleResult(element, results))
                {
                    break;
                }

                if (TryAddResultsArrayProperty(element, "results", results)
                    || TryAddResultsArrayProperty(element, "search_result", results)
                    || TryAddResultsArrayProperty(element, "searchResult", results)
                    || TryAddResultsArrayProperty(element, "searchResults", results)
                    || TryAddResultsArrayProperty(element, "items", results)
                    || TryAddResultsArrayProperty(element, "organic_results", results)
                    || TryAddResultsArrayProperty(element, "data", results))
                {
                    break;
                }

                if (element.TryGetProperty("web", out var web))
                {
                    AddSearchResultsFromJson(web, results);
                }

                break;
        }
    }

    private static bool TryAddResultsArrayProperty(JsonElement element, string propertyName, List<WebSearchResult> results)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        AddSearchResultsFromJson(property, results);
        return true;
    }

    private static bool TryAddSingleResult(JsonElement item, List<WebSearchResult> results)
    {
        var title = FirstStringProperty(item, "title", "pageTitle", "name", "headline");
        var url = FirstStringProperty(item, "url", "pageUrl", "link", "href");
        var snippet = FirstStringProperty(item, "snippet", "summary", "summaries", "content", "description", "text");

        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(url) && string.IsNullOrWhiteSpace(snippet))
        {
            return false;
        }

        results.Add(new WebSearchResult(title, url, snippet));
        return true;
    }

    private static string FirstStringProperty(JsonElement item, params string[] names)
    {
        foreach (var name in names)
        {
            if (item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string BuildToolContentText(JsonElement toolResult)
    {
        if (!toolResult.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
        {
            return toolResult.GetRawText();
        }

        var builder = new StringBuilder();
        foreach (var item in content.EnumerateArray())
        {
            if (!item.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(text.GetString());
        }

        return builder.Length == 0 ? toolResult.GetRawText() : builder.ToString();
    }

    private static HashSet<string> ReadRequiredNames(JsonElement schema)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        if (!schema.TryGetProperty("required", out var required) || required.ValueKind != JsonValueKind.Array)
        {
            return names;
        }

        foreach (var item in required.EnumerateArray())
        {
            var name = item.GetString();
            if (!string.IsNullOrWhiteSpace(name))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static bool IsStringLikeProperty(JsonElement property)
    {
        if (!property.TryGetProperty("type", out var type))
        {
            return true;
        }

        if (type.ValueKind == JsonValueKind.String)
        {
            return string.Equals(type.GetString(), "string", StringComparison.OrdinalIgnoreCase);
        }

        if (type.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in type.EnumerateArray())
            {
                if (string.Equals(item.GetString(), "string", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string BuildPreview(string value, int maxChars, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (value.Length <= maxChars)
        {
            return value;
        }

        return value[..maxChars] + "…";
    }

    private sealed record McpJsonResponse(JsonDocument Document, string? SessionId) : IDisposable
    {
        public void Dispose()
        {
            Document.Dispose();
        }
    }

    private sealed record McpToolMetadata(string Name, string QueryArgumentName);
}
