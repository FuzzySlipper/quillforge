using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Services;
using QuillForge.Providers.WebSearch;

namespace QuillForge.Providers.Tests;

public class ZaiSearchProviderTests
{
    [Fact]
    public async Task SearchAsync_InitializesAndCallsToolWithBearerAuthAndSearchQuery()
    {
        var handler = new RecordingMcpHandler(
            InitializeResponse("session-1"),
            AcceptedResponse(),
            ToolsListResponse("search_query"),
            ToolCallResponse(JsonSerializer.Serialize(new
            {
                results = new[]
                {
                    new { title = "Result One", url = "https://example.test/one", summary = "First summary" },
                },
            })));
        var provider = CreateProvider(handler, maxResults: 5);

        var results = await provider.SearchAsync("latest fantasy publishing news");

        var result = Assert.Single(results);
        Assert.Equal("Result One", result.Title);
        Assert.Equal("https://example.test/one", result.Url);
        Assert.Equal("First summary", result.Snippet);

        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests, request =>
        {
            Assert.Equal(ZaiSearchProvider.DefaultEndpoint, request.Uri.ToString());
            Assert.Equal("Bearer test-zai-key", request.Authorization);
            Assert.Contains("application/json", request.Accept);
            Assert.Contains("text/event-stream", request.Accept);
            Assert.Equal("application/json", request.ContentType);
        });

        Assert.Null(handler.Requests[0].SessionId);
        Assert.Equal("session-1", handler.Requests[1].SessionId);
        Assert.Equal("session-1", handler.Requests[2].SessionId);
        Assert.Equal("session-1", handler.Requests[3].SessionId);

        using var initialize = JsonDocument.Parse(handler.Requests[0].Body);
        Assert.Equal("initialize", initialize.RootElement.GetProperty("method").GetString());
        Assert.Equal("2025-03-26", initialize.RootElement.GetProperty("params").GetProperty("protocolVersion").GetString());

        using var notification = JsonDocument.Parse(handler.Requests[1].Body);
        Assert.Equal("notifications/initialized", notification.RootElement.GetProperty("method").GetString());
        Assert.False(notification.RootElement.TryGetProperty("id", out _));

