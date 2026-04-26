using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Endpoints;

namespace QuillForge.Architecture.Tests;

public sealed class AppSettingsEndpointTests
{
    [Fact]
    public async Task GetAppSettings_ReturnsSanitizedWebSearchSettings()
    {
        var runtimeConfig = new AppConfig
        {
            WebSearch = new WebSearchConfig
            {
                Enabled = true,
                Provider = "z_ai",
                SearxngUrl = "http://search.local:8080",
                TavilyApiKey = "tvly-secret",
                BraveApiKey = "brave-secret",
                GoogleApiKey = "google-secret",
                GoogleCxId = "cx-123",
                ZaiApiKey = "zai-secret",
                ZaiMcpEndpoint = "https://api.z.ai/api/mcp/web_search_prime/mcp",
                ZaiMcpToolName = "webSearchPrime",
                MaxResults = 12,
            }
        };
        await using var app = BuildApp(runtimeConfig, new TrackingAppConfigStore(runtimeConfig));

        var response = await InvokeJsonAsync(app, "GET", "/api/app-settings");

        Assert.Equal(200, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        var webSearch = document.RootElement.GetProperty("webSearch");
        Assert.True(webSearch.GetProperty("enabled").GetBoolean());
        Assert.Equal("zai", webSearch.GetProperty("provider").GetString());
        Assert.True(webSearch.GetProperty("tavilyApiKeySet").GetBoolean());
        Assert.True(webSearch.GetProperty("braveApiKeySet").GetBoolean());
        Assert.True(webSearch.GetProperty("googleApiKeySet").GetBoolean());
        Assert.True(webSearch.GetProperty("zaiApiKeySet").GetBoolean());
        Assert.Equal("cx-123", webSearch.GetProperty("googleCxId").GetString());
        Assert.Equal(12, webSearch.GetProperty("maxResults").GetInt32());
        Assert.DoesNotContain("tvly-secret", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("brave-secret", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("google-secret", response.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("zai-secret", response.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateWebSearchSettings_PersistsThroughStoreAndSyncsRuntimeConfig()
    {
        var runtimeConfig = new AppConfig
        {
            WebSearch = new WebSearchConfig
            {
                Enabled = false,
                Provider = "searxng",
                TavilyApiKey = "keep-tavily",
                BraveApiKey = "clear-brave",
                MaxResults = 50,
            }
        };
        var store = new TrackingAppConfigStore(runtimeConfig);
        await using var app = BuildApp(runtimeConfig, store);

        var response = await InvokeJsonAsync(
            app,
            "PUT",
            "/api/app-settings/web-search",
            """
            {
              "enabled": true,
              "provider": "z-ai",
              "maxResults": 12,
              "tavilyApiKey": "",
              "clearBraveApiKey": true,
              "zaiApiKey": "new-zai-key",
              "zaiMcpEndpoint": " https://custom.example/mcp ",
              "zaiMcpToolName": " webSearchPrime "
            }
            """);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(1, store.UpdateCount);
        Assert.True(store.Config.WebSearch.Enabled);
        Assert.Equal("zai", store.Config.WebSearch.Provider);
        Assert.Equal(12, store.Config.WebSearch.MaxResults);
        Assert.Equal("keep-tavily", store.Config.WebSearch.TavilyApiKey);
        Assert.Null(store.Config.WebSearch.BraveApiKey);
        Assert.Equal("new-zai-key", store.Config.WebSearch.ZaiApiKey);
        Assert.Equal("https://custom.example/mcp", store.Config.WebSearch.ZaiMcpEndpoint);
        Assert.Equal("webSearchPrime", store.Config.WebSearch.ZaiMcpToolName);
        Assert.Equal("zai", runtimeConfig.WebSearch.Provider);
        Assert.Equal(12, runtimeConfig.WebSearch.MaxResults);

        using var document = JsonDocument.Parse(response.Body);
        var webSearch = document.RootElement.GetProperty("webSearch");
        Assert.Equal("zai", webSearch.GetProperty("provider").GetString());
        Assert.True(webSearch.GetProperty("zaiApiKeySet").GetBoolean());
        Assert.False(webSearch.GetProperty("braveApiKeySet").GetBoolean());
    }

    [Theory]
    [InlineData("{ \"provider\": \"unknown\" }")]
    [InlineData("{ \"maxResults\": 0 }")]
    [InlineData("{ \"maxResults\": 101 }")]
    [InlineData("{ \"zaiMcpEndpoint\": \"not a uri\" }")]
    public async Task UpdateWebSearchSettings_RejectsInvalidInput(string body)
    {
        var runtimeConfig = new AppConfig();
        var store = new TrackingAppConfigStore(runtimeConfig);
        await using var app = BuildApp(runtimeConfig, store);

        var response = await InvokeJsonAsync(app, "PUT", "/api/app-settings/web-search", body);

        Assert.Equal(400, response.StatusCode);
        Assert.Equal(0, store.UpdateCount);
    }

    private static WebApplication BuildApp(AppConfig runtimeConfig, IAppConfigStore appConfigStore)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        builder.Services.AddRouting();
        builder.Services.AddLogging();
        builder.Services.AddSingleton(runtimeConfig);
        builder.Services.AddSingleton(appConfigStore);
        builder.Services.AddSingleton(NullLogger<AppConfig>.Instance);

        var app = builder.Build();
        app.MapAppSettingsEndpoints();
        return app;
    }

    private static async Task<(int StatusCode, string Body)> InvokeJsonAsync(
        WebApplication app,
        string method,
        string route,
        string? jsonBody = null)
    {
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .First(candidate =>
                RouteMatches(candidate.RoutePattern, route)
                && EndpointSupportsMethod(candidate, method));

        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
        };
        context.Request.Method = method;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = route;
        ApplyRouteValues(context, endpoint.RoutePattern, route);
        context.Response.Body = new MemoryStream();

        if (jsonBody is not null)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = bodyBytes.Length;
            context.Request.Body = new MemoryStream(bodyBytes);
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new TestRequestBodyDetectionFeature());
        }

        var requestDelegate = endpoint.RequestDelegate;
        Assert.NotNull(requestDelegate);

        await requestDelegate(context);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }

    private static bool EndpointSupportsMethod(RouteEndpoint endpoint, string method)
    {
        var methodMetadata = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>();
        return methodMetadata is null
            || methodMetadata.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase);
    }

    private static bool RouteMatches(RoutePattern pattern, string route)
    {
        var routeSegments = route.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var patternSegments = pattern.PathSegments;
        if (routeSegments.Length != patternSegments.Count)
        {
            return false;
        }

        for (var i = 0; i < patternSegments.Count; i++)
        {
            var part = patternSegments[i].Parts.Single();
            if (part is RoutePatternLiteralPart literal)
            {
                if (!string.Equals(literal.Content, routeSegments[i], StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static void ApplyRouteValues(HttpContext context, RoutePattern pattern, string route)
    {
        var routeSegments = route.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < pattern.PathSegments.Count; i++)
        {
            var part = pattern.PathSegments[i].Parts.Single();
            if (part is RoutePatternParameterPart parameter)
            {
                context.Request.RouteValues[parameter.Name] = routeSegments[i];
            }
        }
    }

    private sealed class TestRequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    private sealed class TrackingAppConfigStore : IAppConfigStore
    {
        public TrackingAppConfigStore(AppConfig config)
        {
            Config = config;
        }

        public AppConfig Config { get; private set; }

        public int UpdateCount { get; private set; }

        public Task<AppConfig> LoadAsync(CancellationToken ct = default)
        {
            return Task.FromResult(Config);
        }

        public Task SaveAsync(AppConfig config, CancellationToken ct = default)
        {
            Config = config;
            return Task.CompletedTask;
        }

        public Task<AppConfig> UpdateAsync(Func<AppConfig, AppConfig> update, CancellationToken ct = default)
        {
            UpdateCount++;
            Config = update(Config);
            return Task.FromResult(Config);
        }
    }
}
