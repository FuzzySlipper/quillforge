using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.RoleplayDriftHarness.Fixtures;
using QuillForge.RoleplayDriftHarness.Models;
using QuillForge.RoleplayDriftHarness.Runners;
using Xunit;

namespace QuillForge.RoleplayDriftHarness.Tests;

/// <summary>
/// Deterministic synthetic regression tests for task #1641: Roleplay Lore Bleed Protocol Fix.
///
/// These tests verify that character-owned details from non-active characters (Caleb)
/// do NOT become active-character facts (Xavier), and that cross-character queries
/// still allow explicit comparisons.
///
/// Uses the #1646/#1661 drift harness/evidence surface.
/// </summary>
public sealed class XavierCalebLoreBleedRegressionTests
{
    private readonly DriftDetector _detector = new();
    private readonly ScenarioRunner _runner;

    public XavierCalebLoreBleedRegressionTests()
    {
        _runner = new ScenarioRunner(_detector);
    }

    // ──────────────────────────────────────────────
    // Clean scenario baselines
    // ──────────────────────────────────────────────

    [Fact]
    public void CleanXavierScenario_Passes_NoLoreBleed()
    {
        // Active: Xavier. Forbidden: Caleb's prosthetic arm + Toring Chip.
        // A clean scenario must detect zero drift.
        var scenario = XavierCalebScenario.CreateClean();
        var run = _runner.Run(scenario);

        Assert.False(run.DriftResult.HasDrift,
            "Clean Xavier scenario must have NO drift. Any 'prosthetic arm' or 'Toring Chip' " +
            "detected indicates Caleb lore leaked into Xavier's narrative.");
        Assert.Empty(run.DriftResult.Findings);
        Assert.True(run.Evaluation?.Passed);
    }

    [Fact]
    public void CleanXavierScenario_SharedBodyTech_IsBackgroundOnly()
    {
        // Shared body-tech (neural interface, hunter gear) should be classified
        // as Unknown/BackgroundOnly — NOT as AssertAsFact for Xavier.
        var scenario = XavierCalebScenario.CreateClean();
        var run = _runner.Run(scenario);

        var sharedPayloads = run.TraceEvents
            .Where(e => e.StructuredPayload?.Applicability == "Unknown")
            .ToList();

        Assert.NotEmpty(sharedPayloads);
        foreach (var sp in sharedPayloads)
        {
            Assert.Equal("BackgroundOnly", sp.StructuredPayload!.AllowedUse);
        }
    }

    // ──────────────────────────────────────────────
    // Contamination detection at each boundary
    // ──────────────────────────────────────────────

