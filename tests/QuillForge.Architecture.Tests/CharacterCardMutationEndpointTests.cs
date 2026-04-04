using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Endpoints;
using QuillForge.Web.Services;

namespace QuillForge.Architecture.Tests;

public sealed class CharacterCardMutationEndpointTests
{
    [Fact]
    public async Task DeleteEndpoint_UsesCharacterCardCommandService()
    {
        var commandService = new RecordingCharacterCardCommandService
        {
            DeleteResult = true,
        };

        await using var app = BuildApp(commandService);

        var response = await InvokeAsync(app, "DELETE", "/api/character-cards/guide", null);

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("guide", commandService.LastDeletedFileName);
    }

    [Fact]
    public async Task ActivateEndpoint_UsesCharacterCardCommandService()
    {
        var sessionId = Guid.CreateVersion7();
        var commandService = new RecordingCharacterCardCommandService
        {
            ActivationResult = SessionMutationResult<SessionState>.Success(new SessionState
            {
                SessionId = sessionId,
                Roleplay = new RoleplayRuntimeState
                {
                    ActiveAiCharacter = "guide",
                    ActiveUserCharacter = "author",
                },
            }),
        };

        await using var app = BuildApp(commandService);

        var response = await InvokeAsync(
            app,
            "POST",
            "/api/character-cards/activate",
            $$"""{"sessionId":"{{sessionId}}","aiCharacter":"guide","userCharacter":"author"}""");

        Assert.Equal(200, response.StatusCode);
        Assert.NotNull(commandService.LastActivationCommand);
        Assert.Equal(sessionId, commandService.LastActivationCommand!.SessionId);
        Assert.True(commandService.LastActivationCommand.HasAiCharacterSelection);
        Assert.True(commandService.LastActivationCommand.HasUserCharacterSelection);
        Assert.Equal("guide", commandService.LastActivationCommand.AiCharacter);
        Assert.Equal("author", commandService.LastActivationCommand.UserCharacter);
    }

    private static WebApplication BuildApp(RecordingCharacterCardCommandService commandService)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        builder.Services.AddRouting();
        builder.Services.AddLogging();
        builder.Services.AddSingleton<ICharacterCardStore>(new TestCharacterCardStore());
        builder.Services.AddSingleton<ICharacterCardCommandService>(commandService);
        builder.Services.AddSingleton<ISessionStateService>(new NoOpRuntimeService());

        var app = builder.Build();
        app.MapCharacterCardEndpoints(Path.Combine(Path.GetTempPath(), $"quillforge-character-card-endpoints-{Guid.NewGuid():N}"));
        return app;
    }

    private static async Task<(int StatusCode, string Body)> InvokeAsync(
        WebApplication app,
        string method,
        string route,
        string? jsonBody)
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
        SetRouteValues(context, endpoint.RoutePattern, route);
        context.Response.Body = new MemoryStream();

        if (jsonBody is not null)
        {
            context.Request.ContentType = "application/json";
            var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
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

        var builtSegments = builtPath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var routeSegments = route.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (builtSegments.Length != routeSegments.Length)
        {
            return false;
        }

        for (var i = 0; i < builtSegments.Length; i++)
        {
            var builtSegment = builtSegments[i];
            if (builtSegment.StartsWith('{') && builtSegment.EndsWith('}'))
            {
                continue;
            }

            if (!string.Equals(builtSegment, routeSegments[i], StringComparison.Ordinal))
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

    private static void SetRouteValues(HttpContext context, RoutePattern pattern, string route)
    {
        var patternSegments = pattern.PathSegments;
        var routeSegments = route.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < patternSegments.Count && i < routeSegments.Length; i++)
        {
            foreach (var part in patternSegments[i].Parts)
            {
                if (part is RoutePatternParameterPart parameter)
                {
                    context.Request.RouteValues[parameter.Name] = routeSegments[i];
                }
            }
        }
    }

    private sealed class RecordingCharacterCardCommandService : ICharacterCardCommandService
    {
        public bool DeleteResult { get; init; }
        public SessionMutationResult<SessionState> ActivationResult { get; init; }
            = SessionMutationResult<SessionState>.Success(new SessionState());

        public string? LastDeletedFileName { get; private set; }
        public ActivateCharacterCardsCommand? LastActivationCommand { get; private set; }

        public Task<bool> DeleteAsync(string fileName, CancellationToken ct = default)
        {
            LastDeletedFileName = fileName;
            return Task.FromResult(DeleteResult);
        }

        public Task<SessionMutationResult<SessionState>> ActivateAsync(
            ActivateCharacterCardsCommand command,
            CancellationToken ct = default)
        {
            LastActivationCommand = command;
            return Task.FromResult(ActivationResult);
        }
    }

    private sealed class TestCharacterCardStore : ICharacterCardStore
    {
        public Task<CharacterCard?> LoadAsync(string fileName, CancellationToken ct = default)
            => Task.FromResult<CharacterCard?>(null);

        public Task SaveAsync(string fileName, CharacterCard card, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> DeleteAsync(string fileName, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CharacterCard>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CharacterCard>>([]);

        public string CardToPrompt(CharacterCard card)
            => card.Name;

        public CharacterCard NewTemplate(string name = "New Character")
            => new() { Name = name, FileName = name };

        public Task<CharacterCard> ImportTavernCardAsync(string pngPath, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class NoOpRuntimeService : ISessionStateService
    {
        public Task<SessionState> LoadViewAsync(Guid? sessionId, CancellationToken ct = default)
            => Task.FromResult(new SessionState { SessionId = sessionId });

        public Task<SessionMutationResult<SessionState>> SetProfileAsync(Guid? sessionId, SetSessionProfileCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> SetRoleplayAsync(Guid? sessionId, SetSessionRoleplayCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> SetModeAsync(Guid? sessionId, SetSessionModeCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> CaptureWriterPendingAsync(Guid? sessionId, CaptureWriterPendingCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<WriterPendingDecisionResult>> AcceptWriterPendingAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> RejectWriterPendingAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> UpdateNarrativeStateAsync(Guid? sessionId, UpdateNarrativeStateCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> SetActivePlotAsync(Guid? sessionId, SetActivePlotCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> ClearActivePlotAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class TestRequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
