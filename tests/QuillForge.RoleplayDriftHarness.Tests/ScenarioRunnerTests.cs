using QuillForge.RoleplayDriftHarness.Fixtures;
using QuillForge.RoleplayDriftHarness.Models;
using QuillForge.RoleplayDriftHarness.Runners;
using Xunit;

namespace QuillForge.RoleplayDriftHarness.Tests;

public sealed class ScenarioRunnerTests
{
    private readonly DriftDetector _detector = new();
    private readonly ScenarioRunner _runner;

    public ScenarioRunnerTests()
    {
        _runner = new ScenarioRunner(_detector);
    }

    [Fact]
    public void Run_CleanXavierScenario_PassesWithoutDrift()
    {
        var scenario = XavierCalebScenario.CreateClean();
        var run = _runner.Run(scenario);

        Assert.NotNull(run);
        Assert.False(run.DriftResult.HasDrift, "Clean scenario should have no drift");
        Assert.Empty(run.DriftResult.Findings);
        Assert.True(run.Evaluation?.Passed);
    }

    [Fact]
    public void Run_CleanScenario_ProducesAllExpectedArtifacts()
    {
        var scenario = XavierCalebScenario.CreateClean();
        var run = _runner.Run(scenario);

        // Should have events for each boundary
        Assert.NotNull(run.TraceEvents);
        Assert.NotEmpty(run.TraceEvents);

        // All boundaries should be represented
        var boundaries = run.TraceEvents.Select(e => e.Boundary).Distinct().ToList();
        Assert.Contains(nameof(BoundaryType.UserTurn), boundaries);
        Assert.Contains(nameof(BoundaryType.QueryLore), boundaries);
        Assert.Contains(nameof(BoundaryType.NarrativeDirector), boundaries);
        Assert.Contains(nameof(BoundaryType.ProseWriter), boundaries);
        Assert.Contains(nameof(BoundaryType.VisibleResponse), boundaries);

        // Run metadata
        Assert.Equal("xavier-caleb-clean", run.ScenarioName);
        Assert.Equal("Xavier", run.ActiveCharacter);
        Assert.Equal("Caleb", run.OffCharacter);
        Assert.NotEmpty(run.RunId);
    }

    [Fact]
    public void Run_CleanScenario_ProducesStructuredPayloadsOnKnowledgeBoundaries()
    {
        var scenario = XavierCalebScenario.CreateClean();
        var run = _runner.Run(scenario);

        var payloads = run.TraceEvents
            .Where(e => e.StructuredPayload is not null)
            .ToList();

        Assert.NotEmpty(payloads);

        // QueryLore payloads should reference source files
        var lorePayloads = payloads.Where(p => p.Boundary == nameof(BoundaryType.QueryLore)).ToList();
        foreach (var lp in lorePayloads)
        {
            Assert.NotNull(lp.StructuredPayload!.LoreRefs);
            Assert.NotEmpty(lp.StructuredPayload.LoreRefs);
        }
    }

