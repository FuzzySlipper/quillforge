using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Web.Services;

namespace QuillForge.Architecture.Tests;

public sealed class ConfigBackedWebSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_ResolvesCurrentWebSearchConfigForEachCall()
    {
        var handler = new RecordingSearchHandler();
        var config = new AppConfig
        {
            WebSearch = new WebSearchConfig
            {
                Enabled = true,
                Provider = "searxng",
                SearxngUrl = "http://search.local:8080",
                MaxResults = 5,
            }
        };
        var service = new ConfigBackedWebSearchService(
            new FixedHttpClientFactory(handler),
            config,
            NullLoggerFactory.Instance,
            NullLogger<ConfigBackedWebSearchService>.Instance);

        var searxngResults = await service.SearchAsync("first query");
        config.WebSearch = config.WebSearch with
        {
            Provider = "brave",
            BraveApiKey = "brave-key",
            MaxResults = 2,
        };
        var braveResults = await service.SearchAsync("second query");

        Assert.Equal("SearXNG Result", Assert.Single(searxngResults).Title);
        Assert.Equal("Brave Result", Assert.Single(braveResults).Title);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal("search.local", handler.Requests[0].RequestUri!.Host);
        Assert.Equal("api.search.brave.com", handler.Requests[1].RequestUri!.Host);
        Assert.Equal("brave-key", handler.Requests[1].Headers.GetValues("X-Subscription-Token").Single());
    }

    private sealed class FixedHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;

        public FixedHttpClientFactory(HttpMessageHandler handler)
        {
            _handler = handler;
        }

        public HttpClient CreateClient(string name)
        {
            return new HttpClient(_handler, disposeHandler: false);
        }
    }

    private sealed class RecordingSearchHandler : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            var body = request.RequestUri?.Host == "api.search.brave.com"
                ? """
                  {
                    "web": {
                      "results": [
                        { "title": "Brave Result", "url": "https://brave.example/", "description": "Brave summary" }
                      ]
                    }
                  }
                  """
                : """
                  {
                    "results": [
                      { "title": "SearXNG Result", "url": "https://searxng.example/", "content": "SearXNG summary" }
                    ]
                  }
                  """;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(body),
            });
        }
    }
}