    [Fact]
    public void ContaminatedAtQueryLore_DetectedWithRetrievalOrigin()
    {
        var scenario = XavierCalebScenario.CreateContaminatedAtBoundary(nameof(BoundaryType.QueryLore));
        var run = _runner.Run(scenario);

        Assert.True(run.DriftResult.HasDrift);

        var armFinding = run.DriftResult.Findings
            .FirstOrDefault(f => f.ForbiddenFact.Contains("prosthetic", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(armFinding);
        Assert.Equal(nameof(BoundaryType.QueryLore), armFinding.FirstAppearanceBoundary);
        Assert.Equal("retrieval", armFinding.LikelyOrigin);

        var chipFinding = run.DriftResult.Findings
            .FirstOrDefault(f => f.ForbiddenFact.Contains("Toring", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(chipFinding);
        Assert.Equal("retrieval", chipFinding.LikelyOrigin);
    }

    [Fact]
    public void ContaminatedAtNarrativeDirector_DetectedWithDirectorOrigin()
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
    public void ContaminatedAtProseWriter_DetectedWithProseMisuseOrigin()
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
    public void ContaminatedAtVisibleResponse_DetectedWithResponseOrigin()
    {
        var scenario = XavierCalebScenario.CreateContaminatedAtBoundary(nameof(BoundaryType.VisibleResponse));
        var run = _runner.Run(scenario);

        Assert.True(run.DriftResult.HasDrift);
        Assert.NotEmpty(run.DriftResult.Findings);
    }

    // ──────────────────────────────────────────────
    // Cross-character query scenarios
    // ──────────────────────────────────────────────

    [Fact]
    public void CalebExplicitlyQueried_AllowsCalebDetails()
    {
        // When Caleb IS the active subject (queried explicitly), his personal
        // details (prosthetic arm, Toring Chip) are legitimate AssertAsFact facts.
        // This scenario simulates a turn where the query asks about Caleb directly.
        var scenario = CreateCalebActiveScenario();
        var run = _runner.Run(scenario);

        // Caleb's own details should not be flagged as drift when he's the subject
        Assert.False(run.DriftResult.HasDrift,
            "When Caleb is the active character, his prosthetic arm and Toring Chip " +
            "are legitimate character-specific facts.");
        Assert.Empty(run.DriftResult.Findings);
    }

    [Fact]
    public void CrossCharacterQuery_XavierVsCaleb_AllowsBothDetails()
    {
        // When the query explicitly compares Xavier and Caleb (cross-character),
        // both characters' details should be available as evidence.
        var scenario = CreateCrossCharacterScenario();
        var run = _runner.Run(scenario);

        // The cross-character query should not produce drift — both sets of facts
        // are legitimate for a comparison query.
        Assert.False(run.DriftResult.HasDrift,
            "Cross-character queries (Xavier vs Caleb) should allow both character details.");
        Assert.Empty(run.DriftResult.Findings);
    }

    // ──────────────────────────────────────────────
    // Classification diagnostics regression
    // ──────────────────────────────────────────────

    [Fact]
    public void ClassifierDiagnostics_RecordsRulesFired()
    {
        // Verify that the diagnostic provenance path works: ClassifyWithDiagnostics
        // should record which heuristic rules fired for a given classification.
        var passage = "Xavier is a Deepspace Hunter with silver-streaked black hair.";
        var diagnostic = QuillForge.Core.Services.RoleplayApplicabilityClassifier
            .ClassifyWithDiagnostics(passage, "Xavier", "characters/xavier.md");

        Assert.NotNull(diagnostic);
        Assert.Equal(QuillForge.Core.Models.ActiveSubjectApplicability.Applies, diagnostic.Applicability);
        Assert.Equal(QuillForge.Core.Models.AllowedUse.AssertAsFact, diagnostic.AllowedUse);
        Assert.NotEmpty(diagnostic.RulesFired);
        Assert.Contains(diagnostic.RulesFired, r => r.Contains("source-file-matches-active-subject"));
        Assert.Equal("characters/xavier.md", diagnostic.SourcePath);
    }

    [Fact]
    public void ClassifierDiagnostics_OffCharacterRecordsRule()
    {
        var passage = "Caleb has a custom prosthetic arm with combat functionality.";
        var offChars = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Caleb" };

        var diagnostic = RoleplayApplicabilityClassifier.ClassifyWithDiagnostics(passage, "Xavier", "characters/caleb.md", offChars);

        Assert.NotNull(diagnostic);
        Assert.Equal(ActiveSubjectApplicability.DoesNotApply, diagnostic.Applicability);
        Assert.Equal(AllowedUse.OffSubjectEvidence, diagnostic.AllowedUse);
        Assert.NotEmpty(diagnostic.RulesFired);
        Assert.Contains(diagnostic.RulesFired, r => r.Contains("source-file-matches-off-character"));
    }

    [Fact]
    public void ClassifierDiagnostics_SharedWorldRecordsRule()
    {
        var passage = "Standard Division neural interfaces are common among all hunter personnel.";
        var diagnostic = QuillForge.Core.Services.RoleplayApplicabilityClassifier
            .ClassifyWithDiagnostics(passage, "Xavier", "world/body-tech.md");

        Assert.NotNull(diagnostic);
        Assert.Equal(QuillForge.Core.Models.ActiveSubjectApplicability.Unknown, diagnostic.Applicability);
        Assert.Equal(QuillForge.Core.Models.AllowedUse.BackgroundOnly, diagnostic.AllowedUse);
        Assert.NotEmpty(diagnostic.RulesFired);
        Assert.Contains(diagnostic.RulesFired, r => r.Contains("shared-world-source"));
    }

    // ──────────────────────────────────────────────
    // Scenario builders
    // ──────────────────────────────────────────────

    /// <summary>
    /// Scenario where Caleb is the active character. His prosthetic arm and Toring Chip
    /// are legitimate character facts, not forbidden details.
    /// </summary>
    private static RoleplayScenario CreateCalebActiveScenario()
    {
        return new RoleplayScenario
        {
            Name = "caleb-active-scenario",
            ActiveCharacter = "Caleb",
            OffCharacter = "Xavier",
            ForbiddenDetails = [],
            Turns =
            [
                new ScriptedTurn
                {
                    TurnNumber = 1,
                    UserMessage = "Describe Caleb's augmentations.",
                    Boundaries =
                    [
                        new ScriptedBoundaryOutput
                        {
                            Boundary = nameof(BoundaryType.QueryLore),
                            Component = "query_lore",
                            Content = "Caleb has a custom prosthetic arm with advanced combat functionality. " +
                                      "His Toring Chip interfaces directly with the Division tactical network, " +
                                      "giving him enhanced data-processing capabilities in the field.",
                            SourceRefs = ["characters/caleb.md"],
                            Payload = new StructuredPayload
                            {
                                ActiveSubject = "Caleb",
                                Applicability = "Applies",
                                AllowedUse = "AssertAsFact",
                                LoreRefs = ["characters/caleb.md"],
                                SourceComponent = "query_lore",
                            },
                        },
                        new ScriptedBoundaryOutput
                        {
                            Boundary = nameof(BoundaryType.NarrativeDirector),
                            Component = "scene_brief",
                            Content = "Describe Caleb's signature augmentations: the prosthetic combat arm and Toring Chip.",
                            Payload = new StructuredPayload
                            {
                                ActiveSubject = "Caleb",
                                Applicability = "Applies",
                                AllowedUse = "AssertAsFact",
                                SourceComponent = "scene_brief",
                            },
                        },
                        new ScriptedBoundaryOutput
                        {
                            Boundary = nameof(BoundaryType.ProseWriter),
                            Component = "direct_scene",
                            Content = "Caleb flexes his prosthetic arm, the combat plating gleaming under the light. " +
                                      "His Toring Chip pulses at his temple as it processes tactical data streams.",
                            Payload = new StructuredPayload
                            {
                                ActiveSubject = "Caleb",
                                Applicability = "Applies",
                                AllowedUse = "AssertAsFact",
                                SourceComponent = "direct_scene",
                            },
                        },
                        new ScriptedBoundaryOutput
                        {
                            Boundary = nameof(BoundaryType.VisibleResponse),
                            Component = "visible_response",
                            Content = "*Caleb rolls up his sleeve, revealing the intricate combat prosthetic.*",
                        },
                    ],
                },
            ],
        };
    }

    /// <summary>
    /// Scenario where the query explicitly compares Xavier and Caleb.
    /// Both characters' details should be available for the comparison.
    /// </summary>
    private static RoleplayScenario CreateCrossCharacterScenario()
    {
        return new RoleplayScenario
        {
            Name = "xavier-caleb-cross-character",
            ActiveCharacter = "Xavier",
            OffCharacter = "Caleb",
            ForbiddenDetails = [],
            Turns =
            [
                new ScriptedTurn
                {
                    TurnNumber = 1,
                    UserMessage = "Compare Xavier's and Caleb's combat gear and augmentations.",
                    Boundaries =
                    [
                        new ScriptedBoundaryOutput
                        {
                            Boundary = nameof(BoundaryType.QueryLore),
                            Component = "query_lore",
                            Content = "Xavier uses standard-issue hunter gear — carbine and combat knife. " +
                                      "Caleb has a custom prosthetic combat arm and Toring Chip interface. " +
                                      "Both are Division operatives with standard neural interfaces.",
                            SourceRefs = ["characters/xavier.md", "characters/caleb.md", "world/body-tech.md"],
                            Payload = new StructuredPayload
                            {
                                ActiveSubject = "Xavier",
                                Applicability = "Applies",
                                AllowedUse = "AssertAsFact",
                                LoreRefs = ["characters/xavier.md", "characters/caleb.md", "world/body-tech.md"],
                                SourceComponent = "query_lore",
                            },
                        },
                        new ScriptedBoundaryOutput
                        {
                            Boundary = nameof(BoundaryType.NarrativeDirector),
                            Component = "scene_brief",
                            Content = "Present a comparison of Xavier and Caleb's equipment. " +
                                      "Xavier: standard carbine, knife. Caleb: prosthetic arm, Toring Chip. " +
                                      "Both share standard neural interface technology.",
                            Payload = new StructuredPayload
                            {
                                ActiveSubject = "Xavier",
                                Applicability = "Applies",
                                AllowedUse = "AssertAsFact",
                                SourceComponent = "scene_brief",
                            },
                        },
                        new ScriptedBoundaryOutput
                        {
                            Boundary = nameof(BoundaryType.ProseWriter),
                            Component = "direct_scene",
                            Content = "Xavier's gear is standard Division-issue — a well-worn carbine and combat knife. " +
                                      "Caleb, by contrast, relies on his custom prosthetic combat arm and the Toring Chip " +
                                      "at his temple. Both operatives share the standard neural interface common to all Division personnel.",
                            Payload = new StructuredPayload
                            {
                                ActiveSubject = "Xavier",
                                Applicability = "Applies",
                                AllowedUse = "AssertAsFact",
                                SourceComponent = "direct_scene",
                            },
                        },
                        new ScriptedBoundaryOutput
                        {
                            Boundary = nameof(BoundaryType.VisibleResponse),
                            Component = "visible_response",
                            Content = "*The two operatives present a study in contrasts — Xavier's practical, well-used gear " +
                                      "versus Caleb's specialized combat augmentations.*",
                        },
                    ],
                },
            ],
        };
    }
}
