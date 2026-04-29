using System.Text;
using System.Text.Json;
using Den.RulesEngine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Endpoints;

namespace QuillForge.Architecture.Tests;

public sealed class GameEndpointTests
{
    [Fact]
    public void GameTemplateEndpoints_MapExpectedTemplateRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        using var app = builder.Build();

        app.MapGameTemplateEndpoints();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => (Pattern: endpoint.RoutePattern.RawText, Methods: endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.ToArray() ?? []))
            .ToArray();

        Assert.Contains(routes, route => route.Pattern == "/api/game-templates/" && route.Methods.Contains("GET"));
        Assert.Contains(routes, route => route.Pattern == "/api/game-templates/catalog" && route.Methods.Contains("GET"));
        Assert.Contains(routes, route => route.Pattern == "/api/game-templates/{templateId}" && route.Methods.Contains("GET"));
        Assert.Contains(routes, route => route.Pattern == "/api/game-templates/{templateId}" && route.Methods.Contains("PUT"));
        Assert.Contains(routes, route => route.Pattern == "/api/game-templates/{templateId}/clone" && route.Methods.Contains("POST"));
        Assert.Contains(routes, route => route.Pattern == "/api/game-templates/validate" && route.Methods.Contains("POST"));
        Assert.Contains(routes, route => route.Pattern == "/api/game-templates/{templateId}" && route.Methods.Contains("DELETE"));
    }

    [Fact]
    public void GameEndpoints_MapExpectedSessionScopedRoutes()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton<IGameBridgeService, FakeGameBridgeService>();
        builder.Services.AddSingleton<IGameInspectorService, FakeGameInspectorService>();
        builder.Services.AddSingleton<IGameDiagnosticLogService, FakeGameDiagnosticLogService>();
        using var app = builder.Build();

        app.MapGameEndpoints();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => (Pattern: endpoint.RoutePattern.RawText, Methods: endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.ToArray() ?? []))
            .ToArray();

        Assert.Contains(routes, route => route.Pattern == "/api/sessions/{sessionId:guid}/game/" && route.Methods.Contains("GET"));
        Assert.Contains(routes, route => route.Pattern == "/api/sessions/{sessionId:guid}/game/inspector" && route.Methods.Contains("GET"));
        Assert.Contains(routes, route => route.Pattern == "/api/sessions/{sessionId:guid}/game/diagnostics" && route.Methods.Contains("GET"));
        Assert.Contains(routes, route => route.Pattern == "/api/sessions/{sessionId:guid}/game/start" && route.Methods.Contains("POST"));
        Assert.Contains(routes, route => route.Pattern == "/api/sessions/{sessionId:guid}/game/actions" && route.Methods.Contains("POST"));
        Assert.Contains(routes, route => route.Pattern == "/api/sessions/{sessionId:guid}/game/messages" && route.Methods.Contains("POST"));
        Assert.Contains(routes, route => route.Pattern == "/api/sessions/{sessionId:guid}/game/direct-messages" && route.Methods.Contains("POST"));
        Assert.Contains(routes, route => route.Pattern == "/api/sessions/{sessionId:guid}/game/end" && route.Methods.Contains("POST"));
        Assert.Contains(routes, route => route.Pattern == "/api/sessions/{sessionId:guid}/game/abort" && route.Methods.Contains("POST"));
    }

    [Fact]
    public async Task PostPublicMessage_WhenRejected_ReturnsStructuredDiagnosticError()
    {
        var bridge = new FakeGameBridgeService
        {
            PublicMessageResult = SessionMutationResult<GameBridgeMutationResult>.Invalid(
                "public_channel_forbidden: Public channel message rejected: public_channel_forbidden."),
        };
        await using var app = BuildGameApp(bridge);
        var sessionId = Guid.CreateVersion7();

        var response = await InvokePostJsonAsync(
            app,
            $"/api/sessions/{sessionId}/game/messages",
            """{"participantId":"human-1","text":"hello"}""");

        Assert.Equal(400, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        var root = document.RootElement;
        Assert.Equal("game_mutation_invalid", root.GetProperty("error").GetString());
        Assert.Equal("public_channel_forbidden", root.GetProperty("reasonCode").GetString());
        Assert.Equal("post_game_public_message", root.GetProperty("operation").GetString());
        Assert.Equal("Public channel message rejected: public_channel_forbidden.", root.GetProperty("message").GetString());
        Assert.Contains("diagnostic log", root.GetProperty("diagnosticHint").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplication BuildGameApp(IGameBridgeService bridge)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });
        builder.Services.AddRouting();
        builder.Services.AddLogging();
        builder.Services.AddSingleton(bridge);
        builder.Services.AddSingleton<IGameInspectorService, FakeGameInspectorService>();
        builder.Services.AddSingleton<IGameDiagnosticLogService, FakeGameDiagnosticLogService>();
        var app = builder.Build();
        app.MapGameEndpoints();
        return app;
    }

    private static async Task<EndpointResponse> InvokePostJsonAsync(WebApplication app, string route, string jsonBody)
    {
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .First(candidate => RouteMatches(candidate.RoutePattern, route) && EndpointSupportsMethod(candidate, "POST"));

        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
        };
        context.Request.Method = "POST";
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = route;
        ApplyRouteValues(endpoint.RoutePattern, route, context.Request.RouteValues);
        context.Request.ContentType = "application/json";
        var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
        context.Request.ContentLength = bodyBytes.Length;
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new TestRequestBodyDetectionFeature());
        context.Response.Body = new MemoryStream();

        var requestDelegate = endpoint.RequestDelegate;
        Assert.NotNull(requestDelegate);
        await requestDelegate(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return new EndpointResponse(context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    private static void ApplyRouteValues(RoutePattern pattern, string route, RouteValueDictionary routeValues)
    {
        var routeSegments = route.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < pattern.PathSegments.Count && i < routeSegments.Length; i++)
        {
            var segment = pattern.PathSegments[i];
            if (segment.Parts.Count == 1 && segment.Parts[0] is RoutePatternParameterPart parameter)
            {
                routeValues[parameter.Name] = routeSegments[i];
            }
        }
    }

    private static bool RouteMatches(RoutePattern pattern, string route)
    {
        var routeSegments = route.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pattern.PathSegments.Count != routeSegments.Length)
        {
            return false;
        }

        for (var i = 0; i < pattern.PathSegments.Count; i++)
        {
            var segment = pattern.PathSegments[i];
            if (segment.Parts.Count == 1 && segment.Parts[0] is RoutePatternParameterPart)
            {
                continue;
            }

            var literal = string.Concat(segment.Parts.Select(part => part switch
            {
                RoutePatternLiteralPart literalPart => literalPart.Content,
                _ => string.Empty,
            }));
            if (!string.Equals(literal, routeSegments[i], StringComparison.OrdinalIgnoreCase))
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

    private sealed record EndpointResponse(int StatusCode, string Body);

    private sealed class TestRequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }

    private sealed class FakeGameInspectorService : IGameInspectorService
    {
        public Task<GameInspectorProjection> GetProjectionAsync(Guid sessionId, int promptEnvelopeLimit = 10, CancellationToken ct = default) =>
            Task.FromResult(new GameInspectorProjection { SessionId = sessionId, HasGame = false });
    }

    private sealed class FakeGameDiagnosticLogService : IGameDiagnosticLogService
    {
        public Task<GameDiagnosticLogProjection> GetLogAsync(Guid sessionId, int promptPreviewCharacters = 1200, CancellationToken ct = default) =>
            Task.FromResult(new GameDiagnosticLogProjection
            {
                SessionId = sessionId,
                HasGame = false,
                PrivacyNotice = GameDiagnosticLogService.PrivacyNotice,
                Events = [],
            });
    }

    private sealed class FakeGameBridgeService : IGameBridgeService
    {
        public SessionMutationResult<GameBridgeMutationResult>? PublicMessageResult { get; init; }

        public Task<GameBridgeView> GetViewAsync(Guid sessionId, string? participantId = null, CancellationToken ct = default) =>
            Task.FromResult(EmptyView());

        public Task<SessionMutationResult<GameBridgeMutationResult>> StartFromTemplateAsync(Guid sessionId, StartGameFromTemplateCommand command, CancellationToken ct = default) =>
            Success();

        public Task<SessionMutationResult<GameBridgeMutationResult>> SubmitTypedActionAsync(Guid sessionId, SubmitGameTypedActionCommand command, CancellationToken ct = default) =>
            Success();

        public Task<SessionMutationResult<GameBridgeMutationResult>> SubmitTextActionAsync(Guid sessionId, SubmitGameTextActionCommand command, CancellationToken ct = default) =>
            Success();

        public Task<SessionMutationResult<GameBridgeMutationResult>> PostPublicMessageAsync(Guid sessionId, PostGameRuntimePublicMessageCommand command, CancellationToken ct = default) =>
            Task.FromResult(PublicMessageResult ?? SuccessResult());

        public Task<SessionMutationResult<GameBridgeMutationResult>> SendDirectMessageAsync(Guid sessionId, SendGameRuntimeDirectMessageCommand command, CancellationToken ct = default) =>
            Success();

        public Task<SessionMutationResult<GameBridgeMutationResult>> EndAsync(Guid sessionId, EndGameBridgeCommand command, CancellationToken ct = default) =>
            Success();

        public Task<SessionMutationResult<GameBridgeMutationResult>> AbortAsync(Guid sessionId, AbortGameRuntimeCommand command, CancellationToken ct = default) =>
            Success();

        private static Task<SessionMutationResult<GameBridgeMutationResult>> Success() =>
            Task.FromResult(SuccessResult());

        private static SessionMutationResult<GameBridgeMutationResult> SuccessResult()
        {
            var view = EmptyView();
            return SessionMutationResult<GameBridgeMutationResult>.Success(new GameBridgeMutationResult(view, [], [], []));
        }

        private static GameBridgeView EmptyView() =>
            new(GameRuntimeStatus.NotStarted, null, null, null, null, null, null, null, [], new GameBridgePublicView([], []), null);
    }
}
