using System.Text;
using System.Text.Json;
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

public sealed class WriterPendingEndpointTests
{
    [Fact]
    public async Task WriterPendingAcceptEndpoint_UsesRuntimeServiceAndReturnsSavedPath()
    {
        var runtimeService = new RecordingWriterRuntimeService();
        await using var app = BuildApp(runtimeService);
        var sessionId = Guid.CreateVersion7();

        var response = await InvokePostJsonAsync(
            app,
            "/api/writer/pending/accept",
            $$"""{"sessionId":"{{sessionId}}"}""");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(sessionId, runtimeService.LastAcceptedSessionId);

        using var document = JsonDocument.Parse(response.Body);
        var root = document.RootElement;
        Assert.Equal(sessionId, root.GetProperty("sessionId").GetGuid());
        Assert.Equal("accepted", root.GetProperty("status").GetString());
        Assert.Equal("story/novel/chapter-01.md", root.GetProperty("savedPath").GetString());
        Assert.Equal("Revised chapter draft".Length, root.GetProperty("contentLength").GetInt32());
    }

    [Fact]
    public async Task WriterPendingRejectEndpoint_UsesRuntimeServiceAndReturnsRejectedStatus()
    {
        var runtimeService = new RecordingWriterRuntimeService();
        await using var app = BuildApp(runtimeService);
        var sessionId = Guid.CreateVersion7();

        var response = await InvokePostJsonAsync(
            app,
            "/api/writer/pending/reject",
            $$"""{"sessionId":"{{sessionId}}"}""");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal(sessionId, runtimeService.LastRejectedSessionId);

        using var document = JsonDocument.Parse(response.Body);
        var root = document.RootElement;
        Assert.Equal(sessionId, root.GetProperty("sessionId").GetGuid());
        Assert.Equal("rejected", root.GetProperty("status").GetString());
    }

    [Fact]
    public async Task WriterPendingAcceptEndpoint_WithoutSessionId_ReturnsBadRequest()
    {
        var runtimeService = new RecordingWriterRuntimeService();
        await using var app = BuildApp(runtimeService);

        var response = await InvokePostJsonAsync(
            app,
            "/api/writer/pending/accept",
            """{}""");

        Assert.Equal(400, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        var root = document.RootElement;
        Assert.Equal("invalid_session_mutation", root.GetProperty("error").GetString());
        Assert.Equal("sessionId is required.", root.GetProperty("message").GetString());
        Assert.Null(runtimeService.LastAcceptedSessionId);
    }

    private static WebApplication BuildApp(ISessionStateService runtimeService)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        builder.Services.AddRouting();
        builder.Services.AddLogging();
        builder.Services.AddSingleton(runtimeService);

        var app = builder.Build();
        app.MapWriterEndpoints();
        return app;
    }

    private static async Task<EndpointResponse> InvokePostJsonAsync(WebApplication app, string route, string jsonBody)
    {
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .First(candidate =>
                RouteMatches(candidate.RoutePattern, route)
                && EndpointSupportsMethod(candidate, "POST"));

        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
        };
        context.Request.Method = "POST";
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = route;
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

    private static bool RouteMatches(RoutePattern pattern, string route)
    {
        var rawText = pattern.RawText;
        if (!string.IsNullOrWhiteSpace(rawText)
            && string.Equals(rawText.TrimStart('/'), route.TrimStart('/'), StringComparison.Ordinal))
        {
            return true;
        }

        var builtPath = "/" + string.Join(
            "/",
            pattern.PathSegments.Select(segment => string.Concat(segment.Parts.Select(part => part switch
            {
                RoutePatternLiteralPart literal => literal.Content,
                RoutePatternParameterPart parameter => $"{{{parameter.Name}}}",
                _ => string.Empty,
            }))));

        return string.Equals(builtPath, route, StringComparison.Ordinal);
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

    private sealed class RecordingWriterRuntimeService : ISessionStateService
    {
        public Guid? LastAcceptedSessionId { get; private set; }
        public Guid? LastRejectedSessionId { get; private set; }

        public Task<SessionState> LoadViewAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> SetProfileAsync(Guid? sessionId, SetSessionProfileCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> SetRoleplayAsync(Guid? sessionId, SetSessionRoleplayCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> SetModeAsync(Guid? sessionId, SetSessionModeCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<WriterPendingCaptureEvent>> CaptureWriterPendingAsync(Guid? sessionId, CaptureWriterPendingCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<WriterPendingContentAcceptedEvent>> AcceptWriterPendingAsync(Guid? sessionId, CancellationToken ct = default)
        {
            LastAcceptedSessionId = sessionId;
            return Task.FromResult(SessionMutationResult<WriterPendingContentAcceptedEvent>.Success(
                new WriterPendingContentAcceptedEvent(
                    sessionId,
                    "Revised chapter draft",
                    "story/novel/chapter-01.md")));
        }

        public Task<SessionMutationResult<WriterPendingContentRejectedEvent>> RejectWriterPendingAsync(Guid? sessionId, CancellationToken ct = default)
        {
            LastRejectedSessionId = sessionId;
            return Task.FromResult(SessionMutationResult<WriterPendingContentRejectedEvent>.Success(
                new WriterPendingContentRejectedEvent(
                    new SessionState
                    {
                        SessionId = sessionId,
                        Writer = new WriterRuntimeState
                        {
                            State = WriterState.Idle,
                        },
                    })));
        }

        public Task<SessionMutationResult<SessionState>> UpdateNarrativeStateAsync(Guid? sessionId, UpdateNarrativeStateCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> SetActivePlotAsync(Guid? sessionId, SetActivePlotCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> ClearActivePlotAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
