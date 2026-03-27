using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;

namespace QuillForge.Providers.WebSearch;

/// <summary>
/// Web search provider backed by the Google Custom Search JSON API.
/// </summary>
public sealed class GoogleSearchProvider : IWebSearchService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _cxId;
    private readonly int _maxResults;
    private readonly ILogger<GoogleSearchProvider> _logger;

    public GoogleSearchProvider(HttpClient httpClient, string apiKey, string cxId, int maxResults, ILogger<GoogleSearchProvider> logger)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _cxId = cxId;
        _maxResults = maxResults;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        // Google Custom Search API limits num to 10 per request.
        var num = Math.Min(_maxResults, 10);
        var url = $"https://www.googleapis.com/customsearch/v1?key={Uri.EscapeDataString(_apiKey)}&cx={Uri.EscapeDataString(_cxId)}&q={Uri.EscapeDataString(query)}&num={num}";
        _logger.LogDebug("Google Custom Search: \"{Query}\"", query);

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var results = new List<WebSearchResult>();

        if (doc.RootElement.TryGetProperty("items", out var items))
        {
            foreach (var item in items.EnumerateArray())
            {
                var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var itemUrl = item.TryGetProperty("link", out var l) ? l.GetString() ?? "" : "";
                var snippet = item.TryGetProperty("snippet", out var s) ? s.GetString() ?? "" : "";

                results.Add(new WebSearchResult(title, itemUrl, snippet));
            }
        }

        _logger.LogInformation("Google: {Count} results for \"{Query}\"", results.Count, query);
        return results;
    }
}
