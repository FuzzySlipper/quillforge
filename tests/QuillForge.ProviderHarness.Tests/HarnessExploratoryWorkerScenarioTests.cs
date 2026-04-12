using QuillForge.Core;

namespace QuillForge.ProviderHarness.Tests;

public sealed class HarnessExploratoryWorkerScenarioTests
{
    [Fact]
    public async Task ExploratoryWorkerBackedForgeScenario_RunsEndToEndAndCapturesWorkerRoles()
    {
        var projectName = "moon-heist";
        var premise = "A jewel thief is forced into an arranged marriage during the winter gala.";
        var scenario = new HarnessWorkerScenario
        {
            Name = "forge-exploratory-workers",
            ProjectName = projectName,
            Premise = premise,
            LoreFiles = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["world.md"] =
                    """
                    Aurora once hid a sapphire ring inside the conservatory wall.
                    The arranged marriage contract binds Aurora and Lucian to present a united front.
                    The winter gala is the first public test of that alliance.
                    """,
            },
            Routes =
            [
                new HarnessWorkerRoute("forge-planner-model", HarnessWorkerRole.Planner),
                new HarnessWorkerRoute("forge-writer-model", HarnessWorkerRole.Writer),
                new HarnessWorkerRoute("forge-reviewer-model", HarnessWorkerRole.Reviewer),
                new HarnessWorkerRoute("forge-librarian-model", HarnessWorkerRole.Librarian),
            ],
        };

        await using var providerHost = await HarnessProviderHost.StartAsync(
            new WorkerBackedHarnessResponseSource(scenario));
        await using var runner = new HarnessForgeScenarioRunner(providerHost);

        var report = await runner.RunCanonicalPauseResumeScenarioAsync(projectName, premise);

        Assert.Equal("forge-pause-resume", report.ScenarioName);
        Assert.Equal(3, report.Phases.Count);

        foreach (var phase in report.Phases)
        {
            Assert.True(
                phase.Evaluation.Status == HarnessEvaluationStatus.Passed,
                FormatPhaseFailures(phase));
            Assert.NotEmpty(phase.Run.ProviderTraces);
            Assert.All(phase.Run.ProviderTraces, trace => Assert.NotNull(trace.WorkerTrace));
        }

        var workerRun = new DualSidedHarnessRun
        {
            ScenarioName = report.ScenarioName,
            ProviderTraces = report.Phases
                .SelectMany(phase => phase.Run.ProviderTraces)
                .ToList(),
        };

        var workerRoleEvaluation = new HarnessEvaluator().Evaluate(
            workerRun,
            [
                new ExpectedWorkerRoleObservedAssertion(
                    HarnessWorkerRole.Planner,
                    HarnessWorkerRole.Writer,
                    HarnessWorkerRole.Reviewer,
                    HarnessWorkerRole.Librarian),
            ]);

        Assert.Equal(
            HarnessEvaluationStatus.Passed,
            workerRoleEvaluation.Status);

        var approvePhase = Assert.Single(report.Phases, phase => phase.PhaseName == "approve");
        var outputArtifact = Assert.Single(
            approvePhase.Run.ArtifactTrace!.Snapshots,
            snapshot => snapshot.RelativePath == $"{ContentPaths.Forge}/{projectName}/output/story.md");
        Assert.True(outputArtifact.Exists);
        Assert.Contains("Aurora", outputArtifact.Content);
        Assert.Contains("Lucian", outputArtifact.Content);

        var runLoreArtifact = Assert.Single(
            approvePhase.Run.ArtifactTrace!.Snapshots,
            snapshot => snapshot.RelativePath == $"{ContentPaths.Forge}/{projectName}/run-lore.md");
        Assert.True(runLoreArtifact.Exists);
        Assert.Contains("sapphire ring", runLoreArtifact.Content, StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatPhaseFailures(HarnessForgePhaseReport phase)
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

        return string.Join(Environment.NewLine, lines);
    }
}
