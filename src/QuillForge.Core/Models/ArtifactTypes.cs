namespace QuillForge.Core.Models;

public enum ArtifactFormat
{
    Newspaper,
    Letter,
    Texts,
    Social,
    Journal,
    Report,
    Wanted,
    Prose,
}

public sealed record Artifact
{
    public required ArtifactFormat Format { get; init; }
    public required string Content { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? FileName { get; init; }
}

public sealed record ArtifactSummary
{
    public required string Name { get; init; }
    public required string Format { get; init; }
    public required string Path { get; init; }
    public required int Size { get; init; }
}
