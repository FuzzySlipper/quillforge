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
using QuillForge.Web.Endpoints;
using QuillForge.Web.Services;

namespace QuillForge.Architecture.Tests;

public sealed class ChatStreamingEndpointTests
{
    [Fact]
    public async Task ChatStream_WithReasoningToolAndText_EmitsExpectedEventSequenceAndPersistsReply()
    {
        var preparedService = new PreparedContextService();
        var runtimeService = new RecordingRuntimeService();
        var sessionStore = new InMemorySessionStore();
        var completionService = new ScriptedStreamingCompletionService();
        var toolHandler = new QueryLoreToolHandler();

        completionService.EnqueueStream(
            new ReasoningDeltaEvent("Thinking through the archive."),
            new ToolCallDeltaReceivedEvent(
                "query_lore",
                "call_1",
                ParseJson("""{"query":"moon archive"}""")),
            new DoneEvent(StopReason.ToolUse, new TokenUsage(3, 4)));
        completionService.EnqueueStream(
            new TextDeltaEvent("Lore "),
            new TextDeltaEvent("answer"),
            new DoneEvent(StopReason.EndTurn, new TokenUsage(5, 7)));

        await using var app = BuildApp(
            preparedService,
            runtimeService,
            sessionStore,
            completionService,
            [toolHandler]);

        var sessionId = Guid.CreateVersion7();
        var response = await InvokePostJsonAsync(
            app,
            "/api/chat/stream",
            $$"""{"sessionId":"{{sessionId}}","message":"Tell me about the moon archive."}""");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("text/event-stream", response.ContentType);

        var events = ParseSseEvents(response.Body);

        Assert.Contains(events, evt => evt.Type == "diagnostic");
        Assert.Contains(events, evt => evt.Type == "reasoning_delta");
        Assert.Contains(events, evt => evt.Type == "tool");
        Assert.Contains(events, evt => evt.Type == "text_delta");
        Assert.Contains(events, evt => evt.Type == "done");
        Assert.Contains(events, evt => evt.Type == "persisted");

        var reasoningIndex = events.FindIndex(evt => evt.Type == "reasoning_delta");
        var toolIndex = events.FindIndex(evt => evt.Type == "tool");
        var firstTextIndex = events.FindIndex(evt => evt.Type == "text_delta");
        var doneIndex = events.FindIndex(evt => evt.Type == "done");
        var persistedIndex = events.FindIndex(evt => evt.Type == "persisted");

        Assert.True(reasoningIndex >= 0);
        Assert.True(toolIndex > reasoningIndex);
        Assert.True(firstTextIndex > toolIndex);
        Assert.True(doneIndex > firstTextIndex);
        Assert.True(persistedIndex > doneIndex);

        var diagnosticEvent = events.First(evt => evt.Type == "diagnostic");
        Assert.True(diagnosticEvent.Payload.TryGetProperty("category", out var diagnosticCategory));
        Assert.True(diagnosticEvent.Payload.TryGetProperty("message", out var diagnosticMessage));
        Assert.True(diagnosticEvent.Payload.TryGetProperty("level", out var diagnosticLevel));
        Assert.False(string.IsNullOrWhiteSpace(diagnosticCategory.GetString()));
        Assert.False(string.IsNullOrWhiteSpace(diagnosticMessage.GetString()));
        Assert.False(string.IsNullOrWhiteSpace(diagnosticLevel.GetString()));

        var reasoningEvent = events.First(evt => evt.Type == "reasoning_delta");
        Assert.Equal("Thinking through the archive.", reasoningEvent.Payload.GetProperty("text").GetString());

        var toolEvent = events.First(evt => evt.Type == "tool");
        Assert.Equal("query_lore", toolEvent.Payload.GetProperty("name").GetString());
        Assert.Equal("call_1", toolEvent.Payload.GetProperty("id").GetString());

        var textEvents = events.Where(evt => evt.Type == "text_delta").ToList();
        Assert.Equal(2, textEvents.Count);
        Assert.Equal("Lore ", textEvents[0].Payload.GetProperty("text").GetString());
        Assert.Equal("answer", textEvents[1].Payload.GetProperty("text").GetString());

        var doneEvent = events.First(evt => evt.Type == "done");
        Assert.Equal(sessionId.ToString(), doneEvent.Payload.GetProperty("sessionId").GetGuid().ToString());
        Assert.Equal("Lore answer", doneEvent.Payload.GetProperty("content").GetString());
        Assert.Equal("end_turn", doneEvent.Payload.GetProperty("stopReason").GetString());
        Assert.Equal("Discussion", doneEvent.Payload.GetProperty("responseType").GetString());
        Assert.Equal(5, doneEvent.Payload.GetProperty("usage").GetProperty("input").GetInt32());
        Assert.Equal(7, doneEvent.Payload.GetProperty("usage").GetProperty("output").GetInt32());

        var persistedEvent = events.First(evt => evt.Type == "persisted");
        var assistantNodeId = persistedEvent.Payload.GetProperty("nodeId").GetGuid();
        var userNodeId = persistedEvent.Payload.GetProperty("userNodeId").GetGuid();
        Assert.Equal(userNodeId, doneEvent.Payload.GetProperty("parentId").GetGuid());

        var savedTree = await sessionStore.LoadAsync(sessionId);
        var thread = savedTree.ToFlatThread();

        Assert.Equal(2, thread.Count);
        Assert.Equal("user", thread[0].Role);
        Assert.Equal("assistant", thread[1].Role);
        Assert.Equal(userNodeId, thread[0].Id);
        Assert.Equal(assistantNodeId, thread[1].Id);
        Assert.Equal("Lore answer", thread[1].Content.GetText());
        Assert.Equal(StopReason.EndTurn, thread[1].Metadata?.StopReason);

        Assert.Single(runtimeService.CaptureCalls);
        Assert.Equal(sessionId, runtimeService.CaptureCalls[0].SessionId);
        Assert.Equal("Lore answer", runtimeService.CaptureCalls[0].Command.Content);
        Assert.Equal(Mode.General, runtimeService.CaptureCalls[0].Command.SourceMode);
        Assert.Equal(2, completionService.StreamRequestCount);
        Assert.Equal(1, toolHandler.CallCount);
    }

