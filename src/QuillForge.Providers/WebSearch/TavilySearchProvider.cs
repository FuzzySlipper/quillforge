using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;

namespace QuillForge.Providers.WebSearch;

/// <summary>
/// Web search provider backed by the Tavily Search API.
/// </summary>
public sealed class TavilySearchProvider : IWebSearchService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly int _maxResults;
    private readonly ILogger<TavilySearchProvider> _logger;

    public TavilySearchProvider(HttpClient httpClient, string apiKey, int maxResults, ILogger<TavilySearchProvider> logger)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _maxResults = maxResults;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        _logger.LogDebug("Tavily search: \"{Query}\"", query);

        var payload = new
        {
            api_key = _apiKey,
            query,
            max_results = _maxResults,
        };

        var response = await _httpClient.PostAsJsonAsync("https://api.tavily.com/search", payload, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var results = new List<WebSearchResult>();

        if (doc.RootElement.TryGetProperty("results", out var resultsArray))
        {
            foreach (var item in resultsArray.EnumerateArray())
            {
                var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var url = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var snippet = item.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";

                results.Add(new WebSearchResult(title, url, snippet));
            }
        }

        _logger.LogInformation("Tavily: {Count} results for \"{Query}\"", results.Count, query);
        return results;
    }
}
