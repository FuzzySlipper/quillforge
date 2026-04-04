using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Endpoints;
using QuillForge.Web.Services;

namespace QuillForge.Architecture.Tests;

public sealed class SessionBootstrapMutationEndpointTests : IDisposable
{
    private readonly string _contentRoot;

    public SessionBootstrapMutationEndpointTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), $"quillforge-bootstrap-endpoints-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);
        Directory.CreateDirectory(Path.Combine(_contentRoot, "conductor"));
        Directory.CreateDirectory(Path.Combine(_contentRoot, "narrative-rules"));
        Directory.CreateDirectory(Path.Combine(_contentRoot, "writing-styles"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ModeEndpoint_WithoutSessionId_CreatesSessionBeforeMutation()
    {
        var runtimeService = new TestSessionRuntimeService();
        var bootstrapService = new TestSessionBootstrapService();
        await using var app = BuildApp(runtimeService, bootstrapService);

        var json = await InvokePostJsonAsync(app, "/api/mode", """{"mode":"writer","project":"novel"}""");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(bootstrapService.CreatedSessionId.ToString(), root.GetProperty("sessionId").GetString());
        Assert.Equal(bootstrapService.CreatedSessionId, runtimeService.LastModeSessionId);
        Assert.Equal("writer", root.GetProperty("mode").GetString());
        Assert.Equal("novel", root.GetProperty("project").GetString());
    }

    [Fact]
    public async Task ProfileSwitchEndpoint_WithoutSessionId_CreatesSessionBeforeMutation()
    {
        var runtimeService = new TestSessionRuntimeService();
        var bootstrapService = new TestSessionBootstrapService();
        await using var app = BuildApp(runtimeService, bootstrapService);

        var json = await InvokePostJsonAsync(app, "/api/profiles/switch", """{"conductor":"grim-conductor","lore":"grim-lore"}""");
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal(bootstrapService.CreatedSessionId.ToString(), root.GetProperty("sessionId").GetString());
        Assert.Equal(bootstrapService.CreatedSessionId, runtimeService.LastProfileSessionId);
        Assert.Equal("grim-conductor", root.GetProperty("activeConductor").GetString());
        Assert.Equal("grim-lore", root.GetProperty("activeLore").GetString());
    }

    [Fact]
    public async Task ProfileSwitchEndpoint_BusyMutation_DeletesAutoCreatedSession()
    {
        var runtimeService = new BusyProfileRuntimeService();
        var bootstrapService = new TestSessionBootstrapService();
        var lifecycleService = new TrackingSessionLifecycleService();
        await using var app = BuildApp(runtimeService, bootstrapService, lifecycleService);

        await InvokePostJsonAsync(app, "/api/profiles/switch", """{"conductor":"grim-conductor","lore":"grim-lore"}""");

        Assert.Equal(bootstrapService.CreatedSessionId, lifecycleService.LastDeletedSessionId);
    }

    private WebApplication BuildApp(
        ISessionRuntimeService runtimeService,
        TestSessionBootstrapService bootstrapService,
        ISessionLifecycleService? lifecycleService = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        builder.Services.AddRouting();
        builder.Services.AddLogging();
        builder.Services.AddSingleton(new AppConfig());
        builder.Services.AddSingleton<ISessionRuntimeService>(runtimeService);
        builder.Services.AddSingleton(bootstrapService);
        builder.Services.AddSingleton<ISessionBootstrapService>(sp => sp.GetRequiredService<TestSessionBootstrapService>());
        builder.Services.AddSingleton(lifecycleService ?? (ISessionLifecycleService)new NoOpSessionLifecycleService());
        builder.Services.AddSingleton<ISessionProfileReadService>(new NoOpSessionProfileReadService());
        builder.Services.AddSingleton<IProfileConfigService>(new NoOpProfileConfigService());
        builder.Services.AddSingleton<IAppConfigStore>(new NoOpAppConfigStore());
        builder.Services.AddSingleton<IContentFileService>(new NoOpContentFileService());
        builder.Services.AddSingleton<INarrativeRulesStore>(new NoOpNarrativeRulesStore());
        builder.Services.AddSingleton<IWritingStyleStore>(new NoOpWritingStyleStore());

        var app = builder.Build();
        app.MapModeEndpoints();
        app.MapProfileEndpoints(_contentRoot);
        return app;
    }

    private static async Task<string> InvokePostJsonAsync(WebApplication app, string route, string jsonBody)
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
        return await reader.ReadToEndAsync();
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

    private sealed class TestSessionBootstrapService : ISessionBootstrapService
    {
        public Guid CreatedSessionId { get; } = Guid.CreateVersion7();

        public Task<ConversationTree> CreateAsync(CreateSessionCommand command, CancellationToken ct = default)
        {
            var tree = new ConversationTree(
                command.SessionId ?? CreatedSessionId,
                command.Name,
                NullLogger<ConversationTree>.Instance);
            return Task.FromResult(tree);
        }
    }

    private sealed class TestSessionRuntimeService : ISessionRuntimeService
    {
        public Guid? LastModeSessionId { get; private set; }
        public Guid? LastProfileSessionId { get; private set; }

        public Task<SessionRuntimeState> LoadViewAsync(Guid? sessionId, CancellationToken ct = default)
            => Task.FromResult(new SessionRuntimeState
            {
                SessionId = sessionId,
                Profile = new ProfileState
                {
                    ProfileId = "default",
                    ActiveConductor = "default-conductor",
                    ActiveLoreSet = "default-lore",
                    ActiveNarrativeRules = "default-rules",
                    ActiveWritingStyle = "default-style",
                },
            });

        public Task<SessionMutationResult<SessionRuntimeState>> SetProfileAsync(Guid? sessionId, SetSessionProfileCommand command, CancellationToken ct = default)
        {
            LastProfileSessionId = sessionId;
            return Task.FromResult(SessionMutationResult<SessionRuntimeState>.Success(new SessionRuntimeState
            {
                SessionId = sessionId,
                Profile = new ProfileState
                {
                    ProfileId = command.ProfileId ?? "default",
                    ActiveConductor = command.Conductor ?? "default-conductor",
                    ActiveLoreSet = command.LoreSet ?? "default-lore",
                    ActiveNarrativeRules = command.NarrativeRules ?? "default-rules",
                    ActiveWritingStyle = command.WritingStyle ?? "default-style",
                },
            }));
        }

        public Task<SessionMutationResult<SessionRuntimeState>> SetModeAsync(Guid? sessionId, SetSessionModeCommand command, CancellationToken ct = default)
        {
            LastModeSessionId = sessionId;
            return Task.FromResult(SessionMutationResult<SessionRuntimeState>.Success(new SessionRuntimeState
            {
                SessionId = sessionId,
                Mode = new ModeSelectionState
                {
                    ActiveModeName = command.Mode,
                    ProjectName = command.Project,
                    CurrentFile = command.File,
                    Character = command.Character,
                },
                Profile = new ProfileState
                {
                    ProfileId = "default",
                    ActiveConductor = "default-conductor",
                    ActiveLoreSet = "default-lore",
                    ActiveNarrativeRules = "default-rules",
                    ActiveWritingStyle = "default-style",
                },
            }));
        }

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

    private sealed class TrackingSessionLifecycleService : ISessionLifecycleService
    {
        public Guid? LastDeletedSessionId { get; private set; }

        public Task<ConversationTree> ForkAsync(Guid sourceSessionId, Guid? messageId = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
        {
            LastDeletedSessionId = sessionId;
            return Task.CompletedTask;
        }
    }

    private sealed class BusyProfileRuntimeService : ISessionRuntimeService
    {
        public Task<SessionRuntimeState> LoadViewAsync(Guid? sessionId, CancellationToken ct = default)
            => Task.FromResult(new SessionRuntimeState { SessionId = sessionId });

        public Task<SessionMutationResult<SessionRuntimeState>> SetProfileAsync(Guid? sessionId, SetSessionProfileCommand command, CancellationToken ct = default)
            => Task.FromResult(SessionMutationResult<SessionRuntimeState>.Busy("session is busy"));

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

    private sealed class NoOpSessionProfileReadService : ISessionProfileReadService
    {
        public Task<SessionProfileReadView> LoadAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<QuillForge.Web.Contracts.ProfilesResponse> BuildProfilesResponseAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class NoOpProfileConfigService : IProfileConfigService
    {
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> GetDefaultProfileIdAsync(CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ResolvedProfileConfig> LoadResolvedAsync(string? profileId = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ResolvedProfileConfig> SaveAsync(string profileId, ProfileConfig config, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ResolvedProfileConfig> CloneAsync(string sourceProfileId, string targetProfileId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string profileId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProfileSelectionResult> SelectAsync(string profileId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProfileSelectionResult> SaveAndSelectAsync(string profileId, ProfileConfig config, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProfileState> BuildSessionProfileStateAsync(string? profileId = null, CancellationToken ct = default)
            => throw new NotSupportedException();
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

    private sealed class NoOpContentFileService : IContentFileService
    {
        public Task<bool> ExistsAsync(string relativePath, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<string> ReadAsync(string relativePath, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task WriteAsync(string relativePath, string content, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListAsync(string directory, string? pattern = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string relativePath, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<(string FilePath, string Snippet)>> SearchAsync(string directory, string query, CancellationToken ct = default)
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