    [Fact]
    public async Task ChatStream_WithEmptyVisibleContent_EmitsWarningAndSkipsAssistantPersistence()
    {
        var preparedService = new PreparedContextService();
        var runtimeService = new RecordingRuntimeService();
        var sessionStore = new InMemorySessionStore();
        var completionService = new ScriptedStreamingCompletionService();

        completionService.EnqueueStream(new DoneEvent(StopReason.EndTurn, new TokenUsage(1, 2)));

        await using var app = BuildApp(
            preparedService,
            runtimeService,
            sessionStore,
            completionService,
            []);

        var sessionId = Guid.CreateVersion7();
        var response = await InvokePostJsonAsync(
            app,
            "/api/chat/stream",
            $$"""{"sessionId":"{{sessionId}}","message":"Say nothing."}""");

        Assert.Equal(200, response.StatusCode);
        Assert.Equal("text/event-stream", response.ContentType);

        var events = ParseSseEvents(response.Body);

        Assert.DoesNotContain(events, evt => evt.Type == "text_delta");
        Assert.Contains(events, evt =>
            evt.Type == "diagnostic"
            && evt.Payload.GetProperty("category").GetString() == "warning"
            && evt.Payload.GetProperty("level").GetString() == "warning"
            && evt.Payload.GetProperty("message").GetString() == "Response completed with empty content — model returned no visible text");

        var doneIndex = events.FindIndex(evt => evt.Type == "done");
        var warningIndex = events.FindIndex(evt =>
            evt.Type == "diagnostic"
            && evt.Payload.GetProperty("message").GetString() == "Response completed with empty content — model returned no visible text");
        var persistedIndex = events.FindIndex(evt => evt.Type == "persisted");

        Assert.True(doneIndex >= 0);
        Assert.True(warningIndex > doneIndex);
        Assert.True(persistedIndex > warningIndex);

        var doneEvent = events.First(evt => evt.Type == "done");
        Assert.Equal(string.Empty, doneEvent.Payload.GetProperty("content").GetString());
        Assert.Equal("end_turn", doneEvent.Payload.GetProperty("stopReason").GetString());

        var persistedEvent = events.First(evt => evt.Type == "persisted");
        Assert.Equal(JsonValueKind.Null, persistedEvent.Payload.GetProperty("nodeId").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, persistedEvent.Payload.GetProperty("userNodeId").ValueKind);

        var savedTree = await sessionStore.LoadAsync(sessionId);
        var thread = savedTree.ToFlatThread();

        Assert.Single(thread);
        Assert.Equal("user", thread[0].Role);
        Assert.Empty(runtimeService.CaptureCalls);
        Assert.Equal(1, completionService.StreamRequestCount);
    }

