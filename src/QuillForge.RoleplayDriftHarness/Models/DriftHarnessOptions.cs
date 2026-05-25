using System.Text.Json.Serialization;

namespace QuillForge.RoleplayDriftHarness.Models;

/// <summary>
/// Options for the drift harness console application.
/// </summary>
public sealed record DriftHarnessOptions
{
    /// <summary>Scenario name to run. Defaults to "xavier-caleb".</summary>
    [JsonPropertyName("scenario")]
    public string ScenarioName { get; init; } = "xavier-caleb";

    /// <summary>Output directory for artifacts.</summary>
    public required string OutputDir { get; init; }

    /// <summary>Optional OpenAI-compatible base URL for a live LLM evaluator/extension seam.</summary>
    [JsonPropertyName("base_url")]
    public string? BaseUrl { get; init; }

    /// <summary>Optional model name for the live LLM evaluator.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>Optional API key for the live LLM evaluator.</summary>
    [JsonPropertyName("api_key")]
    public string? ApiKey { get; init; }

    // ── Live LLM roleplay mode ──

    /// <summary>
    /// If true, run actual LLM-backed Xavier/Caleb lore consistency test
    /// instead of deterministic scripted scenarios.
    /// </summary>
    [JsonPropertyName("live")]
    public bool Live { get; init; }

    /// <summary>
    /// Provider alias for live LLM calls.
    /// E.g. "openai", "anthropic", "openrouter".
    /// </summary>
    [JsonPropertyName("live_provider")]
    public string? LiveProvider { get; init; }

    /// <summary>
    /// Model name for live LLM calls.
    /// E.g. "gpt-4o", "claude-sonnet-4-20250514", "qwen3-35b".
    /// When not set, falls back to the base --model value.
    /// </summary>
    [JsonPropertyName("live_model")]
    public string? LiveModel { get; init; }
}
