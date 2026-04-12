using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using QuillForge.Web.Contracts;

namespace QuillForge.ProviderHarness.Tests;

public sealed class HarnessDebugBridgeDriver
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly WebApplication _app;

    public HarnessDebugBridgeDriver(WebApplication app)
    {
        _app = app;
    }

    public Task<HarnessDebugSessionCreatedResponse> CreateSessionAsync(CancellationToken ct = default)
    {
        return InvokeJsonAsync<HarnessDebugSessionCreatedResponse>(
            "POST",
            "/api/debug/bridge/session/new",
            "{}",
            ct);
    }

    public Task<DebugBridgeSessionResponse> GetSessionAsync(Guid sessionId, CancellationToken ct = default)
    {
        return InvokeJsonAsync<DebugBridgeSessionResponse>(
            "GET",
            $"/api/debug/bridge/session/{sessionId}",
            null,
            ct);
    }

    public Task<DebugBridgeModeResponse> SetModeAsync(
        string mode,
        Guid? sessionId = null,
        string? project = null,
        string? file = null,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            sessionId,
            mode,
            project,
            file,
        });

        return InvokeJsonAsync<DebugBridgeModeResponse>(
            "POST",
            "/api/debug/bridge/mode",
            payload,
            ct);
    }

    public Task<DebugBridgeStreamResponse> StreamChatAsync(
        Guid sessionId,
        string message,
        string model,
        int maxTokens,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            sessionId,
            message,
            model,
            maxTokens,
        });

        return InvokeJsonAsync<DebugBridgeStreamResponse>(
            "POST",
            "/api/debug/bridge/chat/stream",
            payload,
            ct);
    }

    public Task<DebugBridgeForgeCreateResponse> CreateForgeProjectAsync(
        string projectName,
        string premise,
        CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(new
        {
            name = projectName,
            premise,
        });

        return InvokeJsonAsync<DebugBridgeForgeCreateResponse>(
            "POST",
            "/api/debug/bridge/forge/create",
            payload,
            ct);
    }

    public Task<DebugBridgeForgeRunResponse> RunForgeDesignAsync(
        string projectName,
        CancellationToken ct = default)
    {
        return InvokeJsonAsync<DebugBridgeForgeRunResponse>(
            "POST",
            $"/api/debug/bridge/forge/{projectName}/design",
            null,
            ct);
    }

    public Task<DebugBridgeForgeRunResponse> RunForgeStartAsync(
        string projectName,
        CancellationToken ct = default)
    {
        return InvokeJsonAsync<DebugBridgeForgeRunResponse>(
            "POST",
            $"/api/debug/bridge/forge/{projectName}/start",
            null,
            ct);
    }

    public Task<DebugBridgeForgeRunResponse> RunForgeApproveAsync(
        string projectName,
        CancellationToken ct = default)
    {
        return InvokeJsonAsync<DebugBridgeForgeRunResponse>(
            "POST",
            $"/api/debug/bridge/forge/{projectName}/approve",
            null,
            ct);
    }

    private async Task<T> InvokeJsonAsync<T>(
        string method,
        string route,
        string? jsonBody,
        CancellationToken ct)
    {
        var endpoint = ((IEndpointRouteBuilder)_app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .First(candidate =>
                RouteMatches(candidate.RoutePattern, route)
                && EndpointSupportsMethod(candidate, method));

        var context = new DefaultHttpContext
        {
            RequestServices = _app.Services,
            RequestAborted = ct,
        };
        context.Request.Method = method;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = route;
        context.Response.Body = new MemoryStream();
        ApplyRouteValues(context, endpoint.RoutePattern, route);

        if (jsonBody is not null)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = bodyBytes.Length;
            context.Request.Body = new MemoryStream(bodyBytes);
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new TestRequestBodyDetectionFeature());
        }

        var requestDelegate = endpoint.RequestDelegate
            ?? throw new InvalidOperationException($"Endpoint {route} has no request delegate.");
        await requestDelegate(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);

        if (context.Response.StatusCode >= 400)
        {
            throw new InvalidOperationException(
                $"Endpoint {route} returned {context.Response.StatusCode}: {body}");
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new InvalidOperationException($"Could not deserialize {typeof(T).Name} from endpoint {route}.");
    }

    private static bool RouteMatches(RoutePattern pattern, string route)
    {
        var rawText = pattern.RawText;
        if (!string.IsNullOrWhiteSpace(rawText)
            && string.Equals(rawText.TrimStart('/'), route.TrimStart('/'), StringComparison.Ordinal))
        {
            return true;
        }

        var patternSegments = pattern.PathSegments;
        var routeSegments = route.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (patternSegments.Count != routeSegments.Length)
        {
            return false;
        }

        for (var i = 0; i < patternSegments.Count; i++)
        {
            var literalParts = patternSegments[i].Parts.OfType<RoutePatternLiteralPart>().ToList();
            if (literalParts.Count == 0)
            {
                continue;
            }

            var literal = string.Concat(literalParts.Select(part => part.Content));
            if (!string.Equals(literal, routeSegments[i], StringComparison.Ordinal))
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

    private sealed class TestRequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}

public sealed record HarnessDebugSessionCreatedResponse
{
    public required Guid SessionId { get; init; }
    public required string Name { get; init; }
}
