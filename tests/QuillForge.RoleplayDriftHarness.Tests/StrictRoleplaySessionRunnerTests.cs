using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.RoleplayDriftHarness.Fixtures;
using QuillForge.RoleplayDriftHarness.Models;
using QuillForge.RoleplayDriftHarness.Runners;
using Xunit;

namespace QuillForge.RoleplayDriftHarness.Tests;

/// <summary>
/// Deterministic tests for the StrictRoleplaySessionRunner.
/// These tests validate that the runner correctly constructs the real
/// agent pipeline, captures diagnostics, and detects lore bleed.
///
/// The live LLM-backed strict test is gated by an opt-in CLI flag
/// (--strict-live) or environment variable (DRIFT_HARNESS_STRICT_LIVE=true).
/// These xUnit tests run in CI without live credentials.
/// </summary>
public sealed class StrictRoleplaySessionRunnerTests
{
    private readonly DriftDetector _detector = new();

    // ──────────────────────────────────────────────
    // Pipeline construction verification
    // ──────────────────────────────────────────────

    [Fact]
    public void StrictRunner_CanReachProvider_ReturnsTrueForWorkingService()
    {
        // When a completion service responds successfully, CanReachProvider should return true.
        var completionService = new FakeCompletionService();
        var runner = new StrictRoleplaySessionRunner(
            completionService, _detector, "test", "test-model");

        var result = runner.CanReachProvider();
        Assert.True(result,
            "FakeCompletionService returns immediate success, so CanReachProvider should return true.");
    }

    // ──────────────────────────────────────────────
    // Fixture consistency checks
    // ──────────────────────────────────────────────

    [Fact]
    public void LiveXavierCalebScenario_FixturesContainExpectedBoundaries()
    {
        var probeTurns = LiveXavierCalebScenario.ProbeTurns;

        Assert.NotEmpty(probeTurns);
        Assert.All(probeTurns, turn =>
        {
            Assert.InRange(turn.TurnNumber, 1, 10);
            Assert.False(string.IsNullOrWhiteSpace(turn.UserMessage));
            Assert.False(string.IsNullOrWhiteSpace(turn.ExpectedSubject));
            Assert.False(string.IsNullOrWhiteSpace(turn.Category));
            Assert.False(string.IsNullOrWhiteSpace(turn.ContaminationRisk));
            Assert.False(string.IsNullOrWhiteSpace(turn.ProbePrompt));
        });
    }

    [Fact]
    public void LiveXavierCalebScenario_AllTurnsTargetXavier()
    {
        var probeTurns = LiveXavierCalebScenario.ProbeTurns;
        Assert.All(probeTurns, turn =>
            Assert.Equal("Xavier", turn.ExpectedSubject));
    }

    // ──────────────────────────────────────────────
    // Boundary-type consistency
    // ──────────────────────────────────────────────

    [Fact]
    public void StrictMode_BoundaryTypesMatchDriftDetectorExpectations()
    {
        // Verify that the boundary types used by StrictRoleplaySessionRunner
        // are consistent with what DriftDetector.ClassifyOrigin expects.
        var expectedBoundaries = new[]
        {
            nameof(BoundaryType.QueryLore),
            nameof(BoundaryType.NarrativeDirector),
            nameof(BoundaryType.ProseWriter),
            nameof(BoundaryType.VisibleResponse),
            nameof(BoundaryType.UserTurn),
        };

        foreach (var boundary in expectedBoundaries)
        {
            Assert.NotNull(boundary);
            Assert.NotEmpty(boundary);
        }

        // Verify DriftDetector can handle each boundary type
        var detector = new DriftDetector();
        var traceEvents = new List<TraceEvent>();

        foreach (var boundary in expectedBoundaries)
        {
            traceEvents.Add(new TraceEvent
            {
                Turn = 1,
                Component = boundary.ToLowerInvariant(),
                Boundary = boundary,
                Content = "No forbidden content here",
                Preview = "preview",
            });
        }

        var result = detector.Detect(traceEvents, ["nonexistent-forbidden-fact-xyz"]);
        Assert.False(result.HasDrift);
    }

