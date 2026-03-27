using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;

namespace QuillForge.Providers.WebSearch;

/// <summary>
/// Web search provider backed by a self-hosted SearXNG instance.
/// </summary>
public sealed class SearxngSearchProvider : IWebSearchService
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly int _maxResults;
    private readonly ILogger<SearxngSearchProvider> _logger;

    public SearxngSearchProvider(HttpClient httpClient, string baseUrl, int maxResults, ILogger<SearxngSearchProvider> logger)
    {
        _httpClient = httpClient;
        _baseUrl = baseUrl.TrimEnd('/');
        _maxResults = maxResults;
        _logger = logger;
    }

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        var url = $"{_baseUrl}/search?q={Uri.EscapeDataString(query)}&format=json&categories=general";
        _logger.LogDebug("SearXNG search: {Url}", url);

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        var results = new List<WebSearchResult>();

        if (doc.RootElement.TryGetProperty("results", out var resultsArray))
        {
            foreach (var item in resultsArray.EnumerateArray())
            {
                if (results.Count >= _maxResults)
                    break;

                var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                var itemUrl = item.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                var snippet = item.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";

                results.Add(new WebSearchResult(title, itemUrl, snippet));
            }
        }

        _logger.LogInformation("SearXNG: {Count} results for \"{Query}\"", results.Count, query);
        return results;
    }
}
