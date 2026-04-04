using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.Utilities;
using QuillForge.Web.Endpoints;
using QuillForge.Web.Services;

namespace QuillForge.Architecture.Tests;

public sealed class InteractivePreparedContextEndpointTests : IDisposable
{
    private readonly string _contentRoot;

    public InteractivePreparedContextEndpointTests()
    {
        _contentRoot = Path.Combine(Path.GetTempPath(), $"quillforge-prepared-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_contentRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
    }

    [Fact]
    public async Task DebugBridgeChat_UsesPreparedInteractiveRequestConductor()
    {
        var preparedService = new RecordingPreparedContextService();
        var conductorStore = new RecordingConductorStore();
        var completion = new ScriptedCompletionService();
        completion.EnqueueText("Bridge reply");

        await using var app = BuildApp(preparedService, conductorStore, completion);

        var sessionId = Guid.CreateVersion7();
        var response = await InvokePostJsonAsync(
            app,
            "/api/debug/bridge/chat",
            $$"""{"sessionId":"{{sessionId}}","message":"hello"}""");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("prepared-conductor", conductorStore.LastLoadedConductor);
        Assert.Equal(1, preparedService.PrepareCallCount);
        Assert.Equal(sessionId, preparedService.LastSessionId);
        Assert.Null(preparedService.LastAssistantResponse);
    }

    [Fact]
    public async Task ProbeEndpoint_UsesPreparedInteractiveRequestConductor()
    {
        var preparedService = new RecordingPreparedContextService();
        var conductorStore = new RecordingConductorStore();
        var completion = new ScriptedCompletionService();
        completion.EnqueueText("Probe output");

        await using var app = BuildApp(preparedService, conductorStore, completion);

        var response = await InvokePostJsonAsync(app, "/api/probe", "{}");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("prepared-conductor", conductorStore.LastLoadedConductor);
        Assert.Equal(1, preparedService.PrepareCallCount);
    }

    private WebApplication BuildApp(
        RecordingPreparedContextService preparedService,
        RecordingConductorStore conductorStore,
        ScriptedCompletionService completionService)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        builder.Services.AddRouting();
        builder.Services.AddLogging();

        var appConfig = new AppConfig
        {
            Persona = new PersonaConfig { MaxTokens = 2000 },
        };

        builder.Services.AddSingleton(appConfig);
        builder.Services.AddSingleton<ICompletionService>(completionService);
        builder.Services.AddSingleton(preparedService);
        builder.Services.AddSingleton<ISessionProfileReadService>(sp => sp.GetRequiredService<RecordingPreparedContextService>());
        builder.Services.AddSingleton<IConductorStore>(conductorStore);
        builder.Services.AddSingleton<IInteractiveSessionContextService>(new NoOpInteractiveSessionContextService());
        builder.Services.AddSingleton<ISessionStateService>(new NoOpRuntimeService());
        builder.Services.AddSingleton<ISessionBootstrapService>(new TestSessionBootstrapService());
        builder.Services.AddSingleton<ISessionStore>(new InMemorySessionStore());
        builder.Services.AddSingleton(new AtomicFileWriter(NullLogger<AtomicFileWriter>.Instance));

        builder.Services.AddSingleton(sp =>
            new ToolLoop(
                sp.GetRequiredService<ICompletionService>(),
                new ContinuationStrategy(NullLogger<ContinuationStrategy>.Instance),
                NullLogger<ToolLoop>.Instance,
                sp.GetRequiredService<AppConfig>()));

        builder.Services.AddSingleton(sp =>
            new OrchestratorAgent(
                sp.GetRequiredService<ToolLoop>(),
                [new GeneralMode()],
                sp.GetRequiredService<IConductorStore>(),
                sp.GetRequiredService<IInteractiveSessionContextService>(),
                sp.GetRequiredService<AppConfig>(),
                NullLogger<OrchestratorAgent>.Instance));

        var app = builder.Build();
        app.MapChatEndpoints();
        app.MapDebugBridgeEndpoints();
        app.MapProbeEndpoints(_contentRoot);
        return app;
    }

    private static async Task<(int StatusCode, string Body)> InvokePostJsonAsync(
        WebApplication app,
        string route,
        string jsonBody)
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

