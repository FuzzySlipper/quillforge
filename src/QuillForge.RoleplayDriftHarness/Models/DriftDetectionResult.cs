using System.Text.Json.Serialization;

namespace QuillForge.RoleplayDriftHarness.Models;

/// <summary>
/// Result of a full drift detection run against a scenario's trace events.
/// </summary>
public sealed record DriftDetectionResult
{
    /// <summary>True if any forbidden detail was found in the trace.</summary>
    [JsonPropertyName("has_drift")]
    public required bool HasDrift { get; init; }

    /// <summary>Individual findings for each forbidden detail detected.</summary>
    [JsonPropertyName("findings")]
    public IReadOnlyList<DriftFinding> Findings { get; init; } = [];
}

/// <summary>
/// A single drift finding — one forbidden detail appearance in a trace.
/// </summary>
public sealed record DriftFinding
{
    /// <summary>The forbidden fact/detail that appeared.</summary>
    [JsonPropertyName("forbidden_fact")]
    public required string ForbiddenFact { get; init; }

    /// <summary>Turn number where the fact first appeared.</summary>
    [JsonPropertyName("first_appearance_turn")]
    public required int FirstAppearanceTurn { get; init; }

    /// <summary>Boundary type where the fact first appeared.</summary>
    [JsonPropertyName("first_appearance_boundary")]
    public required string FirstAppearanceBoundary { get; init; }

    /// <summary>Component name where the fact first appeared.</summary>
    [JsonPropertyName("first_appearance_component")]
    public required string FirstAppearanceComponent { get; init; }

    /// <summary>
    /// Classified likely origin of the forbidden fact:
    /// "retrieval" — introduced by query_lore/query_context or Librarian result.
    /// "director_synthesis" — introduced by Narrative Director synthesis.
    /// "prose_misuse" — introduced by ProseWriter misuse of context.
    /// "visible_response" — appeared in the visible assistant response.
    /// "summary_history" — appeared in summary or history boundary.
    /// "provider_timing" — appeared only due to provider/timing artifact.
    /// "uncertain" — could not determine origin.
    /// </summary>
    [JsonPropertyName("likely_origin")]
    public required string LikelyOrigin { get; init; }

    /// <summary>Evidence or context about how the drift was detected.</summary>
    [JsonPropertyName("evidence")]
    public string? Evidence { get; init; }
}
