using System.Text.Json.Serialization;

namespace QuillForge.RoleplayDriftHarness.Models;

/// <summary>
/// Full result of a drift harness run, including scenario metadata,
/// trace events, drift detection results, and run-level evaluation.
/// </summary>
public sealed record DriftHarnessRun
{
    [JsonPropertyName("run_id")]
    public required string RunId { get; init; }

    [JsonPropertyName("scenario_name")]
    public required string ScenarioName { get; init; }

    [JsonPropertyName("active_character")]
    public required string ActiveCharacter { get; init; }

    [JsonPropertyName("off_character")]
    public required string OffCharacter { get; init; }

    [JsonPropertyName("turns")]
    public required IReadOnlyList<ScriptedTurn> Turns { get; init; }

    [JsonPropertyName("forbidden_details")]
    public required IReadOnlyList<string> ForbiddenDetails { get; init; }

    [JsonPropertyName("started_at")]
    public required DateTimeOffset StartedAt { get; init; }

    [JsonPropertyName("completed_at")]
    public DateTimeOffset? CompletedAt { get; init; }

    [JsonPropertyName("trace_events")]
    public required IReadOnlyList<TraceEvent> TraceEvents { get; init; }

    [JsonPropertyName("drift_result")]
    public required DriftDetectionResult DriftResult { get; init; }

    [JsonPropertyName("evaluation")]
    public DriftRunEvaluation? Evaluation { get; init; }
}

/// <summary>
/// Top-level evaluation summary for a drift harness run.
/// </summary>
public sealed record DriftRunEvaluation
{
    [JsonPropertyName("passed")]
    public required bool Passed { get; init; }

    [JsonPropertyName("total_turns")]
    public required int TotalTurns { get; init; }

    [JsonPropertyName("total_events")]
    public required int TotalEvents { get; init; }

    [JsonPropertyName("drift_count")]
    public required int DriftCount { get; init; }

    [JsonPropertyName("expected_drift_count")]
    public int? ExpectedDriftCount { get; init; }

    [JsonPropertyName("origins")]
    public IReadOnlyDictionary<string, int>? Origins { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }
}
