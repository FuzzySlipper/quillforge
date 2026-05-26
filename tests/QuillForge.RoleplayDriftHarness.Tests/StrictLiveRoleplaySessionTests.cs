using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using QuillForge.Providers.Adapters;
using QuillForge.RoleplayDriftHarness.Runners;
using Xunit;

namespace QuillForge.RoleplayDriftHarness.Tests;

/// <summary>
/// Live LLM-backed strict roleplay session lore consistency test.
///
/// This test drives the REAL NarrativeDirectorAgent pipeline — not the
/// simulated boundary approach from #1673. It constructs the actual
/// agent chain (ToolLoop → LibrarianAgent → QueryLoreHandler →
/// ProseWriterAgent → WriteProseHandler → NarrativeDirectorAgent) and
/// exercises NarrativeDirectorAgent.DirectSceneAsync() with probe questions.
///
/// Skipped by default to preserve CI determinism. Remove the Skip
/// parameter and configure environment variables to run live.
///
/// Environment variables:
///   DRIFT_HARNESS_BASE_URL       — LLM provider endpoint (e.g. http://localhost:1234/v1)
///   DRIFT_HARNESS_LIVE_MODEL     — Model name (e.g. gpt-4o, claude-sonnet-4-20250514)
///   DRIFT_HARNESS_MODEL          — Fallback model name (if LIVE_MODEL unset)
///   DRIFT_HARNESS_API_KEY        — Optional API key
///   DRIFT_HARNESS_LIVE_PROVIDER  — Provider alias (default: openai-compatible)
///   DRIFT_HARNESS_OUTPUT_DIR     — Output directory for diagnostic artifacts
///   DRIFT_HARNESS_STRICT_LIVE    — Set to "true" to enable strict mode
///   DRIFT_HARNESS_ND_MAX_ROUNDS  — Max tool rounds for Narrative Director (default: 8)
///   DRIFT_HARNESS_DIAGNOSTIC_LEVEL — "minimal", "normal", or "verbose"
/// </summary>
public sealed class StrictLiveRoleplaySessionTests
{
    private const string SkippedReason = "Live LLM provider not configured. " +
        "Remove the Skip parameter from [Fact] and set DRIFT_HARNESS_BASE_URL " +
        "and DRIFT_HARNESS_LIVE_MODEL environment variables. " +
        "Example: DRIFT_HARNESS_BASE_URL=http://localhost:1234/v1 DRIFT_HARNESS_LIVE_MODEL=qwen3-35b";

    /// <summary>
    /// Run the strict live roleplay session lore consistency test through
    /// the REAL NarrativeDirectorAgent pipeline.
    ///
    /// Warning: This test makes actual LLM calls and may incur costs/tokens.
    /// It will also take significant time (30-120 seconds depending on model).
    ///
    /// Artifacts are written to the configured output directory, including
    /// run.json, trace.ndjson, evaluation.json, provider-meta.json, and
    /// session-context.json — with real provenance data from the tool loop.
    /// </summary>
    [Fact(Skip = SkippedReason)]
    public async Task StrictLiveRoleplaySession_FullRun()
    {
        var baseUrl = Environment.GetEnvironmentVariable("DRIFT_HARNESS_BASE_URL");
        var model = Environment.GetEnvironmentVariable("DRIFT_HARNESS_LIVE_MODEL")
                    ?? Environment.GetEnvironmentVariable("DRIFT_HARNESS_MODEL");
        var apiKey = Environment.GetEnvironmentVariable("DRIFT_HARNESS_API_KEY");
        var provider = Environment.GetEnvironmentVariable("DRIFT_HARNESS_LIVE_PROVIDER")
                       ?? "openai-compatible";

        // Optional strict-mode-specific options
        var ndMaxRounds = int.TryParse(
            Environment.GetEnvironmentVariable("DRIFT_HARNESS_ND_MAX_ROUNDS"),
            out var ndRounds) ? ndRounds : 8;
        var diagnosticLevel = Environment.GetEnvironmentVariable("DRIFT_HARNESS_DIAGNOSTIC_LEVEL")
                              ?? "normal";

        // Configure output directory
        var outputDir = Environment.GetEnvironmentVariable("DRIFT_HARNESS_OUTPUT_DIR")
                        ?? Path.Combine(
                            Environment.GetEnvironmentVariable("HOME") ?? "/tmp",
                            "quillforge",
                            "artifacts",
                            "roleplay-lore-strict",
                            DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss"));

        Directory.CreateDirectory(outputDir);

        // Build LLM client
        var clientOptions = new OpenAIClientOptions
        {
            Endpoint = new Uri(baseUrl!),
        };

        var effectiveApiKey = string.IsNullOrWhiteSpace(apiKey) ? "no-key" : apiKey;
        var openAiClient = new OpenAIClient(new ApiKeyCredential(effectiveApiKey), clientOptions);
        var chatClient = openAiClient.GetChatClient(model!).AsIChatClient();

        using var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Warning);
        });

        var logger = loggerFactory.CreateLogger<ChatClientCompletionService>();
        var completionService = new ChatClientCompletionService(chatClient, logger);
        var detector = new DriftDetector();
        var runner = new StrictRoleplaySessionRunner(
            completionService, detector, provider, model!,
            ndMaxRounds: ndMaxRounds,
            diagnosticLevel: diagnosticLevel);

        // Run the strict live test
        var run = await runner.RunAsync(outputDir);

        // Write a test-result summary
        var summaryPath = Path.Combine(outputDir, "test-result.txt");
        await File.WriteAllTextAsync(summaryPath,
            $"Strict Live Roleplay Session Lore Consistency Test\n" +
            $"==================================================\n" +
            $"Run ID: {run.RunId}\n" +
            $"Provider: {provider}\n" +
            $"Model: {model}\n" +
            $"Base URL: {baseUrl}\n" +
            $"ND Max Rounds: {ndMaxRounds}\n" +
            $"Passed (no drift): {run.Evaluation?.Passed}\n" +
            $"Total events: {run.TraceEvents.Count}\n" +
            $"Drift findings: {run.DriftResult.Findings.Count}\n" +
            $"Test type: STRICT (real NarrativeDirectorAgent pipeline)\n" +
            $"Files: run.json, trace.ndjson, evaluation.json, provider-meta.json, session-context.json\n" +
            $"Artifact directory: {outputDir}\n");

        // If drift found, write detailed finding info
        if (run.DriftResult.HasDrift)
        {
            var driftSummaryPath = Path.Combine(outputDir, "drift-findings.txt");
            var lines = new List<string>
            {
                "Drift Findings",
                "=============",
                "",
            };

            foreach (var finding in run.DriftResult.Findings)
            {
                lines.Add($"Forbidden fact: {finding.ForbiddenFact}");
                lines.Add($"  First appearance: turn {finding.FirstAppearanceTurn}");
                lines.Add($"  Boundary: {finding.FirstAppearanceBoundary}");
                lines.Add($"  Component: {finding.FirstAppearanceComponent}");
                lines.Add($"  Likely origin: {finding.LikelyOrigin}");
                lines.Add($"  Evidence: {finding.Evidence}");
                lines.Add("");
            }

            await File.WriteAllTextAsync(driftSummaryPath, string.Join("\n", lines));
        }

        // Assert no drift
        Assert.False(run.DriftResult.HasDrift,
            $"Lore bleed detected in real NarrativeDirectorAgent pipeline! " +
            $"{run.DriftResult.Findings.Count} forbidden fact(s) appeared. " +
            $"First contaminated boundary: {run.DriftResult.Findings[0].FirstAppearanceBoundary}. " +
            $"Details in: {outputDir}");
    }
}
