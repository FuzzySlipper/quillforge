using QuillForge.RoleplayDriftHarness.Fixtures;
using QuillForge.RoleplayDriftHarness.Models;
using QuillForge.RoleplayDriftHarness.Runners;

namespace QuillForge.RoleplayDriftHarness;

/// <summary>
/// Console entry point for the Roleplay Drift Harness.
///
/// Usage:
///   dotnet run --project src/QuillForge.RoleplayDriftHarness
///     -- --scenario xavier-caleb
///     --output-dir /tmp/qf-drift-run
///
///   Optional live LLM evaluator:
///     --base-url http://localhost:1234/v1
///     --model qwen3-35b
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = ParseOptions(args);
        Console.WriteLine($"Roleplay Drift Harness starting with scenario={options.ScenarioName} output={options.OutputDir}");

        // Load the scenario from built-in fixtures
        var scenario = LoadScenario(options.ScenarioName);
        if (scenario is null)
        {
            Console.Error.WriteLine($"Unknown scenario: {options.ScenarioName}. Available: xavier-caleb.");
            return 1;
        }

        // Run the deterministic scenario
        var detector = new DriftDetector();
        var runner = new ScenarioRunner(detector);
        var run = runner.Run(scenario);

        // If a live LLM evaluator seam is configured, note it (extension point)
        if (!string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            Console.WriteLine($"Live LLM evaluator configured: {options.BaseUrl} model={options.Model ?? "(default)"}");
            Console.WriteLine("Note: Live LLM evaluation is an extension seam. Deterministic analysis used by default.");
        }

        // Write artifacts
        var writer = new DriftReportWriter();
        writer.WriteAll(options.OutputDir, run);

        Console.WriteLine($"Drift harness complete. Run ID: {run.RunId}");
        Console.WriteLine($"  Passed: {run.Evaluation?.Passed}");
        Console.WriteLine($"  Events: {run.TraceEvents.Count}");
        Console.WriteLine($"  Drift findings: {run.DriftResult.Findings.Count}");

        foreach (var finding in run.DriftResult.Findings)
        {
            Console.WriteLine($"  - [{finding.LikelyOrigin}] '{finding.ForbiddenFact}' at turn {finding.FirstAppearanceTurn} in {finding.FirstAppearanceBoundary}/{finding.FirstAppearanceComponent}");
        }

        Console.WriteLine($"Artifacts written to: {options.OutputDir}");
        Console.WriteLine($"  run.json, trace.ndjson, evaluation.json, lore-results.json, summary.md");

        return run.Evaluation?.Passed == false ? 2 : 0;
    }

    private static DriftHarnessOptions ParseOptions(string[] args)
    {
        var options = new DriftHarnessOptions
        {
            OutputDir = GetArg(args, "--output-dir")
                ?? Environment.GetEnvironmentVariable("DRIFT_HARNESS_OUTPUT_DIR")
                ?? Path.Combine(Path.GetTempPath(), "quillforge", "lore-drift"),
            ScenarioName = GetArg(args, "--scenario")
                ?? Environment.GetEnvironmentVariable("DRIFT_HARNESS_SCENARIO")
                ?? "xavier-caleb",
            BaseUrl = GetArg(args, "--base-url")
                ?? Environment.GetEnvironmentVariable("DRIFT_HARNESS_BASE_URL"),
            Model = GetArg(args, "--model")
                ?? Environment.GetEnvironmentVariable("DRIFT_HARNESS_MODEL"),
            ApiKey = GetArg(args, "--api-key")
                ?? Environment.GetEnvironmentVariable("DRIFT_HARNESS_API_KEY"),
        };

        // If output dir has a default temp path placeholder, use it
        if (options.OutputDir.StartsWith("/tmp/quillforge", StringComparison.Ordinal))
        {
            options = options with
            {
                OutputDir = Path.Combine(options.OutputDir, $"drift-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}"),
            };
        }

        return options;
    }

    private static RoleplayScenario? LoadScenario(string scenarioName)
    {
        return scenarioName.ToLowerInvariant() switch
        {
            "xavier-caleb" => XavierCalebScenario.CreateClean(),
            _ => null,
        };
    }

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }
        return null;
    }
}
