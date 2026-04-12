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
using QuillForge.Core.Agents.Tools;
using QuillForge.Core.Models;
using QuillForge.Core.Pipeline;
using QuillForge.Core.Services;
using QuillForge.Providers.Registry;
using QuillForge.Storage.FileSystem;
using QuillForge.Storage.Utilities;
using QuillForge.Web.Contracts;
using QuillForge.Web.Endpoints;
using QuillForge.Web.Services;

namespace QuillForge.ProviderHarness.Tests;

public sealed class HarnessForgeScenarioRunner : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly HarnessProviderHost _providerHost;
    private readonly string _contentRoot;
    private readonly WebApplication _app;

    public HarnessForgeScenarioRunner(HarnessProviderHost providerHost)
    {
        _providerHost = providerHost;
        _contentRoot = Path.Combine(Path.GetTempPath(), $"quillforge-harness-forge-{Guid.NewGuid():N}");
        SeedContentRoot(_contentRoot);
        _app = BuildApp();
    }

    public async Task<HarnessForgeScenarioReport> RunCanonicalPauseResumeScenarioAsync(
        string projectName,
        string premise,
        CancellationToken ct = default)
    {
        await InvokeJsonAsync<DebugBridgeForgeCreateResponse>(
            "POST",
            "/api/debug/bridge/forge/create",
            JsonSerializer.Serialize(new
            {
                name = projectName,
                premise,
            }),
            ct);

        var phases = new List<HarnessForgePhaseReport>();

        var designPhase = await RunPhaseAsync(
            phaseName: "design",
            projectName: projectName,
            route: $"/api/debug/bridge/forge/{projectName}/design",
            artifactPaths:
            [
                $"{ContentPaths.Forge}/{projectName}/manifest.json",
                $"{ContentPaths.Forge}/{projectName}/plan/premise.md",
                $"{ContentPaths.Forge}/{projectName}/plan/outline.md",
                $"{ContentPaths.Forge}/{projectName}/plan/style.md",
                $"{ContentPaths.Forge}/{projectName}/plan/bible.md",
                $"{ContentPaths.Forge}/{projectName}/plan/ch-01-brief.md",
            ],
            assertions:
            [
                new ExpectedProviderRequestSectionAssertion("A jewel thief is forced into an arranged marriage during the winter gala."),
                new ExpectedForgeManifestStageAssertion("Writing", expectedPaused: true),
                new ExpectedForgeChapterDiscoveredAssertion("ch-01"),
                new ExpectedForgeStatusMatchesManifestAssertion(),
                new ExpectedArtifactPresenceAssertion($"{ContentPaths.Forge}/{projectName}/plan/outline.md"),
                new ExpectedArtifactPresenceAssertion($"{ContentPaths.Forge}/{projectName}/plan/style.md"),
                new ExpectedArtifactPresenceAssertion($"{ContentPaths.Forge}/{projectName}/plan/bible.md"),
                new ExpectedArtifactPresenceAssertion($"{ContentPaths.Forge}/{projectName}/plan/ch-01-brief.md"),
            ],
            ct);
        phases.Add(designPhase);

        var startPhase = await RunPhaseAsync(
            phaseName: "start",
            projectName: projectName,
            route: $"/api/debug/bridge/forge/{projectName}/start",
            artifactPaths:
            [
                $"{ContentPaths.Forge}/{projectName}/manifest.json",
                $"{ContentPaths.Forge}/{projectName}/drafts/ch-01.md",
                $"{ContentPaths.Forge}/{projectName}/run-lore.md",
            ],
            assertions:
            [
                new ExpectedProviderRequestSectionAssertion("## Chapter Brief"),
                new ExpectedForgeManifestStageAssertion("Review", expectedPaused: true),
                new ExpectedForgeChapterDiscoveredAssertion("ch-01"),
                new ExpectedForgePauseSurfacedAssertion(),
                new ExpectedForgeStatusMatchesManifestAssertion(),
                new ExpectedArtifactPresenceAssertion($"{ContentPaths.Forge}/{projectName}/drafts/ch-01.md"),
            ],
            ct);
        phases.Add(startPhase);

        if (startPhase.Run.ForgeManifest?.Paused == true)
        {
            var approvePhase = await RunPhaseAsync(
                phaseName: "approve",
                projectName: projectName,
                route: $"/api/debug/bridge/forge/{projectName}/approve",
                artifactPaths:
                [
                    $"{ContentPaths.Forge}/{projectName}/manifest.json",
                    $"{ContentPaths.Forge}/{projectName}/output/story.md",
                    $"{ContentPaths.Forge}/{projectName}/run-lore.md",
                ],
                assertions:
                [
                    new ExpectedProviderRequestSectionAssertion("## Chapter Draft"),
                    new ExpectedForgeManifestStageAssertion("Done", expectedPaused: false),
                    new ExpectedForgeStatusMatchesManifestAssertion(),
                    new ExpectedArtifactPresenceAssertion($"{ContentPaths.Forge}/{projectName}/output/story.md"),
                    new ExpectedArtifactPresenceAssertion($"{ContentPaths.Forge}/{projectName}/run-lore.md"),
                ],
                ct);
            phases.Add(approvePhase);
        }

        return new HarnessForgeScenarioReport
        {
            ScenarioName = "forge-pause-resume",
            ProjectName = projectName,
            Phases = phases,
        };
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
        if (Directory.Exists(_contentRoot))
        {
            Directory.Delete(_contentRoot, recursive: true);
        }
    }

    private async Task<HarnessForgePhaseReport> RunPhaseAsync(
        string phaseName,
        string projectName,
        string route,
        IReadOnlyList<string> artifactPaths,
        IReadOnlyList<IHarnessAssertion> assertions,
        CancellationToken ct)
    {
        var providerTraceStartIndex = _providerHost.TraceStore.Snapshot().Count;
        var startedAt = DateTimeOffset.UtcNow;

        var debugResponse = await InvokeJsonAsync<DebugBridgeForgeRunResponse>(
            "POST",
            route,
            null,
            ct);
        var completedAt = DateTimeOffset.UtcNow;

        var providerTraces = _providerHost.TraceStore.Snapshot()
            .Skip(providerTraceStartIndex)
            .ToList();
        var collectedForgeRun = ToCollectedForgeRun(debugResponse);
        var forgeTrace = HarnessAppTraceBuilder.FromCollectedForgeRun(collectedForgeRun);
        var manifest = await LoadManifestSnapshotAsync(projectName, ct);
        var artifactTrace = await HarnessArtifactCollector.CaptureAsync(_contentRoot, artifactPaths, ct);

        var run = new DualSidedHarnessRun
        {
            ScenarioName = $"forge-pause-resume/{phaseName}",
            StartedAt = startedAt,
            CompletedAt = completedAt,
            ProviderTraces = providerTraces,
            ForgeTrace = forgeTrace,
            ForgeManifest = manifest,
            ArtifactTrace = artifactTrace,
        };

        var evaluation = new HarnessEvaluator().Evaluate(run, assertions);
        return new HarnessForgePhaseReport
        {
            PhaseName = phaseName,
            Run = run,
            Evaluation = evaluation,
        };
    }

    private async Task<HarnessForgeManifestSnapshot> LoadManifestSnapshotAsync(
        string projectName,
        CancellationToken ct)
    {
        var manifestPath = Path.Combine(_contentRoot, ContentPaths.Forge, projectName, "manifest.json");
        var json = await File.ReadAllTextAsync(manifestPath, ct);
        var manifest = JsonSerializer.Deserialize<ForgeManifest>(json, JsonOptions)
            ?? throw new InvalidOperationException($"Could not deserialize manifest at {manifestPath}.");
        return HarnessAppTraceBuilder.FromManifest(manifest);
    }

    private static HarnessCollectedForgeRun ToCollectedForgeRun(DebugBridgeForgeRunResponse response)
    {
        return new HarnessCollectedForgeRun
        {
            ProjectName = response.ProjectName,
            Operation = response.Operation,
            FinalEventType = response.FinalEventType,
            Events = response.Events
                .Select(evt => new HarnessCollectedForgeEvent
                {
                    Type = evt.Type,
                    Message = evt.Message,
                    Source = evt.Source,
                    Chapter = evt.Chapter,
                    Status = evt.Status,
                    WordCount = evt.WordCount,
                    Detail = evt.Detail,
                    ChaptersComplete = evt.ChaptersComplete,
                    TotalTokens = evt.TotalTokens,
                })
                .ToList(),
            Status = response.Status is null ? null : ToStatusSnapshot(response.Status),
        };
    }

    private static HarnessForgeStatusSnapshot ToStatusSnapshot(ForgeStatusResponse response)
    {
        return new HarnessForgeStatusSnapshot
        {
            ProjectName = response.ProjectName,
            Stage = response.Stage,
            ChapterCount = response.ChapterCount,
            Paused = response.Paused,
            Chapters = response.Chapters.ToDictionary(
                pair => pair.Key,
                pair => new HarnessForgeChapterSnapshot(
                    pair.Value.State,
                    pair.Value.RevisionCount,
                    pair.Value.WordCount)),
            Stats = new HarnessForgeStatsSnapshot(
                response.Stats.TotalInputTokens,
                response.Stats.TotalOutputTokens,
                response.Stats.AgentCalls,
                response.Stats.ChaptersRevised),
        };
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

        var appConfig = CreateAppConfig();
        builder.Services.AddSingleton(appConfig);
        builder.Services.AddSingleton<AtomicFileWriter>();
        builder.Services.AddSingleton<IContentFileService>(sp =>
            new FileSystemContentService(
                _contentRoot,
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemContentService>>()));
        builder.Services.AddSingleton<ILoreStore>(sp =>
            new FileSystemLoreStore(
                Path.Combine(_contentRoot, ContentPaths.Lore),
                sp.GetRequiredService<ILogger<FileSystemLoreStore>>()));
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

            RegisterHarnessProvider(registry, "forge-planner", "forge-planner-model", requiresReasoning: true);
            RegisterHarnessProvider(registry, "forge-writer", "forge-writer-model", requiresReasoning: true);
            RegisterHarnessProvider(registry, "forge-reviewer", "forge-reviewer-model", requiresReasoning: true);
            RegisterHarnessProvider(registry, "librarian", "forge-librarian-model", requiresReasoning: true);
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
        builder.Services.AddSingleton<IToolHandler>(sp => sp.GetRequiredService<QueryLoreHandler>());
        builder.Services.AddSingleton<IToolHandler, ReadFileHandler>();
        builder.Services.AddSingleton<IToolHandler, WriteFileHandler>();
        builder.Services.AddSingleton<IToolHandler, ListFilesHandler>();

        builder.Services.AddSingleton<ForgePlannerAgent>();
        builder.Services.AddSingleton<ForgeWriterAgent>();
        builder.Services.AddSingleton<ForgeReviewerAgent>(sp =>
            new ForgeReviewerAgent(
                sp.GetRequiredService<ICompletionService>(),
                sp.GetRequiredService<AppConfig>(),
                sp.GetRequiredService<ILogger<ForgeReviewerAgent>>()));

        builder.Services.AddSingleton<IPipelineStage, PlanningStage>();
        builder.Services.AddSingleton<IPipelineStage, DesignStage>();
        builder.Services.AddSingleton<IPipelineStage, WritingStage>();
        builder.Services.AddSingleton<IPipelineStage, ReviewStage>();
        builder.Services.AddSingleton<IPipelineStage, AssemblyStage>();
        builder.Services.AddSingleton<ForgePipeline>(sp =>
            new ForgePipeline(
                sp.GetRequiredService<IEnumerable<IPipelineStage>>(),
                sp.GetRequiredService<IContentFileService>(),
                sp.GetRequiredService<ILogger<ForgePipeline>>(),
                TimeSpan.FromMinutes(sp.GetRequiredService<AppConfig>().Forge.StageTimeoutMinutes)));

        var app = builder.Build();
        app.MapForgeEndpoints();
        app.MapForgeDebugBridgeEndpoints();
        return app;
    }

    private static AppConfig CreateAppConfig()
    {
        return new AppConfig
        {
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
            Diagnostics = new DiagnosticsConfig
            {
                LivePanel = true,
            },
            Forge = new ForgeConfig
            {
                PauseAfterChapter1 = true,
                StageTimeoutMinutes = 10,
            },
            Models = new ModelsConfig
            {
                ForgePlanner = "forge-planner",
                ForgeWriter = "forge-writer",
                ForgeReviewer = "forge-reviewer",
                Librarian = "librarian",
            },
        };
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

    private static void SeedContentRoot(string contentRoot)
    {
        foreach (var directory in ContentPaths.AllDirectories)
        {
            Directory.CreateDirectory(Path.Combine(contentRoot, directory));
        }

        File.WriteAllText(
            Path.Combine(contentRoot, ContentPaths.WritingStyles, "default.md"),
            "Write with lush, high-clarity third-person romantic suspense prose.");
        File.WriteAllText(
            Path.Combine(contentRoot, ContentPaths.NarrativeRules, "default.md"),
            "Preserve canon details and keep timeline continuity explicit.");
        File.WriteAllText(
            Path.Combine(contentRoot, ContentPaths.LibrarianPrompts, "default.md"),
            "Return only canon-backed answers with source attribution.");

        var loreSetDir = Path.Combine(contentRoot, ContentPaths.Lore, "default");
        Directory.CreateDirectory(loreSetDir);
        File.WriteAllText(
            Path.Combine(loreSetDir, "world.md"),
            """
            Aurora once hid a sapphire ring inside the conservatory wall.
            The arranged marriage contract binds Aurora and Lucian to present a united front.
            The winter gala is the first public test of that alliance.
            """);
    }

    private async Task<T> InvokeJsonAsync<T>(
        string method,
        string route,
        string? jsonBody,
        CancellationToken ct)
    {
        var (statusCode, body) = await InvokeAsync(method, route, jsonBody, ct);
        if (statusCode is < 200 or >= 300)
        {
            throw new InvalidOperationException(
                $"Request {method} {route} failed with status {statusCode}: {body}");
        }

        return JsonSerializer.Deserialize<T>(body, JsonOptions)
            ?? throw new InvalidOperationException($"Could not deserialize response for {method} {route}.");
    }

    private async Task<(int StatusCode, string Body)> InvokeAsync(
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
        };
        context.Request.Method = method;
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Path = route;
        ApplyRouteValues(context, endpoint.RoutePattern, route);
        context.Response.Body = new MemoryStream();
        context.RequestAborted = ct;

        if (jsonBody is not null)
        {
            var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
            context.Request.ContentType = "application/json";
            context.Request.ContentLength = bodyBytes.Length;
            context.Request.Body = new MemoryStream(bodyBytes);
            context.Features.Set<IHttpRequestBodyDetectionFeature>(new TestRequestBodyDetectionFeature());
        }

        var requestDelegate = endpoint.RequestDelegate
            ?? throw new InvalidOperationException($"No request delegate found for {method} {route}.");
        await requestDelegate(context);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8, leaveOpen: true);
        return (context.Response.StatusCode, await reader.ReadToEndAsync(ct));
    }

    private static bool RouteMatches(RoutePattern pattern, string route)
    {
        var rawText = pattern.RawText;
        if (!string.IsNullOrWhiteSpace(rawText)
            && string.Equals(rawText.TrimStart('/'), route.TrimStart('/'), StringComparison.Ordinal))
        {
            return true;
        }

        var patternSegments = pattern.PathSegments;
        var routeSegments = route.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (patternSegments.Count != routeSegments.Length)
        {
            return false;
        }

        for (var i = 0; i < patternSegments.Count; i++)
        {
            var literalParts = patternSegments[i].Parts.OfType<RoutePatternLiteralPart>().ToList();
            if (literalParts.Count == 0)
            {
                continue;
            }

            var literal = string.Concat(literalParts.Select(part => part.Content));
            if (!string.Equals(literal, routeSegments[i], StringComparison.Ordinal))
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

    private sealed class TestRequestBodyDetectionFeature : IHttpRequestBodyDetectionFeature
    {
        public bool CanHaveBody => true;
    }
}
