using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;

namespace QuillForge.Providers.WebSearch;

/// <summary>
/// Web search provider backed by the Brave Search API.
/// </summary>
public sealed class BraveSearchProvider : IWebSearchService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly int _maxResults;
    private readonly ILogger<BraveSearchProvider> _logger;

    public BraveSearchProvider(HttpClient httpClient, string apiKey, int maxResults, ILogger<BraveSearchProvider> logger)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _maxResults = maxResults;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(query)}&count={_maxResults}";
        _logger.LogDebug("Brave search: {Url}", url);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Subscription-Token", _apiKey);
        request.Headers.Add("Accept", "application/json");

        var response = await _httpClient.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var results = new List<WebSearchResult>();

        if (doc.RootElement.TryGetProperty("web", out var web) &&
            web.TryGetProperty("results", out var resultsArray))
        {
            foreach (var item in resultsArray.EnumerateArray())
            {
                var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var itemUrl = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var snippet = item.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";

                results.Add(new WebSearchResult(title, itemUrl, snippet));
            }
        }

        _logger.LogInformation("Brave: {Count} results for \"{Query}\"", results.Count, query);
        return results;
    }
}
