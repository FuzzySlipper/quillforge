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
using QuillForge.Providers.Registry;
using QuillForge.Storage.FileSystem;
using QuillForge.Storage.Utilities;
using QuillForge.Web.Endpoints;

namespace QuillForge.Architecture.Tests;

public sealed class ProviderEndpointTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), $"quillforge-provider-endpoints-{Guid.NewGuid():N}");

    [Fact]
    public async Task CreateProvider_AcceptsModelFieldAndFillsOnlyBlankAgentAssignments()
    {
        var runtimeConfig = new AppConfig
        {
            Models = new ModelsConfig
            {
                Orchestrator = "",
                NarrativeDirector = "default",
                ProseWriter = "existing-prose",
            },
        };
        var appConfigStore = new TrackingAppConfigStore(runtimeConfig);
        await using var app = BuildApp(runtimeConfig, appConfigStore);

        var response = await InvokeJsonAsync(
            app,
            "POST",
            "/api/providers",
            """
            {
              "alias": "local",
              "type": "Ollama",
              "baseUrl": "http://localhost:11434",
              "model": "qwen2.5:14b"
            }
            """);

        Assert.Equal(200, response.StatusCode);
        var registry = app.Services.GetRequiredService<ProviderRegistry>();
        Assert.Equal("qwen2.5:14b", registry.GetConfig("local")?.DefaultModel);

        Assert.Equal("local", appConfigStore.Current.Models.Orchestrator);
        Assert.Equal("local", appConfigStore.Current.Models.NarrativeDirector);
        Assert.Equal("existing-prose", appConfigStore.Current.Models.ProseWriter);
        Assert.Equal("local", appConfigStore.Current.Models.GameIntentTranslator);

        Assert.Equal("local", runtimeConfig.Models.Orchestrator);
        Assert.Equal("local", runtimeConfig.Models.NarrativeDirector);
        Assert.Equal("existing-prose", runtimeConfig.Models.ProseWriter);
    }

    [Fact]
    public async Task UpdateProvider_LegacyDefaultModelFieldStillWorksAndFillsBlankAssignments()
    {
        var runtimeConfig = new AppConfig
        {
            Models = new ModelsConfig
            {
                Orchestrator = "default",
                ProseWriter = "writer-provider",
            },
        };
        var appConfigStore = new TrackingAppConfigStore(runtimeConfig);
        await using var app = BuildApp(runtimeConfig, appConfigStore);
        var registry = app.Services.GetRequiredService<ProviderRegistry>();
        registry.Register(new ProviderConfig
        {
            Alias = "claude",
            Type = ProviderType.Anthropic,
            ApiKey = "",
            DefaultModel = "claude-haiku-4-20250414",
        });

        var response = await InvokeJsonAsync(
            app,
            "PUT",
            "/api/providers/claude",
            """
            {
              "defaultModel": "claude-sonnet-4-20250514"
            }
            """);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("claude-sonnet-4-20250514", registry.GetConfig("claude")?.DefaultModel);
        Assert.Equal("claude", appConfigStore.Current.Models.Orchestrator);
        Assert.Equal("writer-provider", appConfigStore.Current.Models.ProseWriter);
        Assert.Equal("claude", runtimeConfig.Models.Orchestrator);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private WebApplication BuildApp(AppConfig runtimeConfig, TrackingAppConfigStore appConfigStore)
    {
        Directory.CreateDirectory(_tempRoot);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        builder.Services.AddRouting();
        builder.Services.AddLogging();
        builder.Services.AddSingleton(runtimeConfig);
        builder.Services.AddSingleton<IAppConfigStore>(appConfigStore);
        builder.Services.AddSingleton<ProviderFactory>();
        builder.Services.AddSingleton<ProviderRegistry>();
        builder.Services.AddSingleton(new AtomicFileWriter(NullLogger<AtomicFileWriter>.Instance));
        builder.Services.AddSingleton(sp => new EncryptedKeyStore(
            Path.Combine(_tempRoot, "data"),
            sp.GetRequiredService<AtomicFileWriter>(),
            NullLogger<EncryptedKeyStore>.Instance));
        builder.Services.AddSingleton(sp => new ProviderConfigStore(
            _tempRoot,
            sp.GetRequiredService<EncryptedKeyStore>(),
            sp.GetRequiredService<AtomicFileWriter>(),
            NullLogger<ProviderConfigStore>.Instance));

        var app = builder.Build();
        app.MapProviderEndpoints();
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

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return (context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    private static bool RouteMatches(RoutePattern pattern, string route)
    {
        var rawText = pattern.RawText;
        if (!string.IsNullOrWhiteSpace(rawText)
            && string.Equals(rawText.Trim('/'), route.Trim('/'), StringComparison.Ordinal))
        {
            return true;
        }

        var routeSegments = route.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (routeSegments.Length != pattern.PathSegments.Count)
        {
            return false;
        }

        for (var i = 0; i < pattern.PathSegments.Count; i++)
        {
            var segment = pattern.PathSegments[i];
            var routeSegment = routeSegments[i];

            var literalText = string.Concat(segment.Parts.OfType<RoutePatternLiteralPart>().Select(part => part.Content));
            var hasParameter = segment.Parts.OfType<RoutePatternParameterPart>().Any();
            if (hasParameter)
            {
                if (string.IsNullOrEmpty(routeSegment))
                {
                    return false;
                }

                continue;
            }

            if (!string.Equals(literalText, routeSegment, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static bool EndpointSupportsMethod(RouteEndpoint endpoint, string method)
    {
        var metadata = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
        return metadata is null || metadata.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase);
    }

    private static void ApplyRouteValues(HttpContext context, RoutePattern pattern, string route)
    {
        var routeSegments = route.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < pattern.PathSegments.Count && i < routeSegments.Length; i++)
        {
            foreach (var parameter in pattern.PathSegments[i].Parts.OfType<RoutePatternParameterPart>())
            {
                context.Request.RouteValues[parameter.Name] = routeSegments[i];
            }
        }
    }

    private sealed class TrackingAppConfigStore : IAppConfigStore
    {
        public TrackingAppConfigStore(AppConfig initialConfig)
        {
            Current = Clone(initialConfig);
        }

        public AppConfig Current { get; private set; }

        public Task<AppConfig> LoadAsync(CancellationToken ct = default) =>
            Task.FromResult(Clone(Current));

        public Task SaveAsync(AppConfig config, CancellationToken ct = default)
        {
            Current = Clone(config);
            return Task.CompletedTask;
        }

        public Task<AppConfig> UpdateAsync(Func<AppConfig, AppConfig> update, CancellationToken ct = default)
        {
            Current = Clone(update(Clone(Current)));
            return Task.FromResult(Clone(Current));
        }

        private static AppConfig Clone(AppConfig config) => config with
        {
            Models = config.Models with { },
        };
    }

    private sealed class TestRequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
