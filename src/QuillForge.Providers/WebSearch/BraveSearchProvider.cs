using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;

namespace QuillForge.Providers.WebSearch;

/// <summary>
/// Web search provider backed by the Brave Search API.
/// </summary>
public sealed class BraveSearchProvider : IWebSearchService
{
    private const int MinResultCount = 1;
    private const int MaxResultCount = 20;
    private const int MaxQueryChars = 400;
    private const int MaxQueryWords = 50;
    private const int MaxErrorBodyChars = 2048;
    private const int MaxQueryPreviewChars = 160;

    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly int _configuredMaxResults;
    private readonly int _count;
    private readonly ILogger<BraveSearchProvider> _logger;

    public BraveSearchProvider(HttpClient httpClient, string apiKey, int maxResults, ILogger<BraveSearchProvider> logger)
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _configuredMaxResults = maxResults;
        _count = Math.Clamp(maxResults, MinResultCount, MaxResultCount);
        _logger = logger;

        if (_count != maxResults)
        {
            _logger.LogWarning(
                "Brave web search count {ConfiguredCount} is outside provider bounds {MinCount}-{MaxCount}; using {Count}",
                maxResults,
                MinResultCount,
                MaxResultCount,
                _count);
        }
    }

    public async Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        var normalizedQuery = NormalizeQuery(query);
        var url = $"https://api.search.brave.com/res/v1/web/search?q={Uri.EscapeDataString(normalizedQuery)}&count={_count}";
        _logger.LogDebug("Brave search: {Url}", url);

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Subscription-Token", _apiKey);
        request.Headers.Add("Accept", "application/json");

        using var response = await _httpClient.SendAsync(request, ct);
        await EnsureSuccessOrThrowActionableAsync(response, normalizedQuery, ct);

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

        _logger.LogInformation("Brave: {Count} results for \"{Query}\"", results.Count, normalizedQuery);
        return results;
    }

    private string NormalizeQuery(string query)
    {
        var words = SplitQueryWords(query ?? string.Empty);
        if (words.Length == 0)
        {
            throw new WebSearchProviderException(
                "brave",
                "Brave Search rejected an empty query before sending the request. Provide a non-empty web_search query.",
                canRetrySameRequest: false);
        }

        var wordLimit = Math.Min(words.Length, MaxQueryWords);
        var builder = new StringBuilder();
        for (var i = 0; i < wordLimit; i++)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(words[i]);
        }

        var normalized = builder.ToString();
        var wasClamped = words.Length > MaxQueryWords;
        if (normalized.Length > MaxQueryChars)
        {
            normalized = normalized[..MaxQueryChars].TrimEnd();
            wasClamped = true;
        }

        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new WebSearchProviderException(
                "brave",
                "Brave Search query became empty after applying provider limits. Provide a shorter, non-empty query.",
                canRetrySameRequest: false);
        }

        if (wasClamped)
        {
            _logger.LogWarning(
                "Brave web search query exceeded provider limits ({MaxChars} chars, {MaxWords} words); clamped to {Chars} chars and {Words} words",
                MaxQueryChars,
                MaxQueryWords,
                normalized.Length,
                CountWords(normalized));
        }

        return normalized;
    }

    private async Task EnsureSuccessOrThrowActionableAsync(
        HttpResponseMessage response,
        string normalizedQuery,
        CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var statusCode = (int)response.StatusCode;
        if (statusCode is not (422 or 429))
        {
            response.EnsureSuccessStatusCode();
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var retryAfter = response.Headers.RetryAfter?.ToString();
        var bodyPreview = BuildPreview(body.Trim(), MaxErrorBodyChars, fallback: "<empty>");
        var queryPreview = BuildPreview(normalizedQuery, MaxQueryPreviewChars, fallback: "<empty>");
        var retryGuidance = statusCode == 429
            ? "Brave Search rate limit was reached. Do not retry this same web_search during the current tool loop; wait for the rate limit to reset or reduce Brave search frequency."
            : "Brave Search rejected the request as invalid. Do not retry this same web_search during the current tool loop; shorten/change the query or fix Brave search configuration.";
        var retryAfterText = string.IsNullOrWhiteSpace(retryAfter)
            ? ""
            : $" Retry-After: {retryAfter}.";

        var message =
            $"Brave Search returned HTTP {statusCode} {response.ReasonPhrase}. {retryGuidance}{retryAfterText} " +
            $"Request diagnostics: configured_count={_configuredMaxResults}, sent_count={_count}, " +
            $"query_chars={normalizedQuery.Length}, query_words={CountWords(normalizedQuery)}, query_preview=\"{queryPreview}\". " +
            $"Response body: {bodyPreview}";

        _logger.LogWarning(
            "Brave search failed with non-retryable status {StatusCode}: configured_count={ConfiguredCount}, sent_count={Count}, query_chars={QueryChars}, query_words={QueryWords}, retry_after={RetryAfter}, body={BodyPreview}",
            statusCode,
            _configuredMaxResults,
            _count,
            normalizedQuery.Length,
            CountWords(normalizedQuery),
            retryAfter,
            bodyPreview);

        throw new WebSearchProviderException(
            "brave",
            message,
            statusCode,
            canRetrySameRequest: false);
    }

    private static int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return 0;
        }

        return SplitQueryWords(text).Length;
    }

    private static string[] SplitQueryWords(string text)
    {
        return text.Trim().Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
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
}
