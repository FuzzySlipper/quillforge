using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using QuillForge.Providers.Adapters;
using QuillForge.RoleplayDriftHarness.Runners;
using Xunit;
namespace QuillForge.RoleplayDriftHarness.Tests;

/// <summary>
/// Live LLM-backed Xavier/Caleb lore consistency test.
///
/// This test requires a configured LLM provider to run. It is CI-safe
/// (skipped when provider credentials are absent) — gated by the
/// <c>[Fact(Skip = "...")]</c> attribute.
///
/// To run this test, remove the Skip parameter from the [Fact] attribute
/// and set the following environment variables:
///   DRIFT_HARNESS_BASE_URL   — LLM provider endpoint (e.g. http://localhost:1234/v1)
///   DRIFT_HARNESS_MODEL      — Model name (e.g. gpt-4o, claude-sonnet-4-20250514)
///   DRIFT_HARNESS_API_KEY    — Optional API key
///   DRIFT_HARNESS_LIVE_PROVIDER — Provider alias (optional, default: openai-compatible)
///   DRIFT_HARNESS_OUTPUT_DIR — Output directory for diagnostic artifacts (optional)
///
/// When provider prerequisites are available, the test:
/// 1. Creates an actual LLM client from configured provider settings.
/// 2. Runs Xavier-facing probe prompts designed to trigger Caleb lore contamination.
/// 3. Captures diagnostics at each component boundary (query_lore, narrative_director,
///    prose_writer, visible_response).
/// 4. Detects drift using the same DriftDetector as deterministic tests.
/// 5. Writes artifacts to the configured output path.
/// 6. Asserts that lore bleed (Caleb facts in Xavier context) is absent.
/// </summary>
public sealed class LiveXavierCalebLoreConsistencyTests
{
    private const string SkippedReason = "Live LLM provider not configured. " +
        "Remove the Skip parameter from [Fact] and set DRIFT_HARNESS_BASE_URL " +
        "and DRIFT_HARNESS_MODEL environment variables. " +
        "Example: DRIFT_HARNESS_BASE_URL=http://localhost:1234/v1 DRIFT_HARNESS_MODEL=qwen3-35b";

    /// <summary>
    /// Run the full live LLM-backed Xavier/Caleb lore consistency test.
    /// Skipped when provider credentials are unavailable.
    /// Artifacts are written to the configured output directory.
    /// </summary>
    [Fact(Skip = SkippedReason)]
    public async Task LiveXavierCalebLoreConsistency_FullRun()
    {
        var baseUrl = Environment.GetEnvironmentVariable("DRIFT_HARNESS_BASE_URL");
        var model = Environment.GetEnvironmentVariable("DRIFT_HARNESS_LIVE_MODEL")
                    ?? Environment.GetEnvironmentVariable("DRIFT_HARNESS_MODEL");
        var apiKey = Environment.GetEnvironmentVariable("DRIFT_HARNESS_API_KEY");
        var provider = Environment.GetEnvironmentVariable("DRIFT_HARNESS_LIVE_PROVIDER")
                       ?? "openai-compatible";

        // Configure output directory
        var outputDir = Environment.GetEnvironmentVariable("DRIFT_HARNESS_OUTPUT_DIR")
                        ?? Path.Combine(
                            Environment.GetEnvironmentVariable("HOME") ?? "/tmp",
                            "quillforge",
                            "artifacts",
                            "roleplay-lore-live",
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
        var runner = new LiveLoreConsistencyRunner(completionService, detector, provider, model!);

        // Run the live test
        var run = await runner.RunAsync(outputDir);

        // Write summary
        var summaryPath = Path.Combine(outputDir, "test-result.txt");
        await File.WriteAllTextAsync(summaryPath,
            $"Live LLM Lore Consistency Test\n" +
            $"==============================\n" +
            $"Run ID: {run.RunId}\n" +
            $"Provider: {provider}\n" +
            $"Model: {model}\n" +
            $"Base URL: {baseUrl}\n" +
            $"Passed (no drift): {run.Evaluation?.Passed}\n" +
            $"Events: {run.TraceEvents.Count}\n" +
            $"Drift Findings: {run.DriftResult.Findings.Count}\n" +
            $"Notes: {run.Evaluation?.Notes ?? "(none)"}\n" +
            $"Output: {outputDir}\n");

        // If drift was detected, write a detailed findings report
        if (run.DriftResult.HasDrift)
        {
            var findingsPath = Path.Combine(outputDir, "bleed-findings.md");
            using var writer = new StreamWriter(findingsPath);
            await writer.WriteLineAsync("# Lore Bleed Findings Report");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync($"Provider: {provider} | Model: {model}");
            await writer.WriteLineAsync($"Run ID: {run.RunId}");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("## Detected Forbidden Facts");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("| Fact | Turn | Boundary | Component | Likely Origin |");
            await writer.WriteLineAsync("|------|------|----------|-----------|---------------|");
            foreach (var f in run.DriftResult.Findings)
            {
                await writer.WriteLineAsync($"| {f.ForbiddenFact} | {f.FirstAppearanceTurn} | {f.FirstAppearanceBoundary} | {f.FirstAppearanceComponent} | {f.LikelyOrigin} |");
            }
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("## Follow-up Needed");
            await writer.WriteLineAsync();
            await writer.WriteLineAsync("Lore bleed was detected in the live LLM roleplay pipeline. ");
            await writer.WriteLineAsync("The following actions are recommended:");
            await writer.WriteLineAsync("1. Review the trace.ndjson for the exact context where bleed occurred.");
            await writer.WriteLineAsync("2. Check if the provider/model choice or prompt structure contributed.");
            await writer.WriteLineAsync("3. Consider additional lore boundary enforcement at the contaminated boundary.");
            await writer.WriteLineAsync($"4. Test with a different model/provider to isolate whether this is model-specific.");
        }

        // Assert — we expect NO drift for a well-functioning pipeline.
        // If drift is found, the test fails, and the detailed artifacts explain why.
        Assert.False(run.DriftResult.HasDrift,
            $"LORE BLEED DETECTED: {run.DriftResult.Findings.Count} forbidden fact(s) appeared in live LLM output. " +
            $"Provider={provider}, Model={model}. " +
            $"See artifacts at: {outputDir}");
    }
}
