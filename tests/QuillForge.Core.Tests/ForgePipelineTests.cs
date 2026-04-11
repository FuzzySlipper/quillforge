using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Pipeline;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

public class ForgePipelineTests
{
    private static readonly ILoggerFactory LogFactory = NullLoggerFactory.Instance;

    private static ForgeContext CreateContext(
        FakeContentFileService fileService,
        FakeCompletionService completionService,
        ForgeManifest? manifest = null)
    {
        var continuation = new ContinuationStrategy(LogFactory.CreateLogger<ContinuationStrategy>());
        var toolLoop = new ToolLoop(completionService, continuation, LogFactory.CreateLogger<ToolLoop>(), new AppConfig());

        var fakeCompletionForReviewer = new FakeCompletionService();

        return new ForgeContext
        {
            Manifest = manifest ?? new ForgeManifest
            {
                ProjectName = "test-project",
                ChapterCount = 2,
                Chapters = new Dictionary<string, ChapterStatus>
                {
                    ["ch-01"] = new() { State = ChapterState.Pending },
                    ["ch-02"] = new() { State = ChapterState.Pending },
                },
                CreatedAt = DateTimeOffset.UtcNow,
            },
            ProjectPath = "forge/test-project",
            Planner = new ForgePlannerAgent(toolLoop, new AppConfig(), LogFactory.CreateLogger<ForgePlannerAgent>()),
            Writer = new ForgeWriterAgent(toolLoop, new AppConfig(), LogFactory.CreateLogger<ForgeWriterAgent>()),
            Reviewer = new ForgeReviewerAgent(fakeCompletionForReviewer, new AppConfig(), LogFactory.CreateLogger<ForgeReviewerAgent>()),
            PlannerTools = [],
            WriterTools = [],
            FileService = fileService,
            AgentContext = new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = Mode.Forge },
            WritingStyle = "Write clearly and concisely.",
        };
    }

    [Fact]
    public async Task PlanningStage_EmitsStartAndComplete()
    {
        var fileService = new FakeContentFileService();
        var completion = new FakeCompletionService();
        completion.EnqueueText("I've created the outline and briefs.");

        var stage = new PlanningStage(LogFactory.CreateLogger<PlanningStage>());
        var context = CreateContext(fileService, completion);

        var events = new List<ForgeEvent>();
        await foreach (var evt in stage.ExecuteAsync(context, CancellationToken.None))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e is StageStartedEvent s && s.StageName == "Planning");
        Assert.Contains(events, e => e is StageCompletedEvent s && s.StageName == "Planning");
    }

    [Fact]
    public async Task WritingStage_SkipsCompletedChapters()
    {
        var fileService = new FakeContentFileService();
        fileService.SeedFile("forge/test-project/plan/ch-02-brief.md", "Write about dragons.");
        fileService.SeedFile("forge/test-project/drafts/ch-01.md", "Chapter 1 already done.");

        var completion = new FakeCompletionService();
        completion.EnqueueText("The dragon roared across the valley...");

        var manifest = new ForgeManifest
        {
            ProjectName = "test-project",
            Chapters = new Dictionary<string, ChapterStatus>
            {
                ["ch-01"] = new() { State = ChapterState.Done, WordCount = 500 },
                ["ch-02"] = new() { State = ChapterState.Pending },
            },
        };

        var context = CreateContext(fileService, completion, manifest);
        var stage = new WritingStage(LogFactory.CreateLogger<WritingStage>());

        var events = new List<ForgeEvent>();
        await foreach (var evt in stage.ExecuteAsync(context, CancellationToken.None))
        {
            events.Add(evt);
        }

        // ch-01 should be skipped, ch-02 should be written
        var chapterEvents = events.OfType<ChapterProgressEvent>().ToList();
        Assert.DoesNotContain(chapterEvents, e => e.ChapterId == "ch-01" && e.Status == "writing");
        Assert.Contains(chapterEvents, e => e.ChapterId == "ch-02" && e.Status == "writing");

        // Draft should be saved
        Assert.True(fileService.Files.ContainsKey("forge/test-project/drafts/ch-02.md"));
    }

    [Fact]
    public async Task AssemblyStage_CombinesAllDrafts()
    {
        var fileService = new FakeContentFileService();
        fileService.SeedFile("forge/test-project/drafts/ch-01.md", "Chapter 1 text.");
        fileService.SeedFile("forge/test-project/drafts/ch-02.md", "Chapter 2 text.");

        var completion = new FakeCompletionService();
        var manifest = new ForgeManifest
        {
            ProjectName = "test-project",
            Chapters = new Dictionary<string, ChapterStatus>
            {
                ["ch-01"] = new() { State = ChapterState.Done },
                ["ch-02"] = new() { State = ChapterState.Done },
            },
        };

        var context = CreateContext(fileService, completion, manifest);
        var stage = new AssemblyStage(LogFactory.CreateLogger<AssemblyStage>());

        await foreach (var evt in stage.ExecuteAsync(context, CancellationToken.None))
        { /* consume events */ }

        var output = fileService.Files["forge/test-project/output/story.md"];
        Assert.Contains("Chapter 1 text.", output);
        Assert.Contains("Chapter 2 text.", output);
    }

    [Fact]
    public async Task AssemblyStage_MarksFlaggedChapters()
    {
        var fileService = new FakeContentFileService();
        fileService.SeedFile("forge/test-project/drafts/ch-01.md", "Flagged chapter.");

        var completion = new FakeCompletionService();
        var manifest = new ForgeManifest
        {
            ProjectName = "test-project",
            Chapters = new Dictionary<string, ChapterStatus>
            {
                ["ch-01"] = new() { State = ChapterState.Flagged },
            },
        };

        var context = CreateContext(fileService, completion, manifest);
        var stage = new AssemblyStage(LogFactory.CreateLogger<AssemblyStage>());

        await foreach (var evt in stage.ExecuteAsync(context, CancellationToken.None))
        { }

        var output = fileService.Files["forge/test-project/output/story.md"];
        Assert.Contains("WARNING", output);
        Assert.Contains("Flagged chapter.", output);
    }

    [Fact]
    public async Task ForgePipeline_PersistsManifestAfterEachStage()
    {
        var fileService = new FakeContentFileService();
        var completion = new FakeCompletionService();
        // Planning response
        completion.EnqueueText("Planning done.");
        // Design — needs outline to exist
        fileService.SeedFile("forge/test-project/plan/outline.md", "The story arc...");
        completion.EnqueueText("Design refined.");

        var manifest = new ForgeManifest
        {
            ProjectName = "test-project",
            Stage = ForgeStage.Planning,
            Chapters = [],
        };

        var context = CreateContext(fileService, completion, manifest);

        IPipelineStage[] stages =
        [
            new PlanningStage(LogFactory.CreateLogger<PlanningStage>()),
            new DesignStage(LogFactory.CreateLogger<DesignStage>()),
        ];

        var pipeline = new ForgePipeline(stages, fileService,
            LogFactory.CreateLogger<ForgePipeline>());

        await foreach (var evt in pipeline.RunAsync(context, CancellationToken.None))
        { }

        // Manifest should have been persisted
        Assert.True(fileService.Files.ContainsKey("forge/test-project/manifest.json"));

        // Stage should be advanced past design
        Assert.True(context.Manifest.Stage > ForgeStage.Design);
    }

    [Fact]
    public async Task ForgePipeline_ResumeSkipsCompletedStages()
    {
        var fileService = new FakeContentFileService();
        fileService.SeedFile("forge/test-project/plan/outline.md", "Already planned.");
        var completion = new FakeCompletionService();
        completion.EnqueueText("Design done.");

        var manifest = new ForgeManifest
        {
            ProjectName = "test-project",
            Stage = ForgeStage.Design, // Already past planning
            Chapters = [],
        };

        var context = CreateContext(fileService, completion, manifest);

        IPipelineStage[] stages =
        [
            new PlanningStage(LogFactory.CreateLogger<PlanningStage>()),
            new DesignStage(LogFactory.CreateLogger<DesignStage>()),
        ];

        var pipeline = new ForgePipeline(stages, fileService,
            LogFactory.CreateLogger<ForgePipeline>());

        var events = new List<ForgeEvent>();
        await foreach (var evt in pipeline.RunAsync(context, CancellationToken.None))
        {
            events.Add(evt);
        }

        // Planning should NOT have been executed
        Assert.DoesNotContain(events, e => e is StageStartedEvent s && s.StageName == "Planning");
        // Design should have been executed
        Assert.Contains(events, e => e is StageStartedEvent s && s.StageName == "Design");
    }

    [Fact]
    public async Task ForgePipeline_PauseStopsAtNextBoundary()
    {
        var fileService = new FakeContentFileService();
        var completion = new FakeCompletionService();
        completion.EnqueueText("Planning done.");

        var manifest = new ForgeManifest
        {
            ProjectName = "test-project",
            Stage = ForgeStage.Planning,
            Chapters = [],
        };

        var context = CreateContext(fileService, completion, manifest);

        IPipelineStage[] stages =
        [
            new PlanningStage(LogFactory.CreateLogger<PlanningStage>()),
            new DesignStage(LogFactory.CreateLogger<DesignStage>()),
        ];

        var pipeline = new ForgePipeline(stages, fileService,
            LogFactory.CreateLogger<ForgePipeline>());

        // Request pause before starting — should stop after first stage
        var events = new List<ForgeEvent>();
        var started = false;
        await foreach (var evt in pipeline.RunAsync(context, CancellationToken.None))
        {
            events.Add(evt);
            if (evt is StageCompletedEvent && !started)
            {
                started = true;
                pipeline.RequestPause();
            }
        }

        // Design should NOT have started
        Assert.DoesNotContain(events, e => e is StageStartedEvent s && s.StageName == "Design");
        Assert.True(context.Manifest.Paused);
    }

    [Fact]
    public async Task ForgePipeline_StageFailure_DoesNotAdvanceManifest()
    {
        var fileService = new FakeContentFileService();
        var completion = new FakeCompletionService();
        var manifest = new ForgeManifest
        {
            ProjectName = "test-project",
            Stage = ForgeStage.Planning,
            Chapters = [],
        };

        var context = CreateContext(fileService, completion, manifest);
        var planningStage = new ThrowingStage(ForgeStage.Planning, "Planning");
        var designStage = new RecordingStage(ForgeStage.Design, "Design");

        IPipelineStage[] stages =
        [
            planningStage,
            designStage,
        ];

        var pipeline = new ForgePipeline(stages, fileService,
            LogFactory.CreateLogger<ForgePipeline>());

        var events = new List<ForgeEvent>();
        await foreach (var evt in pipeline.RunAsync(context, CancellationToken.None))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e is ForgeErrorEvent err && err.StageName == "Planning");
        Assert.False(designStage.WasExecuted);
        Assert.Equal(ForgeStage.Planning, context.Manifest.Stage);
        Assert.True(context.Manifest.Paused);
    }

    [Fact]
    public async Task ForgePipeline_Diagnostics_ReportsState()
    {
        var fileService = new FakeContentFileService();
        var pipeline = new ForgePipeline([], fileService,
            LogFactory.CreateLogger<ForgePipeline>());

        Assert.Equal("forge", pipeline.Category);

        var diag = await pipeline.GetDiagnosticsAsync();
        Assert.False((bool)diag["is_running"]);
    }

    // === Forge Stats Regression Tests ===
    // These tests ensure forge stats accounting is non-optional. If you add a new
    // forge stage or agent that consumes tokens, add a test here proving it records
    // usage through ForgeStatsTracker.

    [Fact]
    public async Task PlanningStage_RecordsTokenUsageInStats()
    {
        var fileService = new FakeContentFileService();
        var completion = new FakeCompletionService();
        completion.EnqueueText("Planning done.");

        var stage = new PlanningStage(LogFactory.CreateLogger<PlanningStage>());
        var context = CreateContext(fileService, completion);

        await foreach (var _ in stage.ExecuteAsync(context, CancellationToken.None)) { }

        var stats = context.StatsTracker.Snapshot();
        Assert.True(stats.TotalInputTokens > 0, "Planning should record input tokens");
        Assert.True(stats.TotalOutputTokens > 0, "Planning should record output tokens");
        Assert.True(stats.AgentCalls > 0, "Planning should record at least one agent call");
    }

    [Fact]
    public async Task DesignStage_RecordsTokenUsageInStats()
    {
        var fileService = new FakeContentFileService();
        fileService.SeedFile("forge/test-project/plan/outline.md", "The story arc...");
        var completion = new FakeCompletionService();
        completion.EnqueueText("Design refined.");

        var stage = new DesignStage(LogFactory.CreateLogger<DesignStage>());
        var context = CreateContext(fileService, completion);

        await foreach (var _ in stage.ExecuteAsync(context, CancellationToken.None)) { }

        var stats = context.StatsTracker.Snapshot();
        Assert.True(stats.TotalInputTokens > 0, "Design should record input tokens");
        Assert.True(stats.AgentCalls > 0, "Design should record at least one agent call");
    }

    [Fact]
    public async Task StatsAccumulateAcrossMultipleStages()
    {
        var fileService = new FakeContentFileService();
        var completion = new FakeCompletionService();
        completion.EnqueueText("Planning done.");
        fileService.SeedFile("forge/test-project/plan/outline.md", "The story arc...");
        completion.EnqueueText("Design refined.");

        var manifest = new ForgeManifest
        {
            ProjectName = "test-project",
            Stage = ForgeStage.Planning,
            Chapters = [],
        };

        var context = CreateContext(fileService, completion, manifest);

        IPipelineStage[] stages =
        [
            new PlanningStage(LogFactory.CreateLogger<PlanningStage>()),
            new DesignStage(LogFactory.CreateLogger<DesignStage>()),
        ];

        var pipeline = new ForgePipeline(stages, fileService,
            LogFactory.CreateLogger<ForgePipeline>());

        await foreach (var _ in pipeline.RunAsync(context, CancellationToken.None)) { }

        // Two agent calls (planning + design), each with TokenUsage(10, 20) from the fake
        var stats = context.Manifest.Stats;
        Assert.Equal(20, stats.TotalInputTokens);  // 10 + 10
        Assert.Equal(40, stats.TotalOutputTokens); // 20 + 20
        Assert.Equal(2, stats.AgentCalls);
    }

    [Fact]
    public async Task FailedStage_PreservesAlreadyRecordedStats()
    {
        var fileService = new FakeContentFileService();
        var completion = new FakeCompletionService();

        var manifest = new ForgeManifest
        {
            ProjectName = "test-project",
            Stage = ForgeStage.Planning,
            Chapters = [],
        };

        var context = CreateContext(fileService, completion, manifest);

        // Planning stage succeeds, design stage fails
        var planningStage = new StatsRecordingStage(ForgeStage.Planning, "Planning");
        var designStage = new ThrowingStage(ForgeStage.Design, "Design");

        IPipelineStage[] stages = [planningStage, designStage];

        var pipeline = new ForgePipeline(stages, fileService,
            LogFactory.CreateLogger<ForgePipeline>());

        await foreach (var _ in pipeline.RunAsync(context, CancellationToken.None)) { }

        // Stats from the successful planning stage should be preserved
        var stats = context.Manifest.Stats;
        Assert.True(stats.TotalInputTokens > 0, "Stats from planning should survive design failure");
        Assert.True(stats.AgentCalls > 0, "Agent calls from planning should survive design failure");
    }

    [Fact]
    public async Task ResumedRun_ContinuesAccumulatingFromExistingStats()
    {
        var fileService = new FakeContentFileService();
        fileService.SeedFile("forge/test-project/plan/outline.md", "Already planned.");
        var completion = new FakeCompletionService();
        completion.EnqueueText("Design done.");

        // Manifest with pre-existing stats from a prior run
        var existingStats = new ForgeStats
        {
            TotalInputTokens = 100,
            TotalOutputTokens = 200,
            AgentCalls = 5,
            StageTiming = new Dictionary<string, StageTiming>
            {
                ["Planning"] = new(DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddMinutes(-5)),
            },
        };

        var manifest = new ForgeManifest
        {
            ProjectName = "test-project",
            Stage = ForgeStage.Design,
            Chapters = [],
            Stats = existingStats,
        };

        var context = CreateContext(fileService, completion, manifest);

        IPipelineStage[] stages =
        [
            new PlanningStage(LogFactory.CreateLogger<PlanningStage>()),
            new DesignStage(LogFactory.CreateLogger<DesignStage>()),
        ];

        var pipeline = new ForgePipeline(stages, fileService,
            LogFactory.CreateLogger<ForgePipeline>());

        await foreach (var _ in pipeline.RunAsync(context, CancellationToken.None)) { }

        var stats = context.Manifest.Stats;
        // Should have accumulated on top of the existing 100/200/5
        Assert.Equal(110, stats.TotalInputTokens);  // 100 + 10
        Assert.Equal(220, stats.TotalOutputTokens); // 200 + 20
        Assert.Equal(6, stats.AgentCalls);           // 5 + 1
        // Prior stage timing should be preserved
        Assert.True(stats.StageTiming.ContainsKey("Planning"), "Prior Planning timing should be preserved");
    }

    [Fact]
    public async Task PausePreservesAccumulatedStats()
    {
        var fileService = new FakeContentFileService();
        var completion = new FakeCompletionService();
        completion.EnqueueText("Planning done.");

        var manifest = new ForgeManifest
        {
            ProjectName = "test-project",
            Stage = ForgeStage.Planning,
            Chapters = [],
        };

        var context = CreateContext(fileService, completion, manifest);

        IPipelineStage[] stages =
        [
            new PlanningStage(LogFactory.CreateLogger<PlanningStage>()),
            new DesignStage(LogFactory.CreateLogger<DesignStage>()),
        ];

        var pipeline = new ForgePipeline(stages, fileService,
            LogFactory.CreateLogger<ForgePipeline>());

        // Pause after planning completes
        var events = new List<ForgeEvent>();
        await foreach (var evt in pipeline.RunAsync(context, CancellationToken.None))
        {
            events.Add(evt);
            if (evt is StageCompletedEvent)
            {
                pipeline.RequestPause();
            }
        }

        Assert.True(context.Manifest.Paused);
        var stats = context.Manifest.Stats;
        Assert.True(stats.TotalInputTokens > 0, "Stats should survive pause");
        Assert.True(stats.AgentCalls > 0, "Agent calls should survive pause");
    }

    [Fact]
    public async Task StageTimingIsRecordedForCompletedStages()
    {
        var fileService = new FakeContentFileService();
        var completion = new FakeCompletionService();
        completion.EnqueueText("Planning done.");
        fileService.SeedFile("forge/test-project/plan/outline.md", "The story arc...");
        completion.EnqueueText("Design done.");

        var manifest = new ForgeManifest
        {
            ProjectName = "test-project",
            Stage = ForgeStage.Planning,
            Chapters = [],
        };

        var context = CreateContext(fileService, completion, manifest);

        IPipelineStage[] stages =
        [
            new PlanningStage(LogFactory.CreateLogger<PlanningStage>()),
            new DesignStage(LogFactory.CreateLogger<DesignStage>()),
        ];

        var pipeline = new ForgePipeline(stages, fileService,
            LogFactory.CreateLogger<ForgePipeline>());

        await foreach (var _ in pipeline.RunAsync(context, CancellationToken.None)) { }

        var timing = context.Manifest.Stats.StageTiming;
        Assert.True(timing.ContainsKey("Planning"), "Planning timing should be recorded");
        Assert.True(timing.ContainsKey("Design"), "Design timing should be recorded");
        Assert.NotNull(timing["Planning"].End);
        Assert.NotNull(timing["Design"].End);
    }

    [Fact]
    public async Task FailedStage_RecordsTimingWithEndTimestamp()
    {
        var fileService = new FakeContentFileService();
        var completion = new FakeCompletionService();

        var manifest = new ForgeManifest
        {
            ProjectName = "test-project",
            Stage = ForgeStage.Planning,
            Chapters = [],
        };

        var context = CreateContext(fileService, completion, manifest);
        var throwingStage = new ThrowingStage(ForgeStage.Planning, "Planning");

        var pipeline = new ForgePipeline([throwingStage], fileService,
            LogFactory.CreateLogger<ForgePipeline>());

        await foreach (var _ in pipeline.RunAsync(context, CancellationToken.None)) { }

        var timing = context.Manifest.Stats.StageTiming;
        Assert.True(timing.ContainsKey("Planning"), "Failed stage should still have timing");
        Assert.NotNull(timing["Planning"].End);
    }

    [Fact]
    public async Task ForgeCompletedEvent_ContainsNonZeroStats()
    {
        var fileService = new FakeContentFileService();
        var completion = new FakeCompletionService();
        completion.EnqueueText("Planning done.");

        var manifest = new ForgeManifest
        {
            ProjectName = "test-project",
            Stage = ForgeStage.Planning,
            Chapters = [],
        };

        var context = CreateContext(fileService, completion, manifest);

        IPipelineStage[] stages =
        [
            new PlanningStage(LogFactory.CreateLogger<PlanningStage>()),
        ];

        var pipeline = new ForgePipeline(stages, fileService,
            LogFactory.CreateLogger<ForgePipeline>());

        ForgeCompletedEvent? completedEvent = null;
        await foreach (var evt in pipeline.RunAsync(context, CancellationToken.None))
        {
            if (evt is ForgeCompletedEvent c) completedEvent = c;
        }

        Assert.NotNull(completedEvent);
        Assert.True(completedEvent.Stats.TotalInputTokens > 0,
            "ForgeCompletedEvent should report non-zero tokens");
        Assert.True(completedEvent.Stats.AgentCalls > 0,
            "ForgeCompletedEvent should report non-zero agent calls");
    }

    [Fact]
    public async Task Pipeline_WiresNestedCompletionCallback()
    {
        // Verifies that ForgePipeline.RunAsync wires OnNestedCompletion on AgentContext
        // so that nested LLM calls (e.g. librarian inside query_lore) get recorded.
        var fileService = new FakeContentFileService();
        var completion = new FakeCompletionService();

        var manifest = new ForgeManifest
        {
            ProjectName = "test-project",
            Stage = ForgeStage.Planning,
            Chapters = [],
        };

        var context = CreateContext(fileService, completion, manifest);
        // Before pipeline runs, callback should be null
        Assert.Null(context.AgentContext.OnNestedCompletion);

        // Use a stage that captures and invokes the callback to simulate a tool handler
        var callbackStage = new NestedCompletionStage(ForgeStage.Planning, "Planning");

        var pipeline = new ForgePipeline([callbackStage], fileService,
            LogFactory.CreateLogger<ForgePipeline>());

        await foreach (var _ in pipeline.RunAsync(context, CancellationToken.None)) { }

        // The stage should have seen a non-null callback and invoked it
        Assert.True(callbackStage.CallbackWasPresent, "OnNestedCompletion should be wired by the pipeline");
        // Stats should include the nested completion
        var stats = context.Manifest.Stats;
        Assert.Equal(75, stats.TotalInputTokens);  // 75 from nested librarian call
        Assert.Equal(150, stats.TotalOutputTokens); // 150 from nested librarian call
        Assert.Equal(1, stats.AgentCalls);
    }

    [Fact]
    public async Task NestedLibrarianUsage_IncludedInManifestStats_BeyondWriterTopLevel()
    {
        // End-to-end: a writing stage records both writer usage and nested lore usage.
        // This proves that forge-triggered query_lore calls show up in manifest stats.
        var fileService = new FakeContentFileService();
        var completion = new FakeCompletionService();

        var manifest = new ForgeManifest
        {
            ProjectName = "test-project",
            Stage = ForgeStage.Planning,
            Chapters = [],
        };

        var context = CreateContext(fileService, completion, manifest);

        // Stage that simulates both a writer completion and a nested librarian call
        var combinedStage = new WriterPlusLoreStage(ForgeStage.Planning, "Planning");

        var pipeline = new ForgePipeline([combinedStage], fileService,
            LogFactory.CreateLogger<ForgePipeline>());

        await foreach (var _ in pipeline.RunAsync(context, CancellationToken.None)) { }

        var stats = context.Manifest.Stats;
        // Writer: TokenUsage(10, 20), Librarian (nested): TokenUsage(30, 40) => total 40 / 60
        Assert.Equal(40, stats.TotalInputTokens);
        Assert.Equal(60, stats.TotalOutputTokens);
        Assert.Equal(2, stats.AgentCalls); // 1 writer + 1 librarian
    }

    /// <summary>
    /// A stage that checks whether OnNestedCompletion is wired and invokes it,
    /// simulating a tool handler making a nested LLM call.
    /// </summary>
    private sealed class NestedCompletionStage(ForgeStage stageEnum, string stageName) : IPipelineStage
    {
        public string StageName => stageName;
        public ForgeStage StageEnum => stageEnum;
        public bool CallbackWasPresent { get; private set; }

        public async IAsyncEnumerable<ForgeEvent> ExecuteAsync(
            ForgeContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield return new StageStartedEvent(StageName);
            await Task.Yield();

            CallbackWasPresent = context.AgentContext.OnNestedCompletion is not null;
            // Simulate a nested librarian call reporting usage
            context.AgentContext.OnNestedCompletion?.Invoke("librarian", new TokenUsage(75, 150));

            yield return new StageCompletedEvent(StageName);
        }
    }

    /// <summary>
    /// Simulates a writing stage that records both top-level writer usage
    /// and nested librarian usage through the callback, proving both appear in stats.
    /// </summary>
    private sealed class WriterPlusLoreStage(ForgeStage stageEnum, string stageName) : IPipelineStage
    {
        public string StageName => stageName;
        public ForgeStage StageEnum => stageEnum;

        public async IAsyncEnumerable<ForgeEvent> ExecuteAsync(
            ForgeContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield return new StageStartedEvent(StageName);
            await Task.Yield();

            // Simulate writer top-level completion
            context.StatsTracker.RecordCompletion("forge-writer", new TokenUsage(10, 20));
            // Simulate nested librarian call via OnNestedCompletion callback
            context.AgentContext.OnNestedCompletion?.Invoke("librarian", new TokenUsage(30, 40));

            yield return new StageCompletedEvent(StageName);
        }
    }

    /// <summary>
    /// A stage that records synthetic stats to simulate real agent work,
    /// used to test that stats survive subsequent stage failures.
    /// </summary>
    private sealed class StatsRecordingStage(ForgeStage stageEnum, string stageName) : IPipelineStage
    {
        public string StageName => stageName;
        public ForgeStage StageEnum => stageEnum;

        public async IAsyncEnumerable<ForgeEvent> ExecuteAsync(
            ForgeContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield return new StageStartedEvent(StageName);
            await Task.Yield();
            context.StatsTracker.RecordCompletion("test-agent", new TokenUsage(50, 100));
            yield return new StageCompletedEvent(StageName);
        }
    }

    private sealed class ThrowingStage(ForgeStage stageEnum, string stageName) : IPipelineStage
    {
        public string StageName => stageName;
        public ForgeStage StageEnum => stageEnum;

        public async IAsyncEnumerable<ForgeEvent> ExecuteAsync(
            ForgeContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            yield return new StageStartedEvent(StageName);
            await Task.Yield();
            throw new InvalidOperationException("boom");
        }
    }

    private sealed class RecordingStage(ForgeStage stageEnum, string stageName) : IPipelineStage
    {
        public string StageName => stageName;
        public ForgeStage StageEnum => stageEnum;
        public bool WasExecuted { get; private set; }

        public async IAsyncEnumerable<ForgeEvent> ExecuteAsync(
            ForgeContext context,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
        {
            WasExecuted = true;
            yield return new StageStartedEvent(StageName);
            await Task.Yield();
            yield return new StageCompletedEvent(StageName);
        }
    }
}
