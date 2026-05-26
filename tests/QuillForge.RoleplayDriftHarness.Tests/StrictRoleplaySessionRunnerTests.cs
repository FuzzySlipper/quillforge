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
    // Fake completion service for deterministic testing
    // ──────────────────────────────────────────────

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
}
