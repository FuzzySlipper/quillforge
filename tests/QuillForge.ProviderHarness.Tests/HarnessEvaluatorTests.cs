using System.Text.Json;
using QuillForge.Core.Models;

namespace QuillForge.ProviderHarness.Tests;

public sealed class HarnessEvaluatorTests : IDisposable
{
    private readonly string _tempDir;

    public HarnessEvaluatorTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "quillforge-harness-evaluator-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void AppTraceBuilder_ConvertsCollectedStreamAndSessionSnapshot()
    {
        var sessionId = Guid.CreateVersion7();
        var trace = HarnessAppTraceBuilder.FromCollectedStream(
            new HarnessCollectedAppStream
            {
                SessionId = sessionId,
                Mode = "writer",
                FinalContent = "Final paragraph",
                FinalReasoning = "Lead with the consequence.",
                StopReason = "tool_use",
                MessageCount = 4,
                ToolRounds = 1,
                Usage = new HarnessUsage(12, 7),
                Events =
                [
                    new HarnessCollectedAppEvent { Type = "text_delta", Text = "Final " },
                    new HarnessCollectedAppEvent { Type = "reasoning_delta", Text = "Lead " },
                    new HarnessCollectedAppEvent { Type = "tool", ToolName = "query_lore", ToolId = "call_1" },
                    new HarnessCollectedAppEvent { Type = "diagnostic", Category = "tool", Message = "query_lore returned 3 hits", Level = "info" },
                    new HarnessCollectedAppEvent { Type = "done", StopReason = "tool_use", Usage = new HarnessUsage(12, 7) },
                ],
                WriterState = "pendingreview",
            },
            new HarnessCollectedSessionSnapshot
            {
                SessionId = sessionId,
                Name = "Writer Session",
                Messages =
                [
                    new HarnessCollectedSessionMessage
                    {
                        Id = Guid.CreateVersion7(),
                        Role = "assistant",
                        Content = "Final paragraph",
                        CreatedAt = DateTimeOffset.UtcNow,
                    },
                ],
            });

        Assert.Equal(sessionId, trace.SessionId);
        Assert.Equal("writer", trace.Mode);
        Assert.Equal("Final paragraph", trace.FinalContent);
        Assert.Equal("tool_use", trace.StopReason);
        Assert.Equal(["Final "], trace.TextDeltas);
        Assert.Equal(["Lead "], trace.ReasoningDeltas);
        Assert.Single(trace.Tools);
        Assert.Equal("query_lore", trace.Tools[0].Name);
        Assert.Single(trace.Diagnostics);
        Assert.Single(trace.PersistedMessages);
        Assert.Equal("pendingreview", trace.WriterState);
    }