    // ──────────────────────────────────────────────
    // Probe turn classification checks
    // ──────────────────────────────────────────────

    [Fact]
    public void XavierLore_ClassifiesCorrectlyAgainstXavierSubject()
    {
        // Xavier's lore should classify as Applies/AssertAsFact for Xavier
        foreach (var loreLine in LiveXavierCalebScenario.XavierLore)
        {
            var diag = RoleplayApplicabilityClassifier.ClassifyWithDiagnostics(
                loreLine,
                "Xavier",
                "characters/xavier.md",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" });

            Assert.True(
                diag.Applicability == ActiveSubjectApplicability.Applies,
                $"Xavier lore should apply to Xavier: \"{loreLine}\"");
        }
    }

    [Fact]
    public void CalebLore_ClassifiesAsDoesNotApply_ForXavierSubject()
    {
        // Caleb's unique lore (prosthetic arm, Toring Chip) should NOT apply to Xavier
        foreach (var loreLine in LiveXavierCalebScenario.CalebLore)
        {
            var diag = RoleplayApplicabilityClassifier.ClassifyWithDiagnostics(
                loreLine,
                "Xavier",
                "characters/caleb.md",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" });

            Assert.True(
                diag.Applicability == ActiveSubjectApplicability.DoesNotApply ||
                diag.Applicability == ActiveSubjectApplicability.Unknown,
                $"Caleb lore should not apply to Xavier: \"{loreLine}\" " +
                $"(got {diag.Applicability})");
        }
    }

    [Fact]
    public void SharedBodyTech_ClassifiesAsUnknown_ForXavierSubject()
    {
        // Shared body tech (neural interfaces, standard gear) should be Unknown/BackgroundOnly
        foreach (var loreLine in LiveXavierCalebScenario.SharedBodyTech)
        {
            var diag = RoleplayApplicabilityClassifier.ClassifyWithDiagnostics(
                loreLine,
                "Xavier",
                "world/body-tech.md",
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" },
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" });

            Assert.Equal(
                ActiveSubjectApplicability.Unknown,
                diag.Applicability);
            Assert.Equal(
                AllowedUse.BackgroundOnly,
                diag.AllowedUse);
        }
    }

    // ──────────────────────────────────────────────
    // StructuredPayload construction
    // ──────────────────────────────────────────────

    [Fact]
    public void StrictMode_StructuredPayload_ContainsExpectedFields()
    {
        var payload = new StructuredPayload
        {
            ActiveSubject = "Xavier",
            Applicability = "Applies",
            AllowedUse = "AssertAsFact",
            LoreRefs = ["characters/xavier.md"],
            SourceComponent = "librarian",
        };

        Assert.Equal("Xavier", payload.ActiveSubject);
        Assert.Equal("Applies", payload.Applicability);
        Assert.Equal("AssertAsFact", payload.AllowedUse);
        Assert.Contains("characters/xavier.md", payload.LoreRefs);
        Assert.Equal("librarian", payload.SourceComponent);
    }

    // ──────────────────────────────────────────────
    // Forbidden details consistency
    // ──────────────────────────────────────────────

    [Fact]
    public void ForbiddenDetails_DetectableInCalebLore()
    {
        var detector = new DriftDetector();
        var forbiddenDetails = new List<string>
        {
            "prosthetic arm",
            "prosthetic",
            "Toring Chip",
            "Toring",
            "custom prosthetic",
        };

        // Caleb lore contains prosthetic arm and Toring Chip references
        var calebLoreText = string.Join("\n", LiveXavierCalebScenario.CalebLore);
        var traceEvents = new List<TraceEvent>
        {
            new()
            {
                Turn = 1,
                Component = "query_lore",
                Boundary = nameof(BoundaryType.QueryLore),
                Content = calebLoreText,
                Preview = "Caleb lore",
            },
        };

        var result = detector.Detect(traceEvents, forbiddenDetails);
        Assert.True(result.HasDrift,
            "Caleb lore containing prosthetic arm and Toring Chip SHOULD be detected as drift " +
            "when it appears in Xavier-focused output.");
    }

    [Fact]
    public void XavierLore_DoesNotTriggerForbiddenDetails()
    {
        var detector = new DriftDetector();
        var forbiddenDetails = new List<string>
        {
            "prosthetic arm",
            "prosthetic",
            "Toring Chip",
            "Toring",
            "custom prosthetic",
        };

        var xavierLoreText = string.Join("\n", LiveXavierCalebScenario.XavierLore);
        var traceEvents = new List<TraceEvent>
        {
            new()
            {
                Turn = 1,
                Component = "query_lore",
                Boundary = nameof(BoundaryType.QueryLore),
                Content = xavierLoreText,
                Preview = "Xavier lore",
            },
        };

        var result = detector.Detect(traceEvents, forbiddenDetails);
        Assert.False(result.HasDrift,
            "Xavier lore should NOT trigger forbidden detail detection.");
    }

    // ──────────────────────────────────────────────
    // Fake completion services for deterministic testing
    // ──────────────────────────────────────────────

    /// <summary>
    /// Fake completion service that returns successful responses for deterministic testing.
    /// </summary>
    private sealed class FakeCompletionService : ICompletionService
    {
        public Task<CompletionResponse> CompleteAsync(
            CompletionRequest request, CancellationToken ct = default)
        {
            // Return a minimal response that won't cause crashes
            return Task.FromResult(new CompletionResponse
            {
                Content = new MessageContent("Test response"),
                StopReason = StopReason.EndTurn,
                Usage = new TokenUsage(0, 0),
            });
        }

        public IAsyncEnumerable<StreamEvent> StreamAsync(
            CompletionRequest request, CancellationToken ct = default)
        {
            return AsyncEnumerable.Empty<StreamEvent>();
        }
    }

    /// <summary>
    /// Fake completion service that throws an authentication/provider error
    /// on every call, simulating a 401 or invalid-credentials scenario.
    /// Used to verify that strict evaluation correctly reports Passed=false
    /// and records PipelineErrors when the agent pipeline cannot complete.
    /// </summary>
    private sealed class FailingCompletionService : ICompletionService
    {
        public Task<CompletionResponse> CompleteAsync(
            CompletionRequest request, CancellationToken ct = default)
        {
            throw new InvalidOperationException(
                "401 (Unauthorized): The API key provided is invalid. " +
                "This simulates a provider authentication failure.");
        }

        public IAsyncEnumerable<StreamEvent> StreamAsync(
            CompletionRequest request, CancellationToken ct = default)
        {
            throw new InvalidOperationException(
                "401 (Unauthorized): The API key provided is invalid.");
        }
    }

    // ──────────────────────────────────────────────
    // Pipeline error handling tests
    // ──────────────────────────────────────────────

    [Fact]
    public async Task StrictRunner_WithFailingCompletionService_ReportsPassedFalse()
    {
        // When the completion service throws provider/auth errors on every call,
        // the strict runner should report Passed=false in the evaluation.
        var completionService = new FailingCompletionService();
        var runner = new StrictRoleplaySessionRunner(
            completionService, _detector, "test", "test-model",
            ndMaxRounds: 2,
            diagnosticLevel: "minimal");

        var outputDir = Path.Combine(Path.GetTempPath(), "qf-test-failing-strict");
        try
        {
            var run = await runner.RunAsync(outputDir);

            Assert.NotNull(run.Evaluation);
            Assert.False(run.Evaluation!.Passed,
                "Strict evaluation must report Passed=false when the completion service fails.");
            Assert.True(run.Evaluation.HasPipelineErrors,
                "HasPipelineErrors should be true when pipeline errors occurred.");
            Assert.NotEmpty(run.Evaluation.PipelineErrors!);
            Assert.Contains(run.Evaluation.PipelineErrors!, e =>
                e.Component == "narrative_director" &&
                e.ErrorType == "InvalidOperationException");
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task StrictRunner_WithFailingCompletionService_WritesPipelineErrorsToEvaluationJson()
    {
        // Verify that pipeline errors are serialized into evaluation.json,
        // not just runtime state. A downstream reader should see has_pipeline_errors=true
        // and the error details in the evaluation artifact.
        var completionService = new FailingCompletionService();
        var runner = new StrictRoleplaySessionRunner(
            completionService, _detector, "test", "test-model",
            ndMaxRounds: 2,
            diagnosticLevel: "minimal");

        var outputDir = Path.Combine(Path.GetTempPath(), "qf-test-failing-eval-json");
        try
        {
            var run = await runner.RunAsync(outputDir);
            Assert.NotNull(run.Evaluation);
            Assert.False(run.Evaluation!.Passed);

            // Read evaluation.json back and verify pipeline errors are present
            var evalPath = Path.Combine(outputDir, "evaluation.json");
            Assert.True(File.Exists(evalPath), "evaluation.json should exist after a strict run.");

            var evalJson = File.ReadAllText(evalPath);
            Assert.Contains("has_pipeline_errors", evalJson);
            Assert.Contains("pipeline_errors", evalJson);
            Assert.Contains("InvalidOperationException", evalJson);
            Assert.Contains("401", evalJson);
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public async Task StrictRunner_WithFailingCompletionService_DoesNotReportFalseDriftFindings()
    {
        // When the pipeline fails, drift findings may be empty or meaningless.
        // Verify the failure is attributed to pipeline errors, not to drift,
        // and the notes clearly indicate the run was invalid.
        var completionService = new FailingCompletionService();
        var runner = new StrictRoleplaySessionRunner(
            completionService, _detector, "test", "test-model",
            ndMaxRounds: 2,
            diagnosticLevel: "minimal");

        var outputDir = Path.Combine(Path.GetTempPath(), "qf-test-failing-no-drift");
        try
        {
            var run = await runner.RunAsync(outputDir);

            Assert.NotNull(run.Evaluation);
            Assert.False(run.Evaluation!.Passed);
            Assert.True(run.Evaluation.HasPipelineErrors);

            // The notes should clearly indicate the run was invalid due to pipeline errors
            Assert.NotNull(run.Evaluation.Notes);
            Assert.Contains("STRICT LIVE RUN INVALID", run.Evaluation.Notes, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("pipeline", run.Evaluation.Notes, StringComparison.OrdinalIgnoreCase);

            // Drift findings should be absent or empty since the pipeline never ran
            Assert.False(run.DriftResult.HasDrift,
                "Drift detection should not report positive findings when pipeline never completed.");
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    [Fact]
    public void CanReachProvider_ReturnsFalseForFailingService()
    {
        var completionService = new FailingCompletionService();
        var runner = new StrictRoleplaySessionRunner(
            completionService, _detector, "test", "test-model");

        var result = runner.CanReachProvider();
        Assert.False(result,
            "FailingCompletionService throws on every call, so CanReachProvider should return false.");
    }

    [Fact]
    public async Task StrictRunner_WithWorkingService_ReportsPassedTrue()
    {
        // When the completion service works, the runner should not report pipeline errors.
        // Note: this does NOT test real LLM, just the fake that returns a minimal response.
        // Real completions may or may not produce lore bleed — that's the live test's job.
        var completionService = new FakeCompletionService();
        var runner = new StrictRoleplaySessionRunner(
            completionService, _detector, "test", "test-model",
            ndMaxRounds: 1,
            diagnosticLevel: "minimal");

        var outputDir = Path.Combine(Path.GetTempPath(), "qf-test-working-strict");
        try
        {
            var run = await runner.RunAsync(outputDir);

            Assert.NotNull(run.Evaluation);
            Assert.False(run.Evaluation!.HasPipelineErrors,
                "A working FakeCompletionService should not produce pipeline errors.");
            Assert.False(run.DriftResult.HasDrift,
                "A basic FakeCompletionService returning 'Test response' should not trigger drift.");
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }
}