        using var toolsCall = JsonDocument.Parse(handler.Requests[3].Body);
        Assert.Equal("tools/call", toolsCall.RootElement.GetProperty("method").GetString());
        var parameters = toolsCall.RootElement.GetProperty("params");
        Assert.Equal("webSearchPrime", parameters.GetProperty("name").GetString());
        Assert.Equal(
            "latest fantasy publishing news",
            parameters.GetProperty("arguments").GetProperty("search_query").GetString());
    }

    [Fact]
    public async Task SearchAsync_UsesQueryArgumentFromToolsListWhenSearchQueryIsUnavailable()
    {
        var handler = new RecordingMcpHandler(
            InitializeResponse("session-2"),
            AcceptedResponse(),
            ToolsListResponse("query"),
            ToolCallResponse(JsonSerializer.Serialize(new[]
            {
                new { title = "Query Result", link = "https://example.test/query", snippet = "Query summary" },
            })));
        var provider = CreateProvider(handler, maxResults: 5);

        await provider.SearchAsync("alternate schema");

        using var toolsCall = JsonDocument.Parse(handler.Requests[3].Body);
        var arguments = toolsCall.RootElement.GetProperty("params").GetProperty("arguments");
        Assert.Equal("alternate schema", arguments.GetProperty("query").GetString());
        Assert.False(arguments.TryGetProperty("search_query", out _));
    }

    [Fact]
    public async Task SearchAsync_ParsesSseToolResponseAndLimitsResults()
    {
        var toolCallPayload = new
        {
            jsonrpc = "2.0",
            id = 3,
            result = new
            {
                content = new[]
                {
                    new
                    {
                        type = "text",
                        text = JsonSerializer.Serialize(new
                        {
                            results = new[]
                            {
                                new { title = "First", url = "https://example.test/1", content = "One" },
                                new { title = "Second", url = "https://example.test/2", content = "Two" },
                                new { title = "Third", url = "https://example.test/3", content = "Three" },
                            },
                        }),
                    },
                },
                isError = false,
            },
        };
        var handler = new RecordingMcpHandler(
            InitializeResponse("session-3"),
            AcceptedResponse(),
            ToolsListResponse("search_query"),
            SseResponse(toolCallPayload));
        var provider = CreateProvider(handler, maxResults: 2);

        var results = await provider.SearchAsync("sse response");

        Assert.Equal(2, results.Count);
        Assert.Equal("First", results[0].Title);
        Assert.Equal("Second", results[1].Title);
    }

    [Fact]
    public async Task SearchAsync_ThrowsActionableExceptionForToolError()
    {
        var handler = new RecordingMcpHandler(
            InitializeResponse("session-4"),
            AcceptedResponse(),
            ToolsListResponse("search_query"),
            JsonResponse(new
            {
                jsonrpc = "2.0",
                id = 3,
                result = new
                {
                    content = new[]
                    {
                        new { type = "text", text = "quota exceeded" },
                    },
                    isError = true,
                },
            }));
        var provider = CreateProvider(handler, maxResults: 5);

        var ex = await Assert.ThrowsAsync<WebSearchProviderException>(() => provider.SearchAsync("quota test"));

        Assert.Equal("zai", ex.Provider);
        Assert.False(ex.CanRetrySameRequest);
        Assert.Contains("Do not retry", ex.Message, StringComparison.Ordinal);
        Assert.Contains("quota exceeded", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_ThrowsActionableExceptionForUnauthorizedHttpResponse()
    {
        var handler = new RecordingMcpHandler(new HttpResponseMessage(HttpStatusCode.Unauthorized)
        {
            ReasonPhrase = "Unauthorized",
            Content = new StringContent("invalid api key", Encoding.UTF8, "text/plain"),
        });
        var provider = CreateProvider(handler, maxResults: 5);

        var ex = await Assert.ThrowsAsync<WebSearchProviderException>(() => provider.SearchAsync("auth test"));

        Assert.Equal("zai", ex.Provider);
        Assert.Equal(401, ex.StatusCode);
        Assert.False(ex.CanRetrySameRequest);
        Assert.Contains("HTTP 401", ex.Message, StringComparison.Ordinal);
        Assert.Contains("invalid api key", ex.Message, StringComparison.Ordinal);
    }

    private static ZaiSearchProvider CreateProvider(RecordingMcpHandler handler, int maxResults)
    {
        return new ZaiSearchProvider(
            new HttpClient(handler),
            "test-zai-key",
            endpoint: null,
            toolName: null,
            maxResults,
            NullLogger<ZaiSearchProvider>.Instance);
    }

    private static HttpResponseMessage InitializeResponse(string sessionId)
    {
        return JsonResponse(new
        {
            jsonrpc = "2.0",
            id = 1,
            result = new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                serverInfo = new { name = "zai-web-search", version = "1.0" },
            },
        }, sessionId);
    }

    private static HttpResponseMessage AcceptedResponse()
    {
        return new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent(string.Empty),
        };
    }

    private static HttpResponseMessage ToolsListResponse(string argumentName)
    {
        return JsonResponse(new
        {
            jsonrpc = "2.0",
            id = 2,
            result = new
            {
                tools = new[]
                {
                    new
                    {
                        name = "webSearchPrime",
                        inputSchema = new
                        {
                            type = "object",
                            properties = new Dictionary<string, object>
                            {
                                [argumentName] = new { type = "string", description = "Search query" },
                            },
                            required = new[] { argumentName },
                        },
                    },
                },
            },
        });
    }

    private static HttpResponseMessage ToolCallResponse(string text)
    {
        return JsonResponse(new
        {
            jsonrpc = "2.0",
            id = 3,
            result = new
            {
                content = new[]
                {
                    new { type = "text", text },
                },
                isError = false,
            },
        });
    }

    private static HttpResponseMessage JsonResponse(object payload, string? sessionId = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(sessionId))
        {
            response.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
        }

        return response;
    }

    private static HttpResponseMessage SseResponse(object payload)
    {
        var json = JsonSerializer.Serialize(payload);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"event: message\ndata: {json}\n\n", Encoding.UTF8, "text/event-stream"),
        };
    }

    private sealed class RecordingMcpHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public RecordingMcpHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.RequestUri ?? new Uri("about:blank"),
                request.Headers.Authorization?.ToString() ?? string.Empty,
                request.Headers.Accept.Select(value => value.MediaType ?? string.Empty).ToList(),
                request.Content?.Headers.ContentType?.MediaType ?? string.Empty,
                request.Headers.TryGetValues("Mcp-Session-Id", out var sessionValues) ? sessionValues.FirstOrDefault() : null,
                body));

            if (_responses.Count == 0)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                {
                    Content = new StringContent("No test response queued"),
                };
            }

            return _responses.Dequeue();
        }
    }

    private sealed record RecordedRequest(
        Uri Uri,
        string Authorization,
        IReadOnlyList<string> Accept,
        string ContentType,
        string? SessionId,
        string Body);
}
