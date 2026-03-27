namespace QuillForge.Core.Models;

public sealed record CharacterCard
{
    public required string Name { get; init; }
    public string? Portrait { get; init; }
    public string? Personality { get; init; }
    public string? Description { get; init; }
    public string? Scenario { get; init; }
    public string? Greeting { get; init; }
    public string? FileName { get; init; }
}
