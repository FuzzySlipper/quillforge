using System.Text.Json;
using Microsoft.AspNetCore.Builder;
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
    private readonly HarnessDebugBridgeDriver _bridge;

    public HarnessForgeScenarioRunner(HarnessProviderHost providerHost)
    {
        _providerHost = providerHost;
        _contentRoot = Path.Combine(Path.GetTempPath(), $"quillforge-harness-forge-{Guid.NewGuid():N}");
        SeedContentRoot(_contentRoot);
        _app = BuildApp();
        _bridge = new HarnessDebugBridgeDriver(_app);
    }

    public async Task<HarnessForgeScenarioReport> RunCanonicalPauseResumeScenarioAsync(
        string projectName,
        string premise,
        CancellationToken ct = default)
    {
        var fixture = HarnessForgeScenarioFixtures.CreateCanonicalPauseResume(projectName, premise);
        return await RunScenarioAsync(fixture, ct);
    }

    public async Task<HarnessForgeScenarioReport> RunScenarioAsync(
        HarnessForgeScenarioFixture fixture,
        CancellationToken ct = default)
    {
        await _bridge.CreateForgeProjectAsync(fixture.ProjectName, fixture.Premise, ct);

        var phases = new List<HarnessForgePhaseReport>();
        foreach (var phase in fixture.Phases)
        {
            var phaseReport = await RunPhaseAsync(
                phaseName: phase.Name,
                projectName: fixture.ProjectName,
                operation: phase.Operation,
                artifactPaths: phase.ArtifactPaths,
                assertions: BuildAssertions(phase.Expectations),
                ct);
            phases.Add(phaseReport);
        }

        return new HarnessForgeScenarioReport
        {
            ScenarioName = fixture.Name,
            ProjectName = fixture.ProjectName,
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
        string operation,
        IReadOnlyList<string> artifactPaths,
        IReadOnlyList<IHarnessAssertion> assertions,
        CancellationToken ct)
    {
        var providerTraceStartIndex = _providerHost.TraceStore.Snapshot().Count;
        var startedAt = DateTimeOffset.UtcNow;

        DebugBridgeForgeRunResponse debugResponse = operation switch
        {
            "design" => await _bridge.RunForgeDesignAsync(projectName, ct),
            "start" => await _bridge.RunForgeStartAsync(projectName, ct),
            "approve" => await _bridge.RunForgeApproveAsync(projectName, ct),
            _ => throw new InvalidOperationException($"Unsupported Forge operation '{operation}'."),
        };
        var completedAt = DateTimeOffset.UtcNow;

        var providerTraces = _providerHost.TraceStore.Snapshot()
            .Skip(providerTraceStartIndex)
            .ToList();
        var providerTraceIds = providerTraces.Select(trace => trace.TraceId).ToList();
        var collectedForgeRun = ToCollectedForgeRun(debugResponse);
        var forgeTrace = HarnessAppTraceBuilder.FromCollectedForgeRun(collectedForgeRun) with
        {
            RunId = _providerHost.ArtifactStore.RunId,
            RelatedProviderTraceIds = providerTraceIds,
        };
        var manifest = await LoadManifestSnapshotAsync(projectName, ct);
        var artifactTrace = (await HarnessArtifactCollector.CaptureAsync(_contentRoot, artifactPaths, ct)) with
        {
            RunId = _providerHost.ArtifactStore.RunId,
            RelatedProviderTraceIds = providerTraceIds,
        };

        var run = new DualSidedHarnessRun
        {
            RunId = _providerHost.ArtifactStore.RunId,
            ScenarioName = $"forge-pause-resume/{phaseName}",
            StartedAt = startedAt,
            CompletedAt = completedAt,
            ProviderTraces = providerTraces,
            ForgeTrace = forgeTrace,
            ForgeManifest = manifest,
            ArtifactTrace = artifactTrace,
        };

        var evaluation = new HarnessEvaluator().Evaluate(run, assertions);
        var persistedReport = HarnessRunReportWriter.WriteForgePhaseReport(
            _providerHost.ArtifactStore,
            phaseName,
            run,
            evaluation);
        return new HarnessForgePhaseReport
        {
            PhaseName = phaseName,
            Run = run,
            Evaluation = evaluation,
            PersistedReport = persistedReport,
        };
    }

    private static IReadOnlyList<IHarnessAssertion> BuildAssertions(HarnessForgePhaseExpectations expectations)
    {
        var assertions = new List<IHarnessAssertion>();

        foreach (var section in expectations.ProviderRequestSections)
        {
            assertions.Add(new ExpectedProviderRequestSectionAssertion(section));
        }

        if (!string.IsNullOrWhiteSpace(expectations.ExpectedManifestStage))
        {
            assertions.Add(new ExpectedForgeManifestStageAssertion(
                expectations.ExpectedManifestStage,
                expectations.ExpectedPaused));
        }

        foreach (var chapterId in expectations.ExpectedChapterIds)
        {
            assertions.Add(new ExpectedForgeChapterDiscoveredAssertion(chapterId));
        }

        if (expectations.RequirePauseSurfaced)
        {
            assertions.Add(new ExpectedForgePauseSurfacedAssertion());
        }

        if (expectations.RequireStatusMatchesManifest)
        {
            assertions.Add(new ExpectedForgeStatusMatchesManifestAssertion());
        }

        foreach (var artifactPath in expectations.ExpectedArtifactPaths)
        {
            assertions.Add(new ExpectedArtifactPresenceAssertion(artifactPath));
        }

        return assertions;
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

}
