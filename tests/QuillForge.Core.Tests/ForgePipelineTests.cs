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
        var toolLoop = new ToolLoop(completionService, continuation, LogFactory.CreateLogger<ToolLoop>());

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
            Planner = new ForgePlannerAgent(toolLoop, LogFactory.CreateLogger<ForgePlannerAgent>()),
            Writer = new ForgeWriterAgent(toolLoop, LogFactory.CreateLogger<ForgeWriterAgent>()),
            Reviewer = new ForgeReviewerAgent(fakeCompletionForReviewer, LogFactory.CreateLogger<ForgeReviewerAgent>()),
            WriterTools = [],
            FileService = fileService,
            AgentContext = new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = "forge" },
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
    public async Task ForgePipeline_Diagnostics_ReportsState()
    {
        var fileService = new FakeContentFileService();
        var pipeline = new ForgePipeline([], fileService,
            LogFactory.CreateLogger<ForgePipeline>());

        Assert.Equal("forge", pipeline.Category);

        var diag = await pipeline.GetDiagnosticsAsync();
        Assert.False((bool)diag["is_running"]);
    }
}
