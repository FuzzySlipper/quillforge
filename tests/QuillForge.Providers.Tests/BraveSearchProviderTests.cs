using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Services;
using QuillForge.Providers.WebSearch;

namespace QuillForge.Providers.Tests;

public class BraveSearchProviderTests
{
    [Fact]
    public async Task SearchAsync_ClampsConfiguredCountToBraveMaximum()
    {
        var handler = new RecordingHttpMessageHandler(SuccessResponse());
        var provider = CreateProvider(handler, maxResults: 50);

        await provider.SearchAsync("clamp count");

        var request = Assert.Single(handler.Requests);
        Assert.Contains("count=20", request.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_ClampsConfiguredCountToBraveMinimum()
    {
        var handler = new RecordingHttpMessageHandler(SuccessResponse());
        var provider = CreateProvider(handler, maxResults: 0);

        await provider.SearchAsync("clamp count");

        var request = Assert.Single(handler.Requests);
        Assert.Contains("count=1", request.RequestUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_ClampsQueryToBraveLimits()
    {
        var handler = new RecordingHttpMessageHandler(SuccessResponse());
        var provider = CreateProvider(handler, maxResults: 5);
        var query = string.Join(' ', Enumerable.Range(1, 80).Select(i => $"word{i:D2}"));

        await provider.SearchAsync(query);

        var request = Assert.Single(handler.Requests);
        var sentQuery = ExtractQueryValue(request.RequestUri!, "q");
        Assert.True(sentQuery.Length <= 400);
        Assert.True(CountWords(sentQuery) <= 50);
        Assert.DoesNotContain("word80", sentQuery, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_ThrowsActionableNonRetryableExceptionFor422()
    {
        var handler = new RecordingHttpMessageHandler(new HttpResponseMessage((HttpStatusCode)422)
        {
            ReasonPhrase = "Unprocessable Entity",
            Content = new StringContent("count must be less than or equal to 20"),
        });
        var provider = CreateProvider(handler, maxResults: 50);

        var ex = await Assert.ThrowsAsync<WebSearchProviderException>(() => provider.SearchAsync("invalid brave request"));

        Assert.Equal("brave", ex.Provider);
        Assert.Equal(422, ex.StatusCode);
        Assert.False(ex.CanRetrySameRequest);
        Assert.Contains("HTTP 422", ex.Message, StringComparison.Ordinal);
        Assert.Contains("configured_count=50", ex.Message, StringComparison.Ordinal);
        Assert.Contains("sent_count=20", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Do not retry", ex.Message, StringComparison.Ordinal);
        Assert.Contains("count must be less than or equal to 20", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_ThrowsActionableNonRetryableExceptionFor429()
    {
        var response = new HttpResponseMessage((HttpStatusCode)429)
        {
            ReasonPhrase = "Too Many Requests",
            Content = new StringContent("rate limit exceeded"),
        };
        response.Headers.TryAddWithoutValidation("Retry-After", "60");
        var handler = new RecordingHttpMessageHandler(response);
        var provider = CreateProvider(handler, maxResults: 10);

        var ex = await Assert.ThrowsAsync<WebSearchProviderException>(() => provider.SearchAsync("rate limited brave request"));

        Assert.Equal(429, ex.StatusCode);
        Assert.False(ex.CanRetrySameRequest);
        Assert.Contains("HTTP 429", ex.Message, StringComparison.Ordinal);
        Assert.Contains("rate limit", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Retry-After: 60", ex.Message, StringComparison.Ordinal);
        Assert.Contains("Do not retry", ex.Message, StringComparison.Ordinal);
    }

    private static BraveSearchProvider CreateProvider(RecordingHttpMessageHandler handler, int maxResults)
    {
        return new BraveSearchProvider(
            new HttpClient(handler),
            "test-key",
            maxResults,
            NullLogger<BraveSearchProvider>.Instance);
    }

    private static HttpResponseMessage SuccessResponse()
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""
                {
                  "web": {
                    "results": [
                      {
                        "title": "Example",
                        "url": "https://example.test/",
                        "description": "Example result"
                      }
                    ]
                  }
                }
                """),
        };
    }

    private static string ExtractQueryValue(Uri uri, string key)
    {
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in query)
        {
            var pieces = part.Split('=', 2);
            if (pieces.Length == 2 && pieces[0] == key)
            {
                return Uri.UnescapeDataString(pieces[1]);
            }
        }

        return string.Empty;
    }

    private static int CountWords(string value)
    {
        return value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private sealed class RecordingHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpResponseMessage _response;

        public RecordingHttpMessageHandler(HttpResponseMessage response)
        {
            _response = response;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_response);
        }
    }
}
