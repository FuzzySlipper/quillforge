using QuillForge.Core.Models;

namespace QuillForge.Web.Contracts;

/// <summary>
/// Response from GET /api/forge/projects/{name}/status.
/// This is the single source of truth for the forge status shape.
/// The frontend reads these fields in commands.ts — keep them in sync.
/// </summary>
public sealed record ForgeStatusResponse
{
    public required string ProjectName { get; init; }
    public required string Stage { get; init; }
    public required int ChapterCount { get; init; }
    public required bool Paused { get; init; }
    public required IReadOnlyDictionary<string, ForgeChapterStatusDto> Chapters { get; init; }
    public required ForgeStats Stats { get; init; }
}

public sealed record ForgeChapterStatusDto
{
    public required string State { get; init; }
    public required int RevisionCount { get; init; }
    public required int WordCount { get; init; }
}