        return string.Equals(builtPath, route, StringComparison.Ordinal);
    }

    private static bool EndpointSupportsMethod(RouteEndpoint endpoint, string method)
    {
        var metadata = endpoint.Metadata.GetMetadata<HttpMethodMetadata>();
        return metadata is null || metadata.HttpMethods.Contains(method, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class RecordingPreparedContextService : ISessionProfileReadService
    {
        public int PrepareCallCount { get; private set; }
        public Guid? LastSessionId { get; private set; }
        public string? LastRequestedConductor { get; private set; }
        public string? LastAssistantResponse { get; private set; }

        public Task<SessionProfileReadView> LoadAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<QuillForge.Web.Contracts.ProfilesResponse> BuildProfilesResponseAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<PreparedInteractiveRequest> PrepareInteractiveRequestAsync(
            Guid? sessionId,
            PrepareInteractiveRequestOptions options,
            CancellationToken ct = default)
        {
            PrepareCallCount++;
            LastSessionId = sessionId;
            LastRequestedConductor = options.RequestedConductor;
            LastAssistantResponse = options.LastAssistantResponse;

            var resolvedSessionId = sessionId ?? Guid.CreateVersion7();
            var sessionContext = new InteractiveSessionContext
            {
                ActiveModeName = "general",
                ProjectName = "prepared-project",
                StoryStatePath = "prepared-project/.state.yaml",
                CurrentFile = "scene.md",
            };
            var sessionState = new SessionState
            {
                SessionId = resolvedSessionId,
                Mode = new ModeSelectionState
                {
                    ActiveModeName = "general",
                    ProjectName = "prepared-project",
                    CurrentFile = "scene.md",
                },
                Profile = new ProfileState
                {
                    ProfileId = "prepared-profile",
                    ActiveConductor = "prepared-conductor",
                    ActiveLoreSet = "prepared-lore",
                    ActiveNarrativeRules = "prepared-rules",
                    ActiveWritingStyle = "prepared-style",
                },
            };

            return Task.FromResult(new PreparedInteractiveRequest
            {
                ProfileView = new SessionProfileReadView
                {
                    SessionState = sessionState,
                    DefaultProfileId = "default",
                    ActiveProfileId = "prepared-profile",
                    ActiveConductor = "prepared-conductor",
                    ActiveLoreSet = "prepared-lore",
                    ActiveNarrativeRules = "prepared-rules",
                    ActiveWritingStyle = "prepared-style",
                },
                SessionContext = sessionContext,
                AgentContext = new AgentContext
                {
                    SessionId = resolvedSessionId,
                    ActiveMode = "general",
                    ActiveLoreSet = "prepared-lore",
                    ActiveNarrativeRules = "prepared-rules",
                    ActiveWritingStyle = "prepared-style",
                    SessionContext = sessionContext,
                    LastAssistantResponse = options.LastAssistantResponse,
                },
                Conductor = "prepared-conductor",
            });
        }
    }

    private sealed class RecordingConductorStore : IConductorStore
    {
        public string? LastLoadedConductor { get; private set; }

        public Task<string> LoadAsync(string conductorName, int? maxTokens = null, CancellationToken ct = default)
        {
            LastLoadedConductor = conductorName;
            return Task.FromResult($"prompt:{conductorName}");
        }

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["prepared-conductor"]);
    }

    private sealed class ScriptedCompletionService : ICompletionService
    {
        private readonly Queue<CompletionResponse> _responses = new();

        public void EnqueueText(string text)
        {
            _responses.Enqueue(new CompletionResponse
            {
                Content = new MessageContent(text),
                StopReason = "end_turn",
                Usage = new TokenUsage(1, 1),
            });
        }

        public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
        {
            return Task.FromResult(_responses.Dequeue());
        }

        public async IAsyncEnumerable<StreamEvent> StreamAsync(
            CompletionRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var response = await CompleteAsync(request, ct);
            yield return new TextDeltaEvent(response.Content.GetText());
            yield return new DoneEvent(response.StopReason, response.Usage);
        }
    }

    private sealed class NoOpInteractiveSessionContextService : IInteractiveSessionContextService
    {
        public Task<InteractiveSessionContext> BuildAsync(SessionState state, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<InteractiveSessionContext> LoadAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed class NoOpRuntimeService : ISessionStateService
    {
        public Task<SessionState> LoadViewAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> SetProfileAsync(Guid? sessionId, SetSessionProfileCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> SetRoleplayAsync(Guid? sessionId, SetSessionRoleplayCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> SetModeAsync(Guid? sessionId, SetSessionModeCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> CaptureWriterPendingAsync(Guid? sessionId, CaptureWriterPendingCommand command, CancellationToken ct = default)
            => Task.FromResult(SessionMutationResult<SessionState>.Success(new SessionState { SessionId = sessionId }));

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

    private sealed class TestSessionBootstrapService : ISessionBootstrapService
    {
        public Task<ConversationTree> CreateAsync(CreateSessionCommand command, CancellationToken ct = default)
        {
            var tree = new ConversationTree(
                command.SessionId ?? Guid.CreateVersion7(),
                command.Name,
                NullLogger<ConversationTree>.Instance);
            return Task.FromResult(tree);
        }
    }

    private sealed class InMemorySessionStore : ISessionStore
    {
        private readonly Dictionary<Guid, ConversationTree> _sessions = [];

        public Task<ConversationTree> LoadAsync(Guid sessionId, CancellationToken ct = default)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                throw new FileNotFoundException($"Session not found: {sessionId}");
            }

            return Task.FromResult(session);
        }

        public Task SaveAsync(ConversationTree session, CancellationToken ct = default)
        {
            _sessions[session.SessionId] = session;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SessionSummary>>([]);

        public Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
        {
            _sessions.Remove(sessionId);
            return Task.CompletedTask;
        }
    }

    private sealed class TestRequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
