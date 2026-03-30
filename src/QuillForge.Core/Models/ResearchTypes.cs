namespace QuillForge.Core.Models;

public sealed record ResearchTopic
{
    public required string Topic { get; init; }
    public string? Focus { get; init; }
}

public sealed record ResearchAgentResult
{
    public required string Topic { get; init; }
    public required string Summary { get; init; }
    public required IReadOnlyList<string> Sources { get; init; }
    public required string FilePath { get; init; }
    public string? Error { get; init; }
}

public sealed record ResearchPoolResult
{
    public required string Project { get; init; }
    public required IReadOnlyList<ResearchAgentResult> Results { get; init; }
}
