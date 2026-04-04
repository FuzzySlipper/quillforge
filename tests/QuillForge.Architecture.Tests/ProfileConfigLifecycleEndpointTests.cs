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
using QuillForge.Web.Contracts;
using QuillForge.Web.Endpoints;
using QuillForge.Web.Services;

namespace QuillForge.Architecture.Tests;

public sealed class ProfileConfigLifecycleEndpointTests
{
    [Fact]
    public async Task CloneEndpoint_ReturnsClonedProfile()
    {
        var profileService = new TrackingProfileConfigService();
        await using var app = BuildApp(profileService);

        var response = await InvokeJsonAsync(
            app,
            "POST",
            "/api/profile-configs/source/clone",
            JsonSerializer.Serialize(new CloneProfileConfigRequest
            {
                TargetProfileId = "copy",
            }));

        Assert.Equal(200, response.StatusCode);
        using var document = JsonDocument.Parse(response.Body);
        Assert.Equal("copy", document.RootElement.GetProperty("profileId").GetString());
        Assert.Equal("copy", profileService.LastCloneTargetProfileId);
    }

    [Fact]
    public async Task DeleteEndpoint_RejectsDefaultProfile()
    {
        var profileService = new TrackingProfileConfigService
        {
            DeleteException = new InvalidOperationException("Cannot delete default profile default."),
        };
        await using var app = BuildApp(profileService);

        var response = await InvokeJsonAsync(app, "DELETE", "/api/profile-configs/default");

        Assert.Equal(409, response.StatusCode);
    }

    [Fact]
    public async Task DeleteEndpoint_RejectsInUseProfile()
    {
        var profileService = new TrackingProfileConfigService
        {
            DeleteException = new InvalidOperationException("Cannot delete profile grim because it is referenced by 1 persisted session(s)."),
        };
        await using var app = BuildApp(profileService);

        var response = await InvokeJsonAsync(app, "DELETE", "/api/profile-configs/grim");

        Assert.Equal(409, response.StatusCode);
    }

    private static WebApplication BuildApp(TrackingProfileConfigService profileService)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        builder.Services.AddRouting();
        builder.Services.AddLogging();
        builder.Services.AddSingleton<IProfileConfigService>(profileService);
        builder.Services.AddSingleton<IContentFileService>(new NoOpContentFileService());
        builder.Services.AddSingleton<ISessionProfileReadService>(new NoOpSessionProfileReadService());
        builder.Services.AddSingleton<ISessionBootstrapService>(new NoOpSessionBootstrapService());
        builder.Services.AddSingleton<ISessionRuntimeService>(new NoOpSessionRuntimeService());
        builder.Services.AddSingleton<ISessionLifecycleService>(new NoOpSessionLifecycleService());
        builder.Services.AddSingleton(new AppConfig());
        builder.Services.AddSingleton<IAppConfigStore>(new NoOpAppConfigStore());
        builder.Services.AddSingleton<INarrativeRulesStore>(new NoOpNarrativeRulesStore());
        builder.Services.AddSingleton<IWritingStyleStore>(new NoOpWritingStyleStore());

        var app = builder.Build();
        app.MapProfileEndpoints(Path.Combine(Path.GetTempPath(), $"quillforge-profile-lifecycle-{Guid.NewGuid():N}"));
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
            && string.Equals(rawText.TrimStart('/'), route.TrimStart('/'), StringComparison.Ordinal))
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

    private sealed class TrackingProfileConfigService : IProfileConfigService
    {
        public string? LastCloneTargetProfileId { get; private set; }
        public Exception? DeleteException { get; init; }

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> GetDefaultProfileIdAsync(CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ResolvedProfileConfig> LoadResolvedAsync(string? profileId = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ResolvedProfileConfig> SaveAsync(string profileId, ProfileConfig config, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ResolvedProfileConfig> CloneAsync(string sourceProfileId, string targetProfileId, CancellationToken ct = default)
        {
            LastCloneTargetProfileId = targetProfileId;
            return Task.FromResult(new ResolvedProfileConfig
            {
                ProfileId = targetProfileId,
                Config = new ProfileConfig
                {
                    Conductor = "cloned-conductor",
                    LoreSet = "cloned-lore",
                    NarrativeRules = "cloned-rules",
                    WritingStyle = "cloned-style",
                },
                Persisted = true,
            });
        }

        public Task DeleteAsync(string profileId, CancellationToken ct = default)
        {
            if (DeleteException is not null)
            {
                throw DeleteException;
            }

            return Task.CompletedTask;
        }

        public Task<ProfileSelectionResult> SelectAsync(string profileId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProfileSelectionResult> SaveAndSelectAsync(string profileId, ProfileConfig config, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProfileState> BuildSessionProfileStateAsync(string? profileId = null, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class NoOpContentFileService : IContentFileService
    {
        public Task<string> ReadAsync(string relativePath, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task WriteAsync(string relativePath, string content, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListAsync(string directory, string? pattern = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string relativePath, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<(string FilePath, string Snippet)>> SearchAsync(string directory, string query, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class NoOpSessionProfileReadService : ISessionProfileReadService
    {
        public Task<SessionProfileReadView> LoadAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProfilesResponse> BuildProfilesResponseAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class NoOpSessionBootstrapService : ISessionBootstrapService
    {
        public Task<ConversationTree> CreateAsync(CreateSessionCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class NoOpSessionRuntimeService : ISessionRuntimeService
    {
        public Task<SessionRuntimeState> LoadViewAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionRuntimeState>> SetProfileAsync(Guid? sessionId, SetSessionProfileCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionRuntimeState>> SetRoleplayAsync(Guid? sessionId, SetSessionRoleplayCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionRuntimeState>> SetModeAsync(Guid? sessionId, SetSessionModeCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionRuntimeState>> CaptureWriterPendingAsync(Guid? sessionId, CaptureWriterPendingCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<WriterPendingDecisionResult>> AcceptWriterPendingAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionRuntimeState>> RejectWriterPendingAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionRuntimeState>> UpdateNarrativeStateAsync(Guid? sessionId, UpdateNarrativeStateCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionRuntimeState>> SetActivePlotAsync(Guid? sessionId, SetActivePlotCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionRuntimeState>> ClearActivePlotAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class NoOpSessionLifecycleService : ISessionLifecycleService
    {
        public Task<ConversationTree> ForkAsync(Guid sourceSessionId, Guid? messageId = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private sealed class NoOpAppConfigStore : IAppConfigStore
    {
        public Task<AppConfig> LoadAsync(CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task SaveAsync(AppConfig config, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<AppConfig> UpdateAsync(Func<AppConfig, AppConfig> update, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class NoOpNarrativeRulesStore : INarrativeRulesStore
    {
        public Task<string> LoadAsync(string rulesName, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class NoOpWritingStyleStore : IWritingStyleStore
    {
        public Task<string> LoadAsync(string styleName, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class TestRequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