    [Fact]
    public void Run_ContaminatedAtQueryLore_DetectsDriftWithRetrievalOrigin()
    {
        var scenario = XavierCalebScenario.CreateContaminatedAtBoundary(nameof(BoundaryType.QueryLore));
        var run = _runner.Run(scenario);

        Assert.True(run.DriftResult.HasDrift, "Contaminated scenario should have drift");
        Assert.NotEmpty(run.DriftResult.Findings);

        // Should find prosthetic arm in query_lore boundary
        var armFinding = run.DriftResult.Findings
            .FirstOrDefault(f => f.ForbiddenFact.Contains("prosthetic", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(armFinding);
        Assert.Equal(nameof(BoundaryType.QueryLore), armFinding.FirstAppearanceBoundary);
        Assert.Equal("retrieval", armFinding.LikelyOrigin);

        // Should find Toring Chip in query_lore boundary
        var chipFinding = run.DriftResult.Findings
            .FirstOrDefault(f => f.ForbiddenFact.Contains("Toring", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(chipFinding);
        Assert.Equal("retrieval", chipFinding.LikelyOrigin);
    }

    [Fact]
    public void Run_ContaminatedAtNarrativeDirector_DetectsDriftWithDirectorOrigin()
    {
        var scenario = XavierCalebScenario.CreateContaminatedAtBoundary(nameof(BoundaryType.NarrativeDirector));
        var run = _runner.Run(scenario);

        Assert.True(run.DriftResult.HasDrift);
        var armFinding = run.DriftResult.Findings
            .FirstOrDefault(f => f.ForbiddenFact.Contains("prosthetic", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(armFinding);
        Assert.Equal("director_synthesis", armFinding.LikelyOrigin);
    }

    [Fact]
    public void Run_ContaminatedAtProseWriter_DetectsDriftWithProseMisuseOrigin()
    {
        var scenario = XavierCalebScenario.CreateContaminatedAtBoundary(nameof(BoundaryType.ProseWriter));
        var run = _runner.Run(scenario);

        Assert.True(run.DriftResult.HasDrift);
        var armFinding = run.DriftResult.Findings
            .FirstOrDefault(f => f.ForbiddenFact.Contains("prosthetic", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(armFinding);
        Assert.Equal("prose_misuse", armFinding.LikelyOrigin);
    }

    [Fact]
    public void Run_ContaminatedAtVisibleResponse_DetectsDriftWithVisibleResponseOrigin()
    {
        var scenario = XavierCalebScenario.CreateContaminatedAtBoundary(nameof(BoundaryType.VisibleResponse));
        var run = _runner.Run(scenario);

        Assert.True(run.DriftResult.HasDrift);
        Assert.NotEmpty(run.DriftResult.Findings);
    }

    [Fact]
    public void Run_MultipleTurns_ProducesCorrectEventSequence()
    {
        var scenario = XavierCalebScenario.CreateClean();
        var run = _runner.Run(scenario);

        // Two turns, each with user_turn + 4 boundaries = 10 events
        Assert.Equal(10, run.TraceEvents.Count);

        // First event is user_turn for turn 1
        Assert.Equal(1, run.TraceEvents[0].Turn);
        Assert.Equal(nameof(BoundaryType.UserTurn), run.TraceEvents[0].Boundary);

        // Last event is visible_response for turn 2
        Assert.Equal(2, run.TraceEvents[^1].Turn);
        Assert.Equal(nameof(BoundaryType.VisibleResponse), run.TraceEvents[^1].Boundary);
    }

    [Fact]
    public void Run_SharedBodyTechEvidence_IsRecordedAsSharedWorld()
    {
        var scenario = XavierCalebScenario.CreateClean();
        var run = _runner.Run(scenario);

        // The shared body-tech (neural interface) should appear with
        // applicability = "Unknown" and allowed_use = "BackgroundOnly"
        var sharedPayloads = run.TraceEvents
            .Where(e => e.StructuredPayload?.Applicability == "Unknown")
            .ToList();

        Assert.NotEmpty(sharedPayloads);
        foreach (var sp in sharedPayloads)
        {
            Assert.Equal("BackgroundOnly", sp.StructuredPayload!.AllowedUse);
        }
    }

    [Fact]
    public void Run_ReportWriter_CreatesAllFiles()
    {
        var scenario = XavierCalebScenario.CreateClean();
        var run = _runner.Run(scenario);

        var outputDir = Path.Combine(Path.GetTempPath(), $"qf-drift-test-{Guid.NewGuid()}");
        try
        {
            var writer = new DriftReportWriter();
            writer.WriteAll(outputDir, run);

            Assert.True(File.Exists(Path.Combine(outputDir, "run.json")));
            Assert.True(File.Exists(Path.Combine(outputDir, "trace.ndjson")));
            Assert.True(File.Exists(Path.Combine(outputDir, "evaluation.json")));
            Assert.True(File.Exists(Path.Combine(outputDir, "summary.md")));
            Assert.True(File.Exists(Path.Combine(outputDir, "lore-results.json")));

            // Verify run.json contains expected structure
            var runJson = File.ReadAllText(Path.Combine(outputDir, "run.json"));
            Assert.Contains("run_id", runJson);
            Assert.Contains("drift_result", runJson);
            Assert.Contains("trace_event_count", runJson);

            // Verify trace.ndjson has one event per line
            var traceLines = File.ReadAllLines(Path.Combine(outputDir, "trace.ndjson"));
            Assert.Equal(run.TraceEvents.Count, traceLines.Length);
        }
        finally
        {
            try { Directory.Delete(outputDir, recursive: true); } catch { }
        }
    }
}
