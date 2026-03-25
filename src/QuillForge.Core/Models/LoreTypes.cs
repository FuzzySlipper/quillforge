namespace QuillForge.Core.Models;

/// <summary>
/// Results from a lore query, including relevant passages and provenance.
/// </summary>
public sealed record LoreBundle
{
    public required IReadOnlyList<string> RelevantPassages { get; init; }
    public required IReadOnlyList<string> SourceFiles { get; init; }
    public LoreConfidence Confidence { get; init; } = LoreConfidence.High;
}

public enum LoreConfidence
{
    High,
    Medium,
    Low
}
