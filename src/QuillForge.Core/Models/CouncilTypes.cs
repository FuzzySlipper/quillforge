namespace QuillForge.Core.Models;

public sealed record CouncilMember
{
    public required string Name { get; init; }
    public string? Model { get; init; }
    public string? ProviderAlias { get; init; }
    public string? BaseUrl { get; init; }
    public required string SystemPrompt { get; init; }
}

public sealed record CouncilResult
{
    public required string Query { get; init; }
    public required IReadOnlyList<CouncilMemberResponse> Members { get; init; }
}

public sealed record CouncilMemberResponse
{
    public required string Name { get; init; }
    public required string Model { get; init; }
    public required string ProviderAlias { get; init; }
    public required string Content { get; init; }
    public string? Error { get; init; }
}
