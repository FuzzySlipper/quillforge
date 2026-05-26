using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using QuillForge.Providers.Adapters;
using QuillForge.RoleplayDriftHarness.Fixtures;
using QuillForge.RoleplayDriftHarness.Models;
using QuillForge.RoleplayDriftHarness.Runners;

namespace QuillForge.RoleplayDriftHarness;

/// <summary>
/// Console entry point for the Roleplay Drift Harness.
///
/// Usage:
///   Deterministic mode (default):
///     dotnet run --project src/QuillForge.RoleplayDriftHarness
///       -- --scenario xavier-caleb
///       --output-dir /tmp/qf-drift-run
///
///   Live LLM-backed roleplay lore consistency test:
///     dotnet run --project src/QuillForge.RoleplayDriftHarness
///       -- --live
///       --base-url http://localhost:1234/v1
///       --model qwen3-35b
///       --output-dir artifacts/roleplay-lore-live/20260525-113312
///
///   Live mode with separate provider/model for roleplay calls:
///     dotnet run --project src/QuillForge.RoleplayDriftHarness
///       -- --live
///       --base-url https://api.openai.com/v1
///       --model gpt-4o
///       --live-model gpt-4o
///       --api-key sk-...
///       --output-dir artifacts/roleplay-lore-live/20260525-113312
///
///   Strict live mode (drives real NarrativeDirectorAgent pipeline):
///     dotnet run --project src/QuillForge.RoleplayDriftHarness
///       -- --strict-live
///       --base-url https://api.openai.com/v1
///       --model gpt-4o
///       --api-key sk-...
///       --output-dir artifacts/roleplay-lore-strict/20260526-<timestamp>
///
/// Environment variables (all optional, CLI flags take precedence):
///   DRIFT_HARNESS_OUTPUT_DIR   Default output directory.
///   DRIFT_HARNESS_SCENARIO     Default scenario name.
///   DRIFT_HARNESS_BASE_URL     LLM provider base URL.
///   DRIFT_HARNESS_MODEL        LLM model name.
///   DRIFT_HARNESS_API_KEY      LLM API key.
///   DRIFT_HARNESS_LIVE         Set to "true" to enable live mode.
///   DRIFT_HARNESS_LIVE_PROVIDER Provider alias for live roleplay calls.
///   DRIFT_HARNESS_LIVE_MODEL   Model name for live roleplay calls (overrides --model).
///   DRIFT_HARNESS_STRICT_LIVE  Set to "true" to enable strict live mode.
///   DRIFT_HARNESS_ND_MAX_ROUNDS  Max tool rounds for Narrative Director (default: 8).
///   DRIFT_HARNESS_LIBRARIAN_MAX_ROUNDS  Max tool rounds for Librarian (default: 1).
///   DRIFT_HARNESS_PW_MAX_ROUNDS  Max tool rounds for Prose Writer (default: 10).
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var options = ParseOptions(args);
        Console.WriteLine($"Roleplay Drift Harness starting with scenario={options.ScenarioName} output={options.OutputDir}");

        if (options.StrictLive)
        {
            return await RunStrictLiveMode(options);
        }

        if (options.Live)
        {
            return await RunLiveMode(options);
        }

        // ── Deterministic mode ──
        return await RunDeterministicMode(options);
    }

    private static async Task<int> RunDeterministicMode(DriftHarnessOptions options)
    {
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
            Console.WriteLine("      Use --live to run actual LLM-backed roleplay lore consistency tests.");
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

    private static async Task<int> RunLiveMode(DriftHarnessOptions options)
    {
        var baseUrl = options.BaseUrl!;
        var model = (options.LiveModel ?? options.Model)!;
        var provider = options.LiveProvider ?? "openai-compatible";
        var apiKey = options.ApiKey;

        // Prerequisite validation
        var missingPrereqs = new List<string>();

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            missingPrereqs.Add("--base-url (or DRIFT_HARNESS_BASE_URL) — the LLM provider endpoint URL");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            missingPrereqs.Add("--model (or DRIFT_HARNESS_MODEL) — the LLM model name");
        }

        if (missingPrereqs.Count > 0)
        {
            Console.Error.WriteLine("ERROR: Live LLM mode requires provider configuration.");
            Console.Error.WriteLine($"Missing prerequisite(s):");
            foreach (var prereq in missingPrereqs)
            {
                Console.Error.WriteLine($"  - {prereq}");
            }
            Console.Error.WriteLine();
            Console.Error.WriteLine("Usage example:");
            Console.Error.WriteLine("  dotnet run --project src/QuillForge.RoleplayDriftHarness -- --live \\");
            Console.Error.WriteLine("    --base-url https://api.openai.com/v1 \\");
            Console.Error.WriteLine("    --model gpt-4o \\");
            Console.Error.WriteLine("    --api-key sk-... \\");
            Console.Error.WriteLine("    --output-dir artifacts/roleplay-lore-live/<timestamp>");
            return 3;
        }

        Console.WriteLine($"Live LLM mode activated.");
        Console.WriteLine($"  Provider: {provider}");
        Console.WriteLine($"  Base URL: {baseUrl}");
        Console.WriteLine($"  Model: {model}");
        Console.WriteLine($"  API Key: {(string.IsNullOrWhiteSpace(apiKey) ? "(none)" : "***")}");
        Console.WriteLine($"  Output: {options.OutputDir}");

        // Build the LLM client
        try
        {
            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(baseUrl),
            };

            var effectiveApiKey = string.IsNullOrWhiteSpace(apiKey) ? "no-key" : apiKey;
            var openAiClient = new OpenAIClient(new ApiKeyCredential(effectiveApiKey), clientOptions);
            var chatClient = openAiClient.GetChatClient(model).AsIChatClient();

            // Optional logging
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Warning);
            });

            var logger = loggerFactory.CreateLogger<ChatClientCompletionService>();

            // Wrap in our adapter
            var completionService = new ChatClientCompletionService(chatClient, logger);
            var detector = new DriftDetector();
            var runner = new LiveLoreConsistencyRunner(completionService, detector, provider, model);

            // Quick connectivity check
            Console.WriteLine("  Checking provider connectivity...");
            var canReach = runner.CanReachProvider();
            if (!canReach)
            {
                Console.Error.WriteLine("WARNING: Provider connectivity check failed.");
                Console.Error.WriteLine("  The live test will attempt to continue, but the provider may be unreachable.");
                Console.Error.WriteLine("  Check that the base URL, model name, and API key are correct.");
                Console.Error.WriteLine("  Continuing anyway...");
            }
            else
            {
                Console.WriteLine("  Provider is reachable. Running live test...");
            }

            // Run the live test
            var run = await runner.RunAsync(options.OutputDir);

            Console.WriteLine();
            Console.WriteLine($"Live lore consistency test complete. Run ID: {run.RunId}");
            Console.WriteLine($"  Passed (no drift): {run.Evaluation?.Passed}");
            Console.WriteLine($"  Events: {run.TraceEvents.Count}");
            Console.WriteLine($"  Drift findings: {run.DriftResult.Findings.Count}");

            foreach (var finding in run.DriftResult.Findings)
            {
                Console.WriteLine($"  - [{finding.LikelyOrigin}] '{finding.ForbiddenFact}' at turn {finding.FirstAppearanceTurn} in {finding.FirstAppearanceBoundary}/{finding.FirstAppearanceComponent}");
            }

            Console.WriteLine($"\nArtifacts written to: {options.OutputDir}");
            Console.WriteLine($"  run.json, trace.ndjson, evaluation.json, lore-results.json, summary.md");

            if (run.Evaluation?.Notes is not null)
            {
                Console.WriteLine($"\nNOTE: {run.Evaluation.Notes}");
            }

            return run.Evaluation?.Passed == false ? 2 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\nERROR: Live LLM test failed: {ex.Message}");
            Console.Error.WriteLine($"  Type: {ex.GetType().FullName}");
            if (ex.InnerException is not null)
            {
                Console.Error.WriteLine($"  Inner: {ex.InnerException.Message}");
            }

            // Write a partial diagnostic artifact even on failure
            try
            {
                Directory.CreateDirectory(options.OutputDir);
                var errorPath = Path.Combine(options.OutputDir, "ERROR.txt");
                await File.WriteAllTextAsync(errorPath,
                    $"Live LLM test failed at {DateTimeOffset.UtcNow:O}\n" +
                    $"Provider: {provider}\n" +
                    $"Model: {model}\n" +
                    $"Base URL: {baseUrl}\n" +
                    $"Error: {ex}\n");
                Console.WriteLine($"Partial error diagnostic written to: {errorPath}");
            }
            catch
            {
                // Ignore write errors during failure handling
            }

            return 4;
        }
    }

    private static async Task<int> RunStrictLiveMode(DriftHarnessOptions options)
    {
        var baseUrl = options.BaseUrl!;
        var model = (options.LiveModel ?? options.Model)!;
        var provider = options.LiveProvider ?? "openai-compatible";
        var apiKey = options.ApiKey;

        // Prerequisite validation
        var missingPrereqs = new List<string>();

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            missingPrereqs.Add("--base-url (or DRIFT_HARNESS_BASE_URL) — the LLM provider endpoint URL");
        }

        if (string.IsNullOrWhiteSpace(model))
        {
            missingPrereqs.Add("--model (or DRIFT_HARNESS_MODEL) — the LLM model name");
        }

        if (missingPrereqs.Count > 0)
        {
            Console.Error.WriteLine("ERROR: Strict live mode requires provider configuration.");
            Console.Error.WriteLine($"Missing prerequisite(s):");
            foreach (var prereq in missingPrereqs)
            {
                Console.Error.WriteLine($"  - {prereq}");
            }
            Console.Error.WriteLine();
            Console.Error.WriteLine("Usage example:");
            Console.Error.WriteLine("  dotnet run --project src/QuillForge.RoleplayDriftHarness -- --strict-live \\");
            Console.Error.WriteLine("    --base-url https://api.openai.com/v1 \\");
            Console.Error.WriteLine("    --model gpt-4o \\");
            Console.Error.WriteLine("    --api-key sk-... \\");
            Console.Error.WriteLine("    --output-dir artifacts/roleplay-lore-strict/<timestamp>");
            return 3;
        }

        Console.WriteLine("Strict live mode activated (real NarrativeDirectorAgent pipeline).");
        Console.WriteLine($"  Provider: {provider}");
        Console.WriteLine($"  Base URL: {baseUrl}");
        Console.WriteLine($"  Model: {model}");
        Console.WriteLine($"  API Key: {(string.IsNullOrWhiteSpace(apiKey) ? "(none)" : "***")}");
        Console.WriteLine($"  ND max rounds: {options.StrictNdMaxRounds}");
        Console.WriteLine($"  Librarian max rounds: {options.StrictLibrarianMaxRounds}");
        Console.WriteLine($"  PW max rounds: {options.StrictPwMaxRounds}");
        Console.WriteLine($"  Diagnostic level: {options.StrictDiagnosticLevel}");
        Console.WriteLine($"  Output: {options.OutputDir}");

        // Build the LLM client
        try
        {
            var clientOptions = new OpenAIClientOptions
            {
                Endpoint = new Uri(baseUrl),
            };

            var effectiveApiKey = string.IsNullOrWhiteSpace(apiKey) ? "no-key" : apiKey;
            var openAiClient = new OpenAIClient(new ApiKeyCredential(effectiveApiKey), clientOptions);
            var chatClient = openAiClient.GetChatClient(model).AsIChatClient();

            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddConsole();
                builder.SetMinimumLevel(LogLevel.Warning);
            });

            var logger = loggerFactory.CreateLogger<ChatClientCompletionService>();
            var completionService = new ChatClientCompletionService(chatClient, logger);
            var detector = new DriftDetector();
            var runner = new StrictRoleplaySessionRunner(
                completionService, detector, provider, model,
                ndMaxRounds: options.StrictNdMaxRounds,
                librarianMaxRounds: options.StrictLibrarianMaxRounds,
                pwMaxRounds: options.StrictPwMaxRounds,
                diagnosticLevel: options.StrictDiagnosticLevel);

            // Quick connectivity check
            Console.WriteLine("  Checking provider connectivity...");
            var canReach = runner.CanReachProvider();
            if (!canReach)
            {
                Console.Error.WriteLine("WARNING: Provider connectivity check failed.");
                Console.Error.WriteLine("  The live test will attempt to continue, but the provider may be unreachable.");
                Console.Error.WriteLine("  Check that the base URL, model name, and API key are correct.");
                Console.Error.WriteLine("  Continuing anyway...");
            }
            else
            {
                Console.WriteLine("  Provider is reachable. Running strict live test...");
            }

            // Run the strict live test
            var run = await runner.RunAsync(options.OutputDir);

            Console.WriteLine();
            Console.WriteLine($"Strict lore consistency test complete. Run ID: {run.RunId}");
            Console.WriteLine($"  Passed (no drift, no pipeline errors): {run.Evaluation?.Passed}");
            Console.WriteLine($"  Events: {run.TraceEvents.Count}");
            Console.WriteLine($"  Drift findings: {run.DriftResult.Findings.Count}");
            Console.WriteLine($"  Pipeline errors: {run.Evaluation?.PipelineErrors?.Count ?? 0}");

            if (run.Evaluation?.HasPipelineErrors == true)
            {
                Console.WriteLine();
                Console.WriteLine("  PIPELINE ERRORS:");
                foreach (var err in run.Evaluation.PipelineErrors!)
                {
                    Console.WriteLine($"  - Turn {err.Turn} [{err.Component}] {err.ErrorType}: {err.ErrorMessage}");
                }
            }

            foreach (var finding in run.DriftResult.Findings)
            {
                Console.WriteLine($"  - [{finding.LikelyOrigin}] '{finding.ForbiddenFact}' at turn {finding.FirstAppearanceTurn} in {finding.FirstAppearanceBoundary}/{finding.FirstAppearanceComponent}");
            }

            Console.WriteLine($"\nArtifacts written to: {options.OutputDir}");

            if (run.Evaluation?.Notes is not null)
            {
                Console.WriteLine($"\nNOTE: {run.Evaluation.Notes}");
            }

            // Exit codes:
            // 0 = passed (no drift, no pipeline errors)
            // 2 = drift found (but pipeline completed)
            // 3 = missing prerequisites (handled above)
            // 4 = unexpected exception (handled in catch below)
            // 5 = pipeline/provider errors (pipeline could not complete)
            if (run.Evaluation?.HasPipelineErrors == true)
                return 5;
            return run.Evaluation?.Passed == false ? 2 : 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"\nERROR: Strict live test failed: {ex.Message}");
            Console.Error.WriteLine($"  Type: {ex.GetType().FullName}");
            if (ex.InnerException is not null)
            {
                Console.Error.WriteLine($"  Inner: {ex.InnerException.Message}");
            }

            // Write a partial diagnostic artifact even on failure
            try
            {
                Directory.CreateDirectory(options.OutputDir);
                var errorPath = Path.Combine(options.OutputDir, "ERROR.txt");
                await File.WriteAllTextAsync(errorPath,
                    $"Strict live test failed at {DateTimeOffset.UtcNow:O}\n" +
                    $"Provider: {provider}\n" +
                    $"Model: {model}\n" +
                    $"Base URL: {baseUrl}\n" +
                    $"Error: {ex}\n");
                Console.WriteLine($"Partial error diagnostic written to: {errorPath}");
            }
            catch
            {
                // Ignore write errors during failure handling
            }

            return 4;
        }
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
            Live = HasFlag(args, "--live")
                || string.Equals(Environment.GetEnvironmentVariable("DRIFT_HARNESS_LIVE"), "true", StringComparison.OrdinalIgnoreCase),
            LiveProvider = GetArg(args, "--live-provider")
                ?? Environment.GetEnvironmentVariable("DRIFT_HARNESS_LIVE_PROVIDER"),
            LiveModel = GetArg(args, "--live-model")
                ?? Environment.GetEnvironmentVariable("DRIFT_HARNESS_LIVE_MODEL"),
            StrictLive = HasFlag(args, "--strict-live")
                || string.Equals(Environment.GetEnvironmentVariable("DRIFT_HARNESS_STRICT_LIVE"), "true", StringComparison.OrdinalIgnoreCase),
            StrictNdMaxRounds = int.TryParse(
                GetArg(args, "--strict-nd-max-rounds")
                ?? Environment.GetEnvironmentVariable("DRIFT_HARNESS_ND_MAX_ROUNDS"),
                out var ndRounds) ? ndRounds : 8,
            StrictLibrarianMaxRounds = int.TryParse(
                GetArg(args, "--strict-librarian-max-rounds")
                ?? Environment.GetEnvironmentVariable("DRIFT_HARNESS_LIBRARIAN_MAX_ROUNDS"),
                out var libRounds) ? libRounds : 1,
            StrictPwMaxRounds = int.TryParse(
                GetArg(args, "--strict-pw-max-rounds")
                ?? Environment.GetEnvironmentVariable("DRIFT_HARNESS_PW_MAX_ROUNDS"),
                out var pwRounds) ? pwRounds : 10,
            StrictDiagnosticLevel = GetArg(args, "--strict-diagnostic-level")
                ?? Environment.GetEnvironmentVariable("DRIFT_HARNESS_DIAGNOSTIC_LEVEL")
                ?? "normal",
        };

        // If output dir is the default temp path, add a timestamped subdir
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

    private static bool HasFlag(string[] args, string name)
    {
        return args.Any(a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    }
}
