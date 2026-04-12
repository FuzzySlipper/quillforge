using System.Text.Json;
using QuillForge.Core;

namespace QuillForge.ProviderHarness.Tests;

public sealed class HarnessForgeScenarioTests
{
    [Fact]
    public async Task CanonicalForgePauseResumeScenario_RunsEndToEndAgainstHarnessProvider()
    {
        var projectName = "moon-heist";
        var premise = "A jewel thief is forced into an arranged marriage during the winter gala.";

        var fixture = HarnessForgeScenarioFixtures.CreateCanonicalPauseResume(projectName, premise);
        var fixtureJson = JsonSerializer.Serialize(fixture);
        var roundTrippedFixture = JsonSerializer.Deserialize<HarnessForgeScenarioFixture>(fixtureJson)
            ?? throw new InvalidOperationException("Failed to deserialize canonical Forge fixture.");

        await using var providerHost = await HarnessProviderHost.StartAsync(roundTrippedFixture.ProviderScenario);
        await using var runner = new HarnessForgeScenarioRunner(providerHost);

        var report = await runner.RunScenarioAsync(roundTrippedFixture);

        Assert.Equal("forge-pause-resume", report.ScenarioName);
        Assert.Equal(projectName, report.ProjectName);
        Assert.Contains(report.Phases, phase => phase.PhaseName == "design");
        Assert.Contains(report.Phases, phase => phase.PhaseName == "start");

        foreach (var phase in report.Phases)
        {
            Assert.True(
                phase.Evaluation.Status == HarnessEvaluationStatus.Passed,
                FormatFailures(phase));
            Assert.Equal(providerHost.ArtifactStore.RunId, phase.Run.RunId);
            Assert.NotEmpty(phase.Run.ProviderTraces);
            Assert.NotNull(phase.Run.ForgeTrace);
            Assert.NotNull(phase.Run.ForgeManifest);
            Assert.NotNull(phase.Run.ArtifactTrace);
            Assert.NotNull(phase.PersistedReport);
            Assert.False(string.IsNullOrWhiteSpace(phase.PersistedReport!.JsonReportPath));
            Assert.False(string.IsNullOrWhiteSpace(phase.PersistedReport.MarkdownReportPath));

            var expectedTraceIds = phase.Run.ProviderTraces.Select(trace => trace.TraceId).ToArray();
            Assert.Equal(expectedTraceIds, phase.Run.ForgeTrace!.RelatedProviderTraceIds.ToArray());
            Assert.Equal(expectedTraceIds, phase.Run.ArtifactTrace!.RelatedProviderTraceIds.ToArray());
            Assert.Equal(providerHost.ArtifactStore.RunId, phase.Run.ForgeTrace.RunId);
            Assert.Equal(providerHost.ArtifactStore.RunId, phase.Run.ArtifactTrace.RunId);

            var jsonReportPath = Path.Combine(
                providerHost.ArtifactStore.RunDirectory,
                phase.PersistedReport.JsonReportPath!.Replace('/', Path.DirectorySeparatorChar));
            var markdownReportPath = Path.Combine(
                providerHost.ArtifactStore.RunDirectory,
                phase.PersistedReport.MarkdownReportPath!.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(jsonReportPath));
            Assert.True(File.Exists(markdownReportPath));

            var markdown = await File.ReadAllTextAsync(markdownReportPath);
            Assert.Contains("## Findings", markdown);
            Assert.Contains($"Status: `{phase.Evaluation.Status}`", markdown);
        }

        Assert.Contains(report.Phases, phase => phase.PhaseName == "approve");

        var startPhase = Assert.Single(report.Phases, phase => phase.PhaseName == "start");
        Assert.Contains(
            startPhase.Run.ForgeTrace!.Events,
            evt => evt is ForgePausedObserved);

        var approvePhase = Assert.Single(report.Phases, phase => phase.PhaseName == "approve");
        var outputArtifact = Assert.Single(
            approvePhase.Run.ArtifactTrace!.Snapshots,
            snapshot => snapshot.RelativePath == $"{ContentPaths.Forge}/{projectName}/output/story.md");
        Assert.True(outputArtifact.Exists);
        Assert.Contains("Aurora crossed the conservatory", outputArtifact.Content);

        var runLoreArtifact = Assert.Single(
            approvePhase.Run.ArtifactTrace!.Snapshots,
            snapshot => snapshot.RelativePath == $"{ContentPaths.Forge}/{projectName}/run-lore.md");
        Assert.True(runLoreArtifact.Exists);
        Assert.Contains("sapphire ring", runLoreArtifact.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ForgeMismatchReport_PointsToUsefulEvidenceForFailedAssertion()
    {
        var projectName = "moon-heist-mismatch";
        var premise = "A jewel thief is forced into an arranged marriage during the winter gala.";

        var fixture = HarnessForgeScenarioFixtures.CreateCanonicalPauseResume(
            projectName,
            premise,
            "forge-pause-resume-mismatch");

        await using var providerHost = await HarnessProviderHost.StartAsync(fixture.ProviderScenario);
        await using var runner = new HarnessForgeScenarioRunner(providerHost);

        var report = await runner.RunScenarioAsync(fixture);
        var designPhase = Assert.Single(report.Phases, phase => phase.PhaseName == "design");

        var failedEvaluation = new HarnessEvaluator().Evaluate(
            designPhase.Run,
            [
                new ExpectedForgeManifestStageAssertion("Done", expectedPaused: false),
            ]);

        Assert.Equal(HarnessEvaluationStatus.Failed, failedEvaluation.Status);

        var persistedFailureReport = HarnessRunReportWriter.WriteForgePhaseReport(
            providerHost.ArtifactStore,
            "design-mismatch",
            designPhase.Run,
            failedEvaluation);

        var markdownPath = Path.Combine(
            providerHost.ArtifactStore.RunDirectory,
            persistedFailureReport.MarkdownReportPath!.Replace('/', Path.DirectorySeparatorChar));
        var markdown = await File.ReadAllTextAsync(markdownPath);

        Assert.NotEmpty(failedEvaluation.Findings);
        var expectedFinding = failedEvaluation.Findings.First();
        var expectedFindingCode = expectedFinding.Code;
        Assert.Contains(expectedFindingCode, markdown);
        Assert.Contains(expectedFinding.Expected, markdown);
        Assert.Contains("provider/traces/", markdown);
    }

    [Fact]
    public async Task ForgeArtifactMismatchReport_PointsToMissingOutputArtifact()
    {
        var projectName = "moon-heist-artifact-mismatch";
        var premise = "A jewel thief is forced into an arranged marriage during the winter gala.";
        var fixture = HarnessForgeScenarioFixtures.CreateCanonicalPauseResume(
            projectName,
            premise,
            "forge-pause-resume-artifact-mismatch");

        await using var providerHost = await HarnessProviderHost.StartAsync(fixture.ProviderScenario);
        await using var runner = new HarnessForgeScenarioRunner(providerHost);

        var report = await runner.RunScenarioAsync(fixture);
        var approvePhase = Assert.Single(report.Phases, phase => phase.PhaseName == "approve");

        var failedEvaluation = new HarnessEvaluator().Evaluate(
            approvePhase.Run,
            [
                new ExpectedArtifactPresenceAssertion($"{ContentPaths.Forge}/{projectName}/output/epilogue.md"),
            ]);

        Assert.Equal(HarnessEvaluationStatus.Failed, failedEvaluation.Status);

        var persistedFailureReport = HarnessRunReportWriter.WriteForgePhaseReport(
            providerHost.ArtifactStore,
            "approve-artifact-mismatch",
            approvePhase.Run,
            failedEvaluation);

        var markdownPath = Path.Combine(
            providerHost.ArtifactStore.RunDirectory,
            persistedFailureReport.MarkdownReportPath!.Replace('/', Path.DirectorySeparatorChar));
        var markdown = await File.ReadAllTextAsync(markdownPath);

        Assert.NotEmpty(failedEvaluation.Findings);
        var expectedFinding = failedEvaluation.Findings.First();
        Assert.Contains(expectedFinding.Code, markdown);
        Assert.Contains($"{ContentPaths.Forge}/{projectName}/output/epilogue.md", markdown);
    }

    private static string FormatFailures(HarnessForgePhaseReport phase)
    {
        if (phase.Evaluation.Findings.Count == 0)
        {
            return $"Phase '{phase.PhaseName}' failed without recorded findings.";
        }

        var lines = new List<string>
        {
            $"Phase '{phase.PhaseName}' failed.",
        };

        foreach (var finding in phase.Evaluation.Findings)
        {
            lines.Add($"[{finding.Severity}] {finding.Code}");
            lines.Add($"Expected: {finding.Expected}");
            lines.Add($"Actual: {finding.Actual}");
            lines.Add($"Evidence: {string.Join(", ", finding.Evidence)}");
        }

        if (phase.Run.ProviderTraces.Count > 0)
        {
            lines.Add("Provider requests:");
            for (var i = 0; i < phase.Run.ProviderTraces.Count; i++)
            {
                var trace = phase.Run.ProviderTraces[i];
                lines.Add($"[{i}] model={trace.Model} path={trace.Path}");
                lines.Add(trace.RawRequestBody);
            }
        }

        if (phase.Run.ForgeTrace is not null)
        {
            lines.Add("Forge events:");
            foreach (var evt in phase.Run.ForgeTrace.Events)
            {
                lines.Add(evt switch
                {
                    ForgeStageStartedObserved started => $"stage_started:{started.StageName}",
                    ForgeStageCompletedObserved completed => $"stage_completed:{completed.StageName}",
                    ForgeChapterObserved chapter => $"chapter:{chapter.ChapterId}:{chapter.ChapterStatus}:{chapter.Detail}",
                    ForgeProgressObserved progress => $"progress:{progress.Source}:{progress.Message}",
                    ForgePausedObserved paused => $"pause:{paused.Message}",
                    ForgeCompletedObserved completed => $"complete:{completed.TotalTokens}:{completed.ChaptersComplete}",
                    ForgeErrorObserved error => $"error:{error.Source}:{error.Message}",
                    ForgeUnknownObserved unknown => $"unknown:{unknown.Type}:{unknown.Message}",
                    _ => evt.GetType().Name,
                });
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

}