    private static WebApplication BuildApp(
        PreparedContextService preparedService,
        RecordingRuntimeService runtimeService,
        InMemorySessionStore sessionStore,
        ScriptedStreamingCompletionService completionService,
        IReadOnlyList<IToolHandler> toolHandlers)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        builder.Services.AddRouting();
        builder.Services.AddLogging();

        var appConfig = new AppConfig
        {
            Diagnostics = new DiagnosticsConfig
            {
                LivePanel = true,
            },
        };

        builder.Services.AddSingleton(appConfig);
        builder.Services.AddSingleton<ICompletionService>(completionService);
        builder.Services.AddSingleton(preparedService);
        builder.Services.AddSingleton<ISessionProfileReadService>(sp => sp.GetRequiredService<PreparedContextService>());
        builder.Services.AddSingleton<IConductorStore>(new RecordingConductorStore());
        builder.Services.AddSingleton<IInteractiveSessionContextService>(new NoOpInteractiveSessionContextService());
        builder.Services.AddSingleton<ISessionStateService>(runtimeService);
        builder.Services.AddSingleton<ISessionBootstrapService>(new TestSessionBootstrapService());
        builder.Services.AddSingleton<ISessionStore>(sessionStore);

        foreach (var toolHandler in toolHandlers)
        {
            builder.Services.AddSingleton(typeof(IToolHandler), toolHandler);
        }

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
        return app;
    }

    private static async Task<EndpointResponse> InvokePostJsonAsync(
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
        return new EndpointResponse(
            context.Response.StatusCode,
            context.Response.ContentType,
            await reader.ReadToEndAsync());
    }

    private static List<SseEventEnvelope> ParseSseEvents(string responseBody)
    {
        var envelopes = new List<SseEventEnvelope>();
        var chunks = responseBody.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (var chunk in chunks)
        {
            if (!chunk.StartsWith("data: ", StringComparison.Ordinal))
            {
                continue;
            }

            var payloadText = chunk["data: ".Length..];
            using var document = JsonDocument.Parse(payloadText);
            var payload = document.RootElement.Clone();
            var type = payload.GetProperty("type").GetString();
            Assert.False(string.IsNullOrWhiteSpace(type));
            envelopes.Add(new SseEventEnvelope(type!, payload));
        }

        return envelopes;
    }

    private static JsonElement ParseJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
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

    private sealed record EndpointResponse(int StatusCode, string? ContentType, string Body);

    private sealed record SseEventEnvelope(string Type, JsonElement Payload);

    private sealed class PreparedContextService : ISessionProfileReadService
    {
        public Task<SessionProfileReadView> LoadAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<QuillForge.Web.Contracts.ProfilesResponse> BuildProfilesResponseAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<PreparedInteractiveRequest> PrepareInteractiveRequestAsync(
            Guid? sessionId,
            PrepareInteractiveRequestOptions options,
            CancellationToken ct = default)
        {
            var resolvedSessionId = sessionId ?? Guid.CreateVersion7();
            var sessionContext = new InteractiveSessionContext
            {
                ActiveMode = Mode.General,
                ProjectName = "prepared-project",
                StoryStatePath = "prepared-project/.state.yaml",
                CurrentFile = "scene.md",
            };

            var state = new SessionState
            {
                SessionId = resolvedSessionId,
                Mode = new ModeSelectionState
                {
                    ActiveMode = Mode.General,
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
                    SessionState = state,
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
                    ActiveMode = Mode.General,
                    ActiveLoreSet = "prepared-lore",
                    ActiveWritingStyle = "prepared-style",
                    ActiveNarrativeRules = "prepared-rules",
                    SessionContext = sessionContext,
                    LastAssistantResponse = options.LastAssistantResponse,
                },
                Conductor = "prepared-conductor",
                AssistantPortraitUrl = "/backgrounds/assistant.png",
                UserPortraitUrl = "/backgrounds/user.png",
            });
        }
    }

    private sealed class RecordingRuntimeService : ISessionStateService
    {
        public List<WriterPendingCaptureCall> CaptureCalls { get; } = [];

        public Task<SessionState> LoadViewAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> SetProfileAsync(Guid? sessionId, SetSessionProfileCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> SetRoleplayAsync(Guid? sessionId, SetSessionRoleplayCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> SetModeAsync(Guid? sessionId, SetSessionModeCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<WriterPendingCaptureEvent>> CaptureWriterPendingAsync(Guid? sessionId, CaptureWriterPendingCommand command, CancellationToken ct = default)
        {
            CaptureCalls.Add(new WriterPendingCaptureCall(sessionId, command));
            return Task.FromResult(SessionMutationResult<WriterPendingCaptureEvent>.Success(
                new WriterPendingContentCapturedEvent(
                    new SessionState { SessionId = sessionId },
                    command.Content.Length,
                    command.SourceMode)));
        }

        public Task<SessionMutationResult<WriterPendingContentAcceptedEvent>> AcceptWriterPendingAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<WriterPendingContentRejectedEvent>> RejectWriterPendingAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> UpdateNarrativeStateAsync(Guid? sessionId, UpdateNarrativeStateCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> SetActivePlotAsync(Guid? sessionId, SetActivePlotCommand command, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<SessionMutationResult<SessionState>> ClearActivePlotAsync(Guid? sessionId, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    private sealed record WriterPendingCaptureCall(Guid? SessionId, CaptureWriterPendingCommand Command);

    private sealed class RecordingConductorStore : IConductorStore
    {
        public Task<string> LoadAsync(string conductorName, int? maxTokens = null, CancellationToken ct = default)
            => Task.FromResult($"prompt:{conductorName}");

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["prepared-conductor"]);
    }

    private sealed class NoOpInteractiveSessionContextService : IInteractiveSessionContextService
    {
        public Task<InteractiveSessionContext> BuildAsync(SessionState state, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<InteractiveSessionContext> LoadAsync(Guid? sessionId, CancellationToken ct = default)
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

    private sealed class ScriptedStreamingCompletionService : ICompletionService
    {
        private readonly Queue<IReadOnlyList<StreamEvent>> _streamScripts = [];

        public int StreamRequestCount { get; private set; }

        public void EnqueueStream(params StreamEvent[] events)
        {
            _streamScripts.Enqueue(events);
        }

        public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("This test service only supports streaming calls.");

        public async IAsyncEnumerable<StreamEvent> StreamAsync(
            CompletionRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            StreamRequestCount++;

            if (_streamScripts.Count == 0)
            {
                throw new InvalidOperationException("No scripted streaming responses remain.");
            }

            var events = _streamScripts.Dequeue();
            foreach (var evt in events)
            {
                ct.ThrowIfCancellationRequested();
                yield return evt;
                await Task.Yield();
            }
        }
    }

    private sealed class QueryLoreToolHandler : IToolHandler
    {
        public string Name => "query_lore";

        public ToolDefinition Definition => new(
            Name,
            "Query lore by text.",
            ParseJson(
                """
                {
                    "type": "object",
                    "properties": {
                        "query": { "type": "string" }
                    },
                    "required": ["query"]
                }
                """));

        public int CallCount { get; private set; }

        public Task<ToolResult> HandleAsync(ToolInput input, AgentContext context, CancellationToken ct = default)
        {
            CallCount++;
            var query = input.GetRequiredString("query");
            return Task.FromResult(ToolResult.Ok($"Lore result for {query}"));
        }
    }

    private sealed class TestRequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
