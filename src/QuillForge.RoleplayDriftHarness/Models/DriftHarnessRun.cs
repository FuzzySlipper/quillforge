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

    /// <summary>
    /// Pipeline/provider errors encountered during strict live mode.
    /// When non-empty, the evaluation should report Passed=false regardless of drift findings,
    /// because a failed pipeline cannot produce a valid no-drift verdict.
    /// </summary>
    [JsonPropertyName("pipeline_errors")]
    public IReadOnlyList<PipelineError>? PipelineErrors { get; init; }

    /// <summary>
    /// True when the pipeline encountered provider/auth/tool errors that
    /// invalidate the evaluation. Derived from PipelineErrors being non-empty.
    /// </summary>
    [JsonIgnore]
    public bool HasPipelineErrors => PipelineErrors is { Count: > 0 };
}

/// <summary>
/// A single pipeline/provider error encountered during a strict live turn.
/// </summary>
public sealed record PipelineError
{
    [JsonPropertyName("turn")]
    public required int Turn { get; init; }

    [JsonPropertyName("component")]
    public required string Component { get; init; }

    [JsonPropertyName("error_type")]
    public required string ErrorType { get; init; }

    [JsonPropertyName("error_message")]
    public required string ErrorMessage { get; init; }
}
