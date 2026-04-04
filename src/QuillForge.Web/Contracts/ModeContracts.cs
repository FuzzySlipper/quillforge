namespace QuillForge.Web.Contracts;

public sealed record ModeResponse
{
    public Guid? SessionId { get; init; }
    public required string Mode { get; init; }
    public string? Project { get; init; }
    public string? File { get; init; }
    public string? Character { get; init; }
    public string? PendingContent { get; init; }
}

public sealed record ModeSetRequest
{
    public string Mode { get; init; } = "general";
    public string? Project { get; init; }
    public string? File { get; init; }
    public string? Character { get; init; }
    public Guid? SessionId { get; init; }
}
