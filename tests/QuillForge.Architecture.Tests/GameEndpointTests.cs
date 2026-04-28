using Den.RulesEngine;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
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
        using var app = builder.Build();

        app.MapGameEndpoints();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Select(endpoint => (Pattern: endpoint.RoutePattern.RawText, Methods: endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods.ToArray() ?? []))
            .ToArray();

        Assert.Contains(routes, route => route.Pattern == "/api/sessions/{sessionId:guid}/game/" && route.Methods.Contains("GET"));
        Assert.Contains(routes, route => route.Pattern == "/api/sessions/{sessionId:guid}/game/start" && route.Methods.Contains("POST"));
        Assert.Contains(routes, route => route.Pattern == "/api/sessions/{sessionId:guid}/game/actions" && route.Methods.Contains("POST"));
        Assert.Contains(routes, route => route.Pattern == "/api/sessions/{sessionId:guid}/game/messages" && route.Methods.Contains("POST"));
        Assert.Contains(routes, route => route.Pattern == "/api/sessions/{sessionId:guid}/game/direct-messages" && route.Methods.Contains("POST"));
        Assert.Contains(routes, route => route.Pattern == "/api/sessions/{sessionId:guid}/game/end" && route.Methods.Contains("POST"));
        Assert.Contains(routes, route => route.Pattern == "/api/sessions/{sessionId:guid}/game/abort" && route.Methods.Contains("POST"));
    }

    private sealed class FakeGameBridgeService : IGameBridgeService
    {
        public Task<GameBridgeView> GetViewAsync(Guid sessionId, string? participantId = null, CancellationToken ct = default) =>
            Task.FromResult(EmptyView());

        public Task<SessionMutationResult<GameBridgeMutationResult>> StartFromTemplateAsync(Guid sessionId, StartGameFromTemplateCommand command, CancellationToken ct = default) =>
            Success();

        public Task<SessionMutationResult<GameBridgeMutationResult>> SubmitTypedActionAsync(Guid sessionId, SubmitGameTypedActionCommand command, CancellationToken ct = default) =>
            Success();

        public Task<SessionMutationResult<GameBridgeMutationResult>> SubmitTextActionAsync(Guid sessionId, SubmitGameTextActionCommand command, CancellationToken ct = default) =>
            Success();

        public Task<SessionMutationResult<GameBridgeMutationResult>> PostPublicMessageAsync(Guid sessionId, PostGameRuntimePublicMessageCommand command, CancellationToken ct = default) =>
            Success();

        public Task<SessionMutationResult<GameBridgeMutationResult>> SendDirectMessageAsync(Guid sessionId, SendGameRuntimeDirectMessageCommand command, CancellationToken ct = default) =>
            Success();

        public Task<SessionMutationResult<GameBridgeMutationResult>> EndAsync(Guid sessionId, EndGameBridgeCommand command, CancellationToken ct = default) =>
            Success();

        public Task<SessionMutationResult<GameBridgeMutationResult>> AbortAsync(Guid sessionId, AbortGameRuntimeCommand command, CancellationToken ct = default) =>
            Success();

        private static Task<SessionMutationResult<GameBridgeMutationResult>> Success()
        {
            var view = EmptyView();
            return Task.FromResult(SessionMutationResult<GameBridgeMutationResult>.Success(new GameBridgeMutationResult(view, [], [], [])));
        }

        private static GameBridgeView EmptyView() =>
            new(GameRuntimeStatus.NotStarted, null, null, null, null, null, null, null, [], new GameBridgePublicView([], []), null);
    }
}
