namespace QuillForge.Core.Models;

/// <summary>
/// Request to generate prose for a scene.
/// </summary>
public sealed record ProseRequest
{
    public required string SceneDescription { get; init; }
    public required string StoryContext { get; init; }
    public string? ToneNotes { get; init; }
}

/// <summary>
/// Result of prose generation.
/// </summary>
public sealed record ProseResult
{
    public required string GeneratedText { get; init; }
    public required IReadOnlyList<string> LoreQueriesMade { get; init; }
    public required int WordCount { get; init; }
}
