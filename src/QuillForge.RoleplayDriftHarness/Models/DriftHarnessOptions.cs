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
}
