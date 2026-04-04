using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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

public sealed class SessionProfileReadEndpointTests : IDisposable
{
    private readonly string _contentRoot;

    public SessionProfileReadEndpointTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), $"quillforge-profile-read-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);
        Directory.CreateDirectory(Path.Combine(_contentRoot, ContentPaths.NarrativeRules));
        Directory.CreateDirectory(Path.Combine(_contentRoot, ContentPaths.WritingStyles));

        File.WriteAllText(Path.Combine(_contentRoot, ContentPaths.NarrativeRules, "default.md"), "# Default Rules");
        File.WriteAllText(Path.Combine(_contentRoot, ContentPaths.NarrativeRules, "session-rules.md"), "# Session Rules");
        File.WriteAllText(Path.Combine(_contentRoot, ContentPaths.WritingStyles, "default.md"), "# Default Style");
        File.WriteAllText(Path.Combine(_contentRoot, ContentPaths.WritingStyles, "session-style.md"), "# Session Style");
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task ProfilesEndpoint_UsesSessionSpecificActiveSelections()
    {
        await using var app = BuildApp();
        var sessionId = Guid.CreateVersion7();

        var json = await InvokeGetAsync(app, "/api/profiles", sessionId);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("session-profile", root.GetProperty("activeProfileId").GetString());
        Assert.Equal("session-conductor", root.GetProperty("activeConductor").GetString());
        Assert.Equal("session-lore", root.GetProperty("activeLore").GetString());
        Assert.Equal("session-rules", root.GetProperty("activeNarrativeRules").GetString());
        Assert.Equal("session-style", root.GetProperty("activeWritingStyle").GetString());
        Assert.Equal("default", root.GetProperty("defaultProfileId").GetString());
    }

    [Fact]
    public async Task NarrativeRulesEndpoint_UsesSessionSpecificActiveSelection()
    {
        await using var app = BuildApp();
        var sessionId = Guid.CreateVersion7();

        var json = await InvokeGetAsync(app, "/api/narrative-rules", sessionId);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("session-rules", document.RootElement.GetProperty("active").GetString());
    }

    [Fact]
    public async Task WritingStylesEndpoint_UsesSessionSpecificActiveSelection()
    {
        await using var app = BuildApp();
        var sessionId = Guid.CreateVersion7();

        var json = await InvokeGetAsync(app, "/api/writing-styles", sessionId);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("session-style", document.RootElement.GetProperty("active").GetString());
    }

    [Fact]
    public async Task StatusEndpoint_UsesSessionSpecificActiveSelections()
    {
        await using var app = BuildApp();
        var sessionId = Guid.CreateVersion7();

        var json = await InvokeGetAsync(app, "/api/status", sessionId);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("writer", root.GetProperty("mode").GetString());
        Assert.Equal("session-lore", root.GetProperty("loreSet").GetString());
        Assert.Equal("session-conductor", root.GetProperty("conductor").GetString());
        Assert.Equal("session-style", root.GetProperty("writingStyle").GetString());
    }

    private WebApplication BuildApp()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        builder.Services.AddRouting();
        builder.Services.AddLogging();
        builder.Services.AddSingleton(new AppConfig
        {
            Models = new ModelsConfig { Orchestrator = "orch-model" },
            Layout = new LayoutConfig { Active = "default-layout" },
            Roleplay = new RoleplayConfig
            {
                AiCharacter = "Guide",
                UserCharacter = "Author",
            },
            Persona = new PersonaConfig { Active = "default-conductor", MaxTokens = 2000 },
            Lore = new LoreConfig { Active = "default-lore" },
            NarrativeRules = new NarrativeRulesConfig { Active = "default-rules" },
            WritingStyle = new WritingStyleConfig { Active = "default-style" },
        });
        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<AutoUpdateService>(sp =>
            new AutoUpdateService(
                NullLogger<AutoUpdateService>.Instance,
                sp.GetRequiredService<IHttpClientFactory>(),
                sp.GetRequiredService<AppConfig>()));
        builder.Services.AddSingleton<IConductorStore>(new TestConductorStore());
        builder.Services.AddSingleton<ILoreStore>(new TestLoreStore());
        builder.Services.AddSingleton<INarrativeRulesStore>(new TestNarrativeRulesStore());
        builder.Services.AddSingleton<IWritingStyleStore>(new TestWritingStyleStore());
        builder.Services.AddSingleton<IProfileConfigService>(new TestProfileConfigService());
        builder.Services.AddSingleton<ISessionRuntimeService>(new TestSessionRuntimeService());
        builder.Services.AddSingleton<ISessionBootstrapService>(new NoOpSessionBootstrapService());
        builder.Services.AddSingleton<ISessionLifecycleService>(new NoOpSessionLifecycleService());
        builder.Services.AddSingleton<ISessionProfileReadService, SessionProfileReadService>();

        var app = builder.Build();
        app.MapModeEndpoints();
        app.MapProfileEndpoints(_contentRoot);
        app.MapStatusEndpoints();
        return app;
    }

    private static async Task<string> InvokeGetAsync(WebApplication app, string route, Guid? sessionId)
    {
        var endpoint = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .First(candidate =>
                RouteMatches(candidate.RoutePattern, route)
                && EndpointSupportsMethod(candidate, "GET"));

        var context = new DefaultHttpContext
        {
            RequestServices = app.Services,
        };
        context.Request.Method = "GET";
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = route;
        context.Request.QueryString = sessionId.HasValue
            ? new QueryString($"?sessionId={sessionId.Value}")
            : QueryString.Empty;
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

    private sealed class TestSessionRuntimeService : ISessionRuntimeService
    {
        public Task<SessionRuntimeState> LoadViewAsync(Guid? sessionId, CancellationToken ct = default)
        {
            var state = sessionId.HasValue
                ? new SessionRuntimeState
                {
                    SessionId = sessionId,
                    Mode = new ModeSelectionState
                    {
                        ActiveModeName = "writer",
                        ProjectName = "session-project",
                        CurrentFile = "scene-01.md",
                    },
                    Profile = new ProfileState
                    {
                        ProfileId = "session-profile",
                        ActiveConductor = "session-conductor",
                        ActiveLoreSet = "session-lore",
                        ActiveNarrativeRules = "session-rules",
                        ActiveWritingStyle = "session-style",
                    },
                }
                : new SessionRuntimeState
                {
                    Mode = new ModeSelectionState { ActiveModeName = "general" },
                    Profile = new ProfileState
                    {
                        ProfileId = "default",
                        ActiveConductor = "default-conductor",
                        ActiveLoreSet = "default-lore",
                        ActiveNarrativeRules = "default-rules",
                        ActiveWritingStyle = "default-style",
                    },
                };

            return Task.FromResult(state);
        }

        public Task<SessionMutationResult<SessionRuntimeState>> SetProfileAsync(Guid? sessionId, SetSessionProfileCommand command, CancellationToken ct = default)
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

    private sealed class TestProfileConfigService : IProfileConfigService
    {
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["default", "session-profile"]);

        public Task<string> GetDefaultProfileIdAsync(CancellationToken ct = default)
            => Task.FromResult("default");

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

    private sealed class TestConductorStore : IConductorStore
    {
        public Task<string> LoadAsync(string conductorName, int? maxTokens = null, CancellationToken ct = default)
            => Task.FromResult(conductorName == "session-conductor" ? "session conductor prompt" : "default conductor prompt");

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["default-conductor", "session-conductor"]);
    }

    private sealed class TestLoreStore : ILoreStore
    {
        public Task<IReadOnlyDictionary<string, string>> LoadLoreSetAsync(string loreSetName, CancellationToken ct = default)
        {
            IReadOnlyDictionary<string, string> lore = loreSetName == "session-lore"
                ? new Dictionary<string, string>
                {
                    ["entry-a.md"] = "Session lore entry A",
                    ["entry-b.md"] = "Session lore entry B",
                }
                : new Dictionary<string, string>
                {
                    ["default-entry.md"] = "Default lore entry",
                };

            return Task.FromResult(lore);
        }

        public Task<IReadOnlyList<string>> ListLoreSetsAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["default-lore", "session-lore"]);

        public Task<IReadOnlyList<(string FilePath, string Snippet)>> SearchAsync(string loreSetName, string query, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class TestNarrativeRulesStore : INarrativeRulesStore
    {
        public Task<string> LoadAsync(string rulesName, CancellationToken ct = default)
            => Task.FromResult($"rules:{rulesName}");

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["default-rules", "session-rules"]);
    }

    private sealed class TestWritingStyleStore : IWritingStyleStore
    {
        public Task<string> LoadAsync(string styleName, CancellationToken ct = default)
            => Task.FromResult($"style:{styleName}");

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["default-style", "session-style"]);
    }

    private sealed class NoOpSessionBootstrapService : ISessionBootstrapService
    {
        public Task<ConversationTree> CreateAsync(CreateSessionCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class NoOpSessionLifecycleService : ISessionLifecycleService
    {
        public Task<ConversationTree> ForkAsync(Guid sourceSessionId, Guid? messageId = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
