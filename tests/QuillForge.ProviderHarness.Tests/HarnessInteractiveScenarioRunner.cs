using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using QuillForge.Core;
using QuillForge.Core.Agents;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Agents.Tools;
using QuillForge.Core.Models;
using QuillForge.Core.Pipeline;
using QuillForge.Core.Services;
using QuillForge.Providers.Registry;
using QuillForge.Storage.Docs;
using QuillForge.Storage.FileSystem;
using QuillForge.Storage.Utilities;
using QuillForge.Web;
using QuillForge.Web.Contracts;
using QuillForge.Web.Endpoints;
using QuillForge.Web.Services;

namespace QuillForge.ProviderHarness.Tests;

public sealed class HarnessInteractiveScenarioRunner : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HarnessProviderHost _providerHost;
    private readonly string _contentRoot;
    private readonly string _docsRoot;
    private readonly WebApplication _app;
    private readonly AppConfig _appConfig;
    private readonly HarnessDebugBridgeDriver _bridge;

    public HarnessInteractiveScenarioRunner(HarnessProviderHost providerHost)
    {
        _providerHost = providerHost;
        _contentRoot = Path.Combine(Path.GetTempPath(), $"quillforge-harness-interactive-{Guid.NewGuid():N}");
        _docsRoot = Path.Combine(Path.GetTempPath(), $"quillforge-harness-docs-{Guid.NewGuid():N}");
        _appConfig = CreateAppConfig();
        SeedContentRoot(_contentRoot, _docsRoot);
        _app = BuildApp();
        _bridge = new HarnessDebugBridgeDriver(_app);
    }

    public HarnessDebugBridgeDriver Bridge => _bridge;

    public async Task<HarnessInteractiveScenarioReport> RunTurnAsync(
        Mode mode,
        string message,
        string? character = null,
        string? project = null,
        string? file = null,
        CancellationToken ct = default)
    {
        var bootstrap = _app.Services.GetRequiredService<ISessionBootstrapService>();
        var runtimeService = _app.Services.GetRequiredService<ISessionStateService>();
        var sessionStore = _app.Services.GetRequiredService<ISessionStore>();
        var tracker = _app.Services.GetRequiredService<ITokenUsageTracker>();

        var tree = await bootstrap.CreateAsync(
            new CreateSessionCommand
            {
                Name = $"Harness {mode.ToWireString()} Session",
            },
            ct);

        var modeResult = await runtimeService.SetModeAsync(
            tree.SessionId,
            new SetSessionModeCommand(mode.ToWireString(), project, file, character),
            ct);

        if (modeResult.Status != SessionMutationStatus.Success)
        {
            throw new InvalidOperationException(
                $"Failed to set mode to {mode.ToWireString()}: {modeResult.Error ?? "unknown error"}");
        }

        var providerTraceStartIndex = _providerHost.TraceStore.Snapshot().Count;
        var startedAt = DateTimeOffset.UtcNow;

        var streamResponse = await InvokeJsonAsync<DebugBridgeStreamResponse>(
            "POST",
            "/api/debug/bridge/chat/stream",
            JsonSerializer.Serialize(new
            {
                sessionId = tree.SessionId,
                message,
                model = _appConfig.Models.Orchestrator,
                maxTokens = 4096,
            }),
            ct);

        var completedAt = DateTimeOffset.UtcNow;
        var providerTraces = _providerHost.TraceStore.Snapshot()
            .Skip(providerTraceStartIndex)
            .ToList();
        var providerTraceIds = providerTraces.Select(trace => trace.TraceId).ToList();
        var savedTree = await sessionStore.LoadAsync(tree.SessionId, ct);
        var sessionSnapshot = ToCollectedSessionSnapshot(savedTree);
        var appTrace = HarnessAppTraceBuilder.FromCollectedStream(
            ToCollectedAppStream(streamResponse, tree.SessionId, mode.ToWireString()),
            sessionSnapshot) with
        {
            RunId = _providerHost.ArtifactStore.RunId,
            RelatedProviderTraceIds = providerTraceIds,
        };
        var usageSummary = tracker.GetSessionUsage(tree.SessionId);

        var run = new DualSidedHarnessRun
        {
            RunId = _providerHost.ArtifactStore.RunId,
            ScenarioName = $"interactive/{mode.ToWireString()}",
            StartedAt = startedAt,
            CompletedAt = completedAt,
            ProviderTraces = providerTraces,
            AppTrace = appTrace,
        };

        var report = new HarnessInteractiveScenarioReport
        {
            SessionId = tree.SessionId,
            Mode = mode.ToWireString(),
            Run = run,
            UsageSummary = usageSummary,
        };
        var persistedReport = HarnessRunReportWriter.WriteInteractiveReport(
            _providerHost.ArtifactStore,
            report);

        return report with
        {
            PersistedReport = persistedReport,
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();

        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }

        if (Directory.Exists(_docsRoot))
        {
            Directory.Delete(_docsRoot, recursive: true);
        }
    }

    private WebApplication BuildApp()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Development",
        });

        builder.Services.AddRouting();
        builder.Services.AddLogging();
        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        });

        builder.Services.AddSingleton(_appConfig);
        builder.Services.AddSingleton<AtomicFileWriter>();
        builder.Services.AddSingleton<Den.Persistence.AtomicFileWriter>();
        builder.Services.AddSingleton<IContentFileService>(sp =>
            new FileSystemContentService(
                _contentRoot,
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemContentService>>()));
        builder.Services.AddSingleton<ILoreStore>(sp =>
            new FileSystemLoreStore(
                Path.Combine(_contentRoot, ContentPaths.Lore),
                sp.GetRequiredService<ILogger<FileSystemLoreStore>>()));
        builder.Services.AddSingleton<IStoryStore>(sp =>
            new FileSystemStoryStore(
                Path.Combine(_contentRoot, ContentPaths.Story),
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemStoryStore>>()));
        builder.Services.AddSingleton<IWritingStyleStore>(sp =>
            new FileSystemWritingStyleStore(
                Path.Combine(_contentRoot, ContentPaths.WritingStyles),
                sp.GetRequiredService<ILogger<FileSystemWritingStyleStore>>()));
        builder.Services.AddSingleton<INarrativeRulesStore>(sp =>
            new FileSystemNarrativeRulesStore(
                Path.Combine(_contentRoot, ContentPaths.NarrativeRules),
                sp.GetRequiredService<ILogger<FileSystemNarrativeRulesStore>>()));
        builder.Services.AddSingleton<ILibrarianPromptStore>(sp =>
            new FileSystemLibrarianPromptStore(
                Path.Combine(_contentRoot, ContentPaths.LibrarianPrompts),
                sp.GetRequiredService<ILogger<FileSystemLibrarianPromptStore>>()));
        builder.Services.AddSingleton<IPlotStore>(sp =>
            new FileSystemPlotStore(
                Path.Combine(_contentRoot, ContentPaths.Plots),
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemPlotStore>>()));
        builder.Services.AddSingleton<IAssistantPromptStore>(sp =>
            new FileSystemAssistantPromptStore(
                Path.Combine(_contentRoot, ContentPaths.Assistant),
                sp.GetRequiredService<ILogger<FileSystemAssistantPromptStore>>()));
        builder.Services.AddSingleton<ICharacterCardStore>(sp =>
            new FileSystemCharacterCardStore(
                Path.Combine(_contentRoot, ContentPaths.CharacterCards),
                Path.Combine(_contentRoot, ContentPaths.CharacterCards),
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemCharacterCardStore>>()));
        builder.Services.AddSingleton<ISessionStore>(sp =>
            new FileSystemSessionStore(
                Path.Combine(_contentRoot, ContentPaths.DataSessions),
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemSessionStore>>(),
                sp.GetRequiredService<ILoggerFactory>()));
        builder.Services.AddSingleton<IProfileConfigStore>(sp =>
            new FileSystemProfileConfigStore(
                _contentRoot,
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemProfileConfigStore>>()));
        builder.Services.AddSingleton<FileSystemSessionRuntimeStore>(sp =>
            new FileSystemSessionRuntimeStore(
                _contentRoot,
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemSessionRuntimeStore>>()));
        builder.Services.AddSingleton<ISessionStateStore>(sp =>
            sp.GetRequiredService<FileSystemSessionRuntimeStore>());
        builder.Services.AddSingleton<IStoryStateService>(sp =>
            new FileSystemStoryStateService(
                Path.Combine(_contentRoot, ContentPaths.Story),
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemStoryStateService>>()));
        builder.Services.AddSingleton<IDocsService>(sp =>
            new FileSystemDocsService(
                _docsRoot,
                sp.GetRequiredService<ILogger<FileSystemDocsService>>()));
        builder.Services.AddSingleton<IAppConfigStore>(new FixedAppConfigStore(_appConfig));

        builder.Services.AddSingleton<ProviderFactory>(sp =>
            new ProviderFactory(
                sp.GetRequiredService<ILogger<ProviderFactory>>(),
                sp.GetRequiredService<AppConfig>()));
        builder.Services.AddSingleton<ProviderRegistry>(sp =>
        {
            var registry = new ProviderRegistry(
                sp.GetRequiredService<ProviderFactory>(),
                sp.GetRequiredService<AppConfig>(),
                sp.GetRequiredService<ILogger<ProviderRegistry>>(),
                sp.GetRequiredService<ILoggerFactory>());

            RegisterHarnessProvider(registry, "orchestrator", "orchestrator-model", requiresReasoning: true);
            RegisterHarnessProvider(registry, "narrative-director", "narrative-director-model", requiresReasoning: true);
            RegisterHarnessProvider(registry, "prose-writer", "prose-writer-model", requiresReasoning: true);
            RegisterHarnessProvider(registry, "librarian", "librarian-model", requiresReasoning: true);
            RegisterHarnessProvider(registry, "council-critic", "council-critic-model", requiresReasoning: true);
            return registry;
        });

        builder.Services.AddSingleton<ITokenUsageTracker, InMemoryTokenUsageTracker>();
        builder.Services.AddSingleton<DefaultCompletionService>();
        builder.Services.AddSingleton<ICompletionService>(sp =>
            new UsageTrackingCompletionService(
                sp.GetRequiredService<DefaultCompletionService>(),
                sp.GetRequiredService<ITokenUsageTracker>(),
                sp.GetRequiredService<ILogger<UsageTrackingCompletionService>>()));

        builder.Services.AddSingleton<ContinuationStrategy>();
        builder.Services.AddSingleton<ToolLoop>();
        builder.Services.AddSingleton<CanonPrerequisiteGuard>();
        builder.Services.AddSingleton<LibrarianAgent>();
        builder.Services.AddSingleton<QueryLoreHandler>();
        builder.Services.AddSingleton<ProseWriterAgent>(sp =>
            new ProseWriterAgent(
                sp.GetRequiredService<ToolLoop>(),
                sp.GetRequiredService<QueryLoreHandler>(),
                sp.GetRequiredService<CanonPrerequisiteGuard>(),
                sp.GetRequiredService<AppConfig>(),
                sp.GetRequiredService<ILogger<ProseWriterAgent>>()));
        builder.Services.AddSingleton<NarrativeDirectorAgent>();
        builder.Services.AddSingleton<ForgePlannerAgent>();
        builder.Services.AddSingleton<ForgeWriterAgent>();
        builder.Services.AddSingleton<ForgeReviewerAgent>(sp =>
            new ForgeReviewerAgent(
                sp.GetRequiredService<ICompletionService>(),
                sp.GetRequiredService<AppConfig>(),
                sp.GetRequiredService<ILogger<ForgeReviewerAgent>>()));

        builder.Services.AddSingleton<DelegatePool>(sp =>
        {
            var registry = sp.GetRequiredService<ProviderRegistry>();
            return new DelegatePool(
                alias => registry.GetCompletionService(alias),
                registry.ResolveProviderAlias,
                sp.GetRequiredService<ILogger<DelegatePool>>());
        });
        builder.Services.AddSingleton<ICouncilService, CouncilService>();
        builder.Services.AddSingleton<RunCouncilHandler>();

        builder.Services.AddSingleton<GetStoryStateHandler>();
        builder.Services.AddSingleton<UpdateStoryStateHandler>();
        builder.Services.AddSingleton<UpdateNarrativeStateHandler>();
        builder.Services.AddSingleton<WriteProseHandler>();
        builder.Services.AddSingleton<DirectSceneHandler>();

        builder.Services.AddSingleton<IToolHandler>(sp => sp.GetRequiredService<QueryLoreHandler>());
        builder.Services.AddSingleton<IToolHandler>(sp => sp.GetRequiredService<DirectSceneHandler>());
        builder.Services.AddSingleton<IToolHandler>(sp => sp.GetRequiredService<RunCouncilHandler>());
        builder.Services.AddSingleton<IToolHandler>(sp => sp.GetRequiredService<GetStoryStateHandler>());
        builder.Services.AddSingleton<IToolHandler>(sp => sp.GetRequiredService<UpdateStoryStateHandler>());
        builder.Services.AddSingleton<IToolHandler>(sp => sp.GetRequiredService<UpdateNarrativeStateHandler>());
        builder.Services.AddSingleton<IToolHandler>(sp => sp.GetRequiredService<WriteProseHandler>());
        builder.Services.AddSingleton<IToolHandler>(sp =>
            new QueryDocsHandler(
                sp.GetRequiredService<IDocsService>(),
                sp.GetRequiredService<ILogger<QueryDocsHandler>>()));

        builder.Services.AddSingleton<IMode, GuideMode>();
        builder.Services.AddSingleton<IMode, WriterMode>();
        builder.Services.AddSingleton<IMode, RoleplayMode>();
        builder.Services.AddSingleton<IMode, CouncilMode>();
        builder.Services.AddSingleton<IMode, GamesMode>();

        builder.Services.AddSingleton<IPipelineStage, PlanningStage>();
        builder.Services.AddSingleton<IPipelineStage, DesignStage>();
        builder.Services.AddSingleton<IPipelineStage, WritingStage>();
        builder.Services.AddSingleton<IPipelineStage, ReviewStage>();
        builder.Services.AddSingleton<IPipelineStage, AssemblyStage>();
        builder.Services.AddSingleton<ForgePipeline>();

        builder.Services.AddSingleton<ISessionMutationGate, InMemorySessionMutationGate>();
        builder.Services.AddSingleton<SessionRuntimeService>();
        builder.Services.AddSingleton<ISessionStateService>(sp =>
            sp.GetRequiredService<SessionRuntimeService>());
        builder.Services.AddSingleton<ISessionBootstrapService, SessionBootstrapService>();
        builder.Services.AddSingleton<ISessionLifecycleService, SessionLifecycleService>();
        builder.Services.AddSingleton<ISessionTranscriptService, SessionTranscriptService>();
        builder.Services.AddSingleton<IInteractiveSessionContextService, InteractiveSessionContextService>();
        builder.Services.AddSingleton<IProfileConfigService, ProfileConfigService>();
        builder.Services.AddSingleton<ISessionProfileReadService, SessionProfileReadService>();
        builder.Services.AddSingleton<OrchestratorAgent>();

        var app = builder.Build();
        app.MapDebugBridgeEndpoints();
        return app;
    }

    private void RegisterHarnessProvider(
        ProviderRegistry registry,
        string alias,
        string model,
        bool requiresReasoning)
    {
        registry.Register(new ProviderConfig
        {
            Alias = alias,
            Type = ProviderType.Custom,
            ApiKey = "test-key",
            BaseUrl = _providerHost.OpenAiBaseUri.ToString().TrimEnd('/'),
            ModelsUrl = _providerHost.ModelsUri.ToString(),
            DefaultModel = model,
            RequiresReasoning = requiresReasoning,
        });
    }

    private static AppConfig CreateAppConfig()
    {
        return new AppConfig
        {
            Diagnostics = new DiagnosticsConfig
            {
                LivePanel = true,
            },
            Lore = new LoreConfig
            {
                Active = "default",
            },
            NarrativeRules = new NarrativeRulesConfig
            {
                Active = "default",
            },
            WritingStyle = new WritingStyleConfig
            {
                Active = "default",
            },
            Models = new ModelsConfig
            {
                Orchestrator = "orchestrator",
                NarrativeDirector = "narrative-director",
                ProseWriter = "prose-writer",
                Librarian = "librarian",
                DelegateTechnical = "orchestrator",
            },
        };
    }

    private static void SeedContentRoot(string contentRoot, string docsRoot)
    {
        foreach (var directory in ContentPaths.AllDirectories)
        {
            Directory.CreateDirectory(Path.Combine(contentRoot, directory));
        }

        Directory.CreateDirectory(Path.Combine(contentRoot, ContentPaths.Lore, "default"));
        Directory.CreateDirectory(docsRoot);

        File.WriteAllText(
            Path.Combine(contentRoot, ContentPaths.Lore, "default", "world.md"),
            """
            Aurora once hid a sapphire ring inside the conservatory wall.
            Lucian quietly shields Aurora whenever rivals press too close.
            Aurora is classically elegant and composed under social pressure.
            """);

        File.WriteAllText(
            Path.Combine(contentRoot, ContentPaths.NarrativeRules, "default.md"),
            """
            Write in close third person past tense.
            Re-ground against canon when characterization or scene facts are corrected.
            Keep scene continuity tight.
            """);

        File.WriteAllText(
            Path.Combine(contentRoot, ContentPaths.WritingStyles, "default.md"),
            """
            Elegant romantic suspense prose with controlled sentence rhythm and clear sensory detail.
            """);

        File.WriteAllText(
            Path.Combine(contentRoot, ContentPaths.LibrarianPrompts, "default.md"),
            """
            Treat lore as canonical source material. Prefer direct matches over genre inference.
            """);

        File.WriteAllText(
            Path.Combine(contentRoot, ContentPaths.Assistant, "default.md"),
            """
            Be calm, concise, and helpful when summarizing tool-owned workflows.
            """);

        File.WriteAllText(
            Path.Combine(contentRoot, ContentPaths.CharacterCards, "aurora.yaml"),
            """
            name: Aurora
            personality: Graceful, composed, and quietly perceptive.
            description: A woman of classic elegance who keeps her emotions carefully tended.
            scenario: Aurora is navigating a fragile alliance under social scrutiny.
            greeting: Aurora inclines her head with poised warmth.
            """);

        File.WriteAllText(
            Path.Combine(contentRoot, ContentPaths.Council, "critic.md"),
            """
            provider: council-critic

            You are a structural critic. Focus on coherence, tension, and scene architecture.
            """);

        File.WriteAllText(
            Path.Combine(docsRoot, "modes-overview.md"),
            """
            ---
            name: Modes Overview
            summary: Test harness docs topic
            ---

            # Modes Overview

            Writer is for drafting with review.
            Roleplay is for in-character scene interaction.
            Council is for advisory synthesis.
            """);
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
        context.Request.ContentType = "application/json";
        context.Response.Body = new MemoryStream();

        var bodyBytes = Encoding.UTF8.GetBytes(jsonBody ?? string.Empty);
        context.Request.ContentLength = bodyBytes.Length;
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Features.Set<IHttpRequestBodyDetectionFeature>(new TestRequestBodyDetectionFeature());

        var requestDelegate = endpoint.RequestDelegate
            ?? throw new InvalidOperationException($"Endpoint {route} has no request delegate.");
        await requestDelegate(context);

        if (context.Response.StatusCode >= 400)
        {
            context.Response.Body.Position = 0;
            using var errorReader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
            var errorBody = await errorReader.ReadToEndAsync(ct);
            throw new InvalidOperationException(
                $"Endpoint {route} returned {context.Response.StatusCode}: {errorBody}");
        }

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(ct);
        var value = JsonSerializer.Deserialize<T>(body, JsonOptions);
        return value
            ?? throw new InvalidOperationException($"Could not deserialize {typeof(T).Name} from endpoint {route}.");
    }

    private static HarnessCollectedAppStream ToCollectedAppStream(
        DebugBridgeStreamResponse response,
        Guid sessionId,
        string mode)
    {
        return new HarnessCollectedAppStream
        {
            SessionId = sessionId,
            Mode = mode,
            FinalContent = response.FinalContent,
            FinalReasoning = response.FinalReasoning,
            StopReason = response.StopReason,
            MessageCount = response.MessageCount,
            ToolRounds = response.ToolRounds,
            Usage = new HarnessUsage(response.Usage.InputTokens, response.Usage.OutputTokens),
            Events = response.Events
                .Select(evt => new HarnessCollectedAppEvent
                {
                    Type = evt.Type,
                    Text = evt.Text,
                    ToolName = evt.ToolName,
                    ToolId = evt.ToolId,
                    Category = evt.Category,
                    Message = evt.Message,
                    Level = evt.Level,
                    StopReason = evt.StopReason,
                    Usage = evt.Usage is null ? null : new HarnessUsage(evt.Usage.InputTokens, evt.Usage.OutputTokens),
                })
                .ToList(),
            WriterState = response.WriterState,
        };
    }

    private static HarnessCollectedSessionSnapshot ToCollectedSessionSnapshot(ConversationTree tree)
    {
        var thread = tree.ToFlatThread();
        return new HarnessCollectedSessionSnapshot
        {
            SessionId = tree.SessionId,
            Name = tree.Name,
            Messages = thread.Select(message => new HarnessCollectedSessionMessage
            {
                Id = message.Id,
                Role = message.Role,
                Content = message.Content.GetText(),
                CreatedAt = message.CreatedAt,
                ParentId = message.ParentId,
                Reasoning = message.Metadata?.Reasoning,
                Variants = [],
            }).ToList(),
        };
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

    private sealed class FixedAppConfigStore : IAppConfigStore
    {
        private readonly Lock _lock = new();
        private AppConfig _current;

        public FixedAppConfigStore(AppConfig initial)
        {
            _current = initial;
        }

        public Task<AppConfig> LoadAsync(CancellationToken ct = default)
        {
            lock (_lock)
            {
                return Task.FromResult(_current);
            }
        }

        public Task SaveAsync(AppConfig config, CancellationToken ct = default)
        {
            lock (_lock)
            {
                _current = config;
                return Task.CompletedTask;
            }
        }

        public Task<AppConfig> UpdateAsync(Func<AppConfig, AppConfig> update, CancellationToken ct = default)
        {
            lock (_lock)
            {
                _current = update(_current);
                return Task.FromResult(_current);
            }
        }
    }

    private sealed class TestRequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}

public sealed record HarnessInteractiveScenarioReport
{
    public required Guid SessionId { get; init; }
    public required string Mode { get; init; }
    public required DualSidedHarnessRun Run { get; init; }
    public required SessionUsageSummary UsageSummary { get; init; }
    public HarnessPersistedRunReport? PersistedReport { get; init; }
}
