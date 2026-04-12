using QuillForge.Core.Models;

namespace QuillForge.ProviderHarness.Tests;

public static class HarnessRunReportWriter
{
    public const int SchemaVersion = 1;

    public static HarnessPersistedRunReport WriteForgePhaseReport(
        HarnessRunArtifactStore artifactStore,
        string phaseName,
        DualSidedHarnessRun run,
        HarnessEvaluationResult evaluation)
    {
        var providerTraceFiles = ResolveProviderTraceFiles(artifactStore, run.ProviderTraces);
        var forgeTracePath = run.ForgeTrace is null
            ? null
            : artifactStore.PersistJson($"app/forge-{phaseName}-trace.json", run.ForgeTrace);
        var manifestPath = run.ForgeManifest is null
            ? null
            : artifactStore.PersistJson($"artifacts/forge-{phaseName}-manifest.json", run.ForgeManifest);
        var artifactTracePath = run.ArtifactTrace is null
            ? null
            : artifactStore.PersistJson($"artifacts/forge-{phaseName}-artifact-trace.json", run.ArtifactTrace);

        var report = new HarnessPersistedRunReport
        {
            SchemaVersion = SchemaVersion,
            Kind = "forge-phase",
            RunId = artifactStore.RunId,
            ScenarioName = run.ScenarioName,
            ScopeName = phaseName,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = evaluation.Status.ToString(),
            ProviderTraceFiles = providerTraceFiles,
            AppTraceFile = forgeTracePath,
            ArtifactTraceFile = artifactTracePath,
            ManifestFile = manifestPath,
            AssertionResults = evaluation.AssertionResults,
            Findings = evaluation.Findings,
        };

        report.JsonReportPath = artifactStore.PersistJson($"reports/forge-{phaseName}-evaluation.json", report);
        report.MarkdownReportPath = artifactStore.PersistText(
            $"reports/forge-{phaseName}-summary.md",
            BuildMarkdownReport(report));

        return report;
    }

    public static HarnessPersistedRunReport WriteInteractiveReport(
        HarnessRunArtifactStore artifactStore,
        HarnessInteractiveScenarioReport scenarioReport)
    {
        var providerTraceFiles = ResolveProviderTraceFiles(artifactStore, scenarioReport.Run.ProviderTraces);
        var appTracePath = scenarioReport.Run.AppTrace is null
            ? null
            : artifactStore.PersistJson($"app/interactive-{scenarioReport.Mode}-trace.json", scenarioReport.Run.AppTrace);

        var report = new HarnessPersistedRunReport
        {
            SchemaVersion = SchemaVersion,
            Kind = "interactive",
            RunId = artifactStore.RunId,
            ScenarioName = scenarioReport.Run.ScenarioName,
            ScopeName = scenarioReport.Mode,
            CreatedAt = DateTimeOffset.UtcNow,
            Status = "captured",
            ProviderTraceFiles = providerTraceFiles,
            AppTraceFile = appTracePath,
            UsageSummary = scenarioReport.UsageSummary,
            AssertionResults = [],
            Findings = [],
        };

        report.JsonReportPath = artifactStore.PersistJson($"reports/interactive-{scenarioReport.Mode}-capture.json", report);
        report.MarkdownReportPath = artifactStore.PersistText(
            $"reports/interactive-{scenarioReport.Mode}-summary.md",
            BuildMarkdownReport(report));

        return report;
    }

    private static IReadOnlyList<string> ResolveProviderTraceFiles(
        HarnessRunArtifactStore artifactStore,
        IReadOnlyList<HarnessProviderTrace> traces)
    {
        var references = artifactStore.SnapshotProviderTraceFiles();
        var files = new List<string>();

        foreach (var trace in traces)
        {
            var reference = references.FirstOrDefault(item =>
                string.Equals(item.TraceId, trace.TraceId, StringComparison.Ordinal));
            if (reference is not null)
            {
                files.Add(reference.RelativePath);
            }
        }

        return files;
    }

    private static string BuildMarkdownReport(HarnessPersistedRunReport report)
    {
        var lines = new List<string>
        {
            $"# Harness Report: {report.ScenarioName}",
            string.Empty,
            $"- Run ID: `{report.RunId}`",
            $"- Kind: `{report.Kind}`",
            $"- Scope: `{report.ScopeName}`",
            $"- Status: `{report.Status}`",
            $"- Schema Version: `{report.SchemaVersion}`",
        };

        if (report.ProviderTraceFiles.Count > 0)
        {
            lines.Add("- Provider trace files:");
            foreach (var file in report.ProviderTraceFiles)
            {
                lines.Add($"  - `{file}`");
            }
        }

        if (!string.IsNullOrWhiteSpace(report.AppTraceFile))
        {
            lines.Add($"- App trace file: `{report.AppTraceFile}`");
        }

        if (!string.IsNullOrWhiteSpace(report.ManifestFile))
        {
            lines.Add($"- Manifest file: `{report.ManifestFile}`");
        }

        if (!string.IsNullOrWhiteSpace(report.ArtifactTraceFile))
        {
            lines.Add($"- Artifact trace file: `{report.ArtifactTraceFile}`");
        }

        if (report.UsageSummary is not null)
        {
            lines.Add("- Session usage:");
            lines.Add($"  - total requests: `{report.UsageSummary.TotalRequests}`");
            lines.Add($"  - total input tokens: `{report.UsageSummary.TotalInputTokens}`");
            lines.Add($"  - total output tokens: `{report.UsageSummary.TotalOutputTokens}`");
        }

        lines.Add(string.Empty);
        lines.Add("## Findings");

        if (report.Findings.Count == 0)
        {
            lines.Add("No findings recorded.");
        }
        else
        {
            foreach (var finding in report.Findings)
            {
                lines.Add($"- [{finding.Severity}] `{finding.Code}`");
                lines.Add($"  - Expected: {finding.Expected}");
                lines.Add($"  - Actual: {finding.Actual}");
                if (finding.Evidence.Count > 0)
                {
                    lines.Add($"  - Evidence: {string.Join(", ", finding.Evidence)}");
                }
            }
        }

        return string.Join(Environment.NewLine, lines);
    }
}

public sealed record HarnessPersistedRunReport
{
    public required int SchemaVersion { get; init; }
    public required string Kind { get; init; }
    public required string RunId { get; init; }
    public required string ScenarioName { get; init; }
    public required string ScopeName { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string Status { get; init; }
    public IReadOnlyList<string> ProviderTraceFiles { get; init; } = [];
    public string? AppTraceFile { get; init; }
    public string? ArtifactTraceFile { get; init; }
    public string? ManifestFile { get; init; }
    public SessionUsageSummary? UsageSummary { get; init; }
    public IReadOnlyList<HarnessAssertionResult> AssertionResults { get; init; } = [];
    public IReadOnlyList<HarnessFinding> Findings { get; init; } = [];
    public string? JsonReportPath { get; set; }
    public string? MarkdownReportPath { get; set; }
}