    [Fact]
    public async Task ArtifactCollector_CapturesFilesAndMissingEntries()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "plan"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "plan", "outline.md"), "Outline content");

        var trace = await HarnessArtifactCollector.CaptureAsync(
            _tempDir,
            ["plan/outline.md", "drafts/ch-1.md"]);

        var outline = Assert.Single(trace.Snapshots, snapshot => snapshot.RelativePath == "plan/outline.md");
        Assert.True(outline.Exists);
        Assert.Equal("Outline content", outline.Content);

        var missing = Assert.Single(trace.Snapshots, snapshot => snapshot.RelativePath == "drafts/ch-1.md");
        Assert.False(missing.Exists);
    }

    [Fact]
    public void Evaluator_ReportsStructuralDivergenceFindings()
    {
        var evaluator = new HarnessEvaluator();
        var run = new DualSidedHarnessRun
        {
            ScenarioName = "mismatch-case",
            ProviderTraces =
            [
                new HarnessProviderTrace
                {
                    TraceId = "provider-1",
                    ScenarioName = "mismatch-case",
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Method = "POST",
                    Path = "/v1/chat/completions",
                    RawRequestBody = "{\"messages\":[{\"role\":\"user\",\"content\":\"Use the lore section.\"}],\"tools\":[{\"name\":\"query_lore\"}]}",
                    Model = "harness-basic",
                    Stream = true,
                    MessageCount = 1,
                    ToolCount = 1,
                    Messages = [new HarnessMessageSummary("user", "Use the lore section.")],
                    HasAuthorizationHeader = true,
                    ContentType = "application/json",
                    UserAgent = "test",
                    ResponseMode = HarnessResponseMode.ScriptedStream,
                    StatusCode = 200,
                    EmittedFrames = [],
                    TextDeltas = ["Provider text"],
                    EmittedToolCalls = [new HarnessToolCallTrace("call_1", "query_lore", "{\"query\":\"sun vault\"}", 0)],
                    Usage = new HarnessUsage(10, 6),
                    FinalContent = "Provider text",
                    FinishReason = "tool_calls",
                    Fault = null,
                    Error = null,
                    DurationMs = 10,
                },
            ],
            AppTrace = new HarnessAppTrace
            {
                SessionId = Guid.CreateVersion7(),
                Mode = "writer",
                FinalContent = "Different app text",
                StopReason = "end_turn",
                MessageCount = 2,
                ToolRounds = 0,
                Usage = new HarnessUsage(10, 6),
                PersistedMessages =
                [
                    new HarnessPersistedMessageTrace
                    {
                        Id = Guid.CreateVersion7(),
                        Role = "assistant",
                        Content = "Some other persisted text",
                        CreatedAt = DateTimeOffset.UtcNow,
                    },
                ],
            },
        };

        var result = evaluator.Evaluate(
            run,
            [
                new ExpectedProviderRequestSectionAssertion("lore section"),
                new ExpectedToolMirroredAcrossBoundaryAssertion(),
                new ExpectedFinalContentConsistencyAssertion(),
                new ExpectedPersistedAssistantContentAssertion(),
                new ExpectedStopReasonConsistencyAssertion(),
            ]);

        Assert.Equal(HarnessEvaluationStatus.Failed, result.Status);
        Assert.Contains(result.Findings, finding => finding.Code == "provider_tool_not_mirrored");
        Assert.Contains(result.Findings, finding => finding.Code == "final_content_mismatch");
        Assert.Contains(result.Findings, finding => finding.Code == "assistant_content_not_persisted");
        Assert.Contains(result.Findings, finding => finding.Code == "stop_reason_mismatch");
    }

    [Fact]
    public void Evaluator_ReportsMissingWorkerRoles()
    {
        var evaluator = new HarnessEvaluator();
        var run = new DualSidedHarnessRun
        {
            ScenarioName = "worker-role-check",
            ProviderTraces =
            [
                new HarnessProviderTrace
                {
                    TraceId = "provider-1",
                    ScenarioName = "worker-role-check",
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Method = "POST",
                    Path = "/v1/chat/completions",
                    RawRequestBody = "{}",
                    Model = "forge-planner-model",
                    Stream = false,
                    MessageCount = 1,
                    ToolCount = 0,
                    Messages = [new HarnessMessageSummary("user", "Plan this story.")],
                    HasAuthorizationHeader = true,
                    ContentType = "application/json",
                    UserAgent = "test",
                    ResponseMode = HarnessResponseMode.ScriptedComplete,
                    StatusCode = 200,
                    EmittedFrames = [],
                    WorkerTrace = new HarnessWorkerTrace
                    {
                        Role = HarnessWorkerRole.Planner.ToString(),
                        Strategy = "prototype-role-worker",
                        StartedAt = DateTimeOffset.UtcNow,
                        CompletedAt = DateTimeOffset.UtcNow,
                        RequestSummary = "planner request",
                        ProposedToolCallCount = 1,
                    },
                    FinishReason = "tool_calls",
                    Fault = null,
                    Error = null,
                    DurationMs = 5,
                },
            ],
        };

        var result = evaluator.Evaluate(
            run,
            [
                new ExpectedWorkerRoleObservedAssertion(
                    HarnessWorkerRole.Planner,
                    HarnessWorkerRole.Writer),
            ]);

        Assert.Equal(HarnessEvaluationStatus.Failed, result.Status);
        Assert.Contains(result.Findings, finding => finding.Code == "worker_role_not_observed");
    }

    [Fact]
    public async Task Evaluator_PassesForAlignedGeneralAndArtifactEvidence()
    {
        Directory.CreateDirectory(Path.Combine(_tempDir, "plan"));
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "plan", "outline.md"), "Aligned outline");
        var artifacts = await HarnessArtifactCollector.CaptureAsync(_tempDir, ["plan/outline.md"]);

        var evaluator = new HarnessEvaluator();
        var run = new DualSidedHarnessRun
        {
            ScenarioName = "aligned-case",
            ProviderTraces =
            [
                new HarnessProviderTrace
                {
                    TraceId = "provider-1",
                    ScenarioName = "aligned-case",
                    StartedAt = DateTimeOffset.UtcNow,
                    CompletedAt = DateTimeOffset.UtcNow,
                    Method = "POST",
                    Path = "/v1/chat/completions",
                    RawRequestBody = "{\"messages\":[{\"role\":\"user\",\"content\":\"Include lore and outline.\"}]}",
                    Model = "harness-basic",
                    Stream = false,
                    MessageCount = 1,
                    ToolCount = 0,
                    Messages = [new HarnessMessageSummary("user", "Include lore and outline.")],
                    HasAuthorizationHeader = true,
                    ContentType = "application/json",
                    UserAgent = "test",
                    ResponseMode = HarnessResponseMode.ScriptedComplete,
                    StatusCode = 200,
                    EmittedFrames = [],
                    TextDeltas = ["Aligned response"],
                    Usage = new HarnessUsage(9, 4),
                    FinalContent = "Aligned response",
                    FinishReason = "stop",
                    Fault = null,
                    Error = null,
                    DurationMs = 8,
                },
            ],
            AppTrace = new HarnessAppTrace
            {
                SessionId = Guid.CreateVersion7(),
                Mode = "guide",
                FinalContent = "Aligned response",
                StopReason = "end_turn",
                MessageCount = 2,
                ToolRounds = 0,
                Usage = new HarnessUsage(9, 4),
                PersistedMessages =
                [
                    new HarnessPersistedMessageTrace
                    {
                        Id = Guid.CreateVersion7(),
                        Role = "assistant",
                        Content = "Aligned response",
                        CreatedAt = DateTimeOffset.UtcNow,
                    },
                ],
            },
            ArtifactTrace = artifacts,
        };

        var result = evaluator.Evaluate(
            run,
            [
                new ExpectedProviderRequestSectionAssertion("outline"),
                new ExpectedFinalContentConsistencyAssertion(),
                new ExpectedPersistedAssistantContentAssertion(),
                new ExpectedArtifactPresenceAssertion("plan/outline.md"),
                new ExpectedStopReasonConsistencyAssertion(),
            ]);

        Assert.Equal(HarnessEvaluationStatus.Passed, result.Status);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public async Task RunReportWriter_PersistsInteractiveJsonAndMarkdownWithStableSchemaFields()
    {
        var artifactStore = new HarnessRunArtifactStore("report-workflow", _tempDir);
        var providerTrace = new HarnessProviderTrace
        {
            TraceId = "provider-1",
            ScenarioName = "interactive/guide",
            StartedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
            Method = "POST",
            Path = "/v1/chat/completions",
            RawRequestBody = "{\"messages\":[{\"role\":\"user\",\"content\":\"Where should I start?\"}]}",
            Model = "orchestrator-model",
            Stream = true,
            MessageCount = 1,
            ToolCount = 0,
            Messages = [new HarnessMessageSummary("user", "Where should I start?")],
            HasAuthorizationHeader = true,
            ContentType = "application/json",
            UserAgent = "test",
            ResponseMode = HarnessResponseMode.ScriptedStream,
            StatusCode = 200,
            EmittedFrames = [],
            TextDeltas = ["Start with Guide mode."],
            Usage = new HarnessUsage(12, 5),
            FinalContent = "Start with Guide mode.",
            FinishReason = "stop",
            Fault = null,
            Error = null,
            DurationMs = 5,
        };
        artifactStore.PersistProviderTrace(providerTrace);

        var scenarioReport = new HarnessInteractiveScenarioReport
        {
            SessionId = Guid.CreateVersion7(),
            Mode = "guide",
            Run = new DualSidedHarnessRun
            {
                ScenarioName = "interactive/guide",
                ProviderTraces = [providerTrace],
                AppTrace = new HarnessAppTrace
                {
                    SessionId = Guid.CreateVersion7(),
                    Mode = "guide",
                    FinalContent = "Start with Guide mode.",
                    StopReason = "end_turn",
                    MessageCount = 2,
                    ToolRounds = 0,
                    Usage = new HarnessUsage(12, 5),
                },
            },
            UsageSummary = new SessionUsageSummary
            {
                TotalInputTokens = 12,
                TotalOutputTokens = 5,
                TotalRequests = 1,
                ByAgent =
                [
                    new AgentUsageEntry
                    {
                        AgentName = "orchestrator",
                        InputTokens = 12,
                        OutputTokens = 5,
                        RequestCount = 1,
                    },
                ],
            },
        };

        var persisted = HarnessRunReportWriter.WriteInteractiveReport(artifactStore, scenarioReport);
        var jsonPath = Path.Combine(
            artifactStore.RunDirectory,
            persisted.JsonReportPath!.Replace('/', Path.DirectorySeparatorChar));
        var markdownPath = Path.Combine(
            artifactStore.RunDirectory,
            persisted.MarkdownReportPath!.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(jsonPath));
        Assert.True(File.Exists(markdownPath));

        using var reportDocument = JsonDocument.Parse(await File.ReadAllTextAsync(jsonPath));
        Assert.Equal(HarnessRunReportWriter.SchemaVersion, reportDocument.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("interactive", reportDocument.RootElement.GetProperty("kind").GetString());
        Assert.Equal("captured", reportDocument.RootElement.GetProperty("status").GetString());
        Assert.Single(reportDocument.RootElement.GetProperty("providerTraceFiles").EnumerateArray());
        Assert.Equal("app/interactive-guide-trace.json", reportDocument.RootElement.GetProperty("appTraceFile").GetString());
        Assert.Equal(1, reportDocument.RootElement.GetProperty("usageSummary").GetProperty("totalRequests").GetInt32());

        var markdown = await File.ReadAllTextAsync(markdownPath);
        Assert.Contains("# Harness Report: interactive/guide", markdown);
        Assert.Contains("Status: `captured`", markdown);
        Assert.Contains("provider/traces/", markdown);
        Assert.Contains("Session usage", markdown);
    }
}
