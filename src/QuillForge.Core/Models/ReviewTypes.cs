namespace QuillForge.Core.Models;

/// <summary>
/// Scores and feedback from a chapter review.
/// </summary>
public sealed record ReviewResult
{
    public required double Continuity { get; init; }
    public required double BriefAdherence { get; init; }
    public required double VoiceConsistency { get; init; }
    public required double Quality { get; init; }
    public required double Overall { get; init; }
    public required string Feedback { get; init; }
    public required bool Passed { get; init; }

    /// <summary>
    /// Small details extracted from the chapter for run-specific lore:
    /// character descriptions, new names, objects, relationship changes, timeline specifics, etc.
    /// Empty if none were extracted or the chapter didn't pass.
    /// </summary>
    public IReadOnlyList<string> ExtractedDetails { get; init; } = [];
}
