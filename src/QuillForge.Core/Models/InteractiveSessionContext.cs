namespace QuillForge.Core.Models;

/// <summary>
/// Prepared interactive-mode context derived from session runtime state and
/// related storage. This is the single seam for orchestration and tool handlers
/// that need project/file/character/story-session context.
/// </summary>
public sealed record InteractiveSessionContext
{
    public required string ActiveModeName { get; init; }
    public required string ProjectName { get; init; }
    public required string StoryStatePath { get; init; }
    public string? CurrentFile { get; init; }
    public string? Character { get; init; }
    public string? CharacterSection { get; init; }
    public string? StoryStateSummary { get; init; }
    public string? FileContext { get; init; }
    public string? WriterPendingContent { get; init; }
    public string? DirectorNotes { get; init; }
    public string? ActivePlotFile { get; init; }
    public string? ActivePlotContent { get; init; }
    public string? PlotProgressSummary { get; init; }
}
