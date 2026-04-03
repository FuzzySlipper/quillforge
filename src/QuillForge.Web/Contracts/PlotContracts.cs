namespace QuillForge.Web.Contracts;

public sealed record PlotFileDto
{
    public required string Name { get; init; }
    public required string Path { get; init; }
    public required int Tokens { get; init; }
    public required int Size { get; init; }
}

public sealed record PlotListResponse
{
    public required IReadOnlyList<PlotFileDto> Files { get; init; }
    public string? ActivePlotFile { get; init; }
    public Guid? SessionId { get; init; }
    public PlotProgressDto? PlotProgress { get; init; }
}

public sealed record PlotReadResponse
{
    public required string Name { get; init; }
    public required string Content { get; init; }
    public required int Tokens { get; init; }
}

public sealed record PlotGenerateResponse
{
    public required string Name { get; init; }
    public required string Content { get; init; }
    public required int ToolRoundsUsed { get; init; }
    public Guid? SessionId { get; init; }
}

public sealed record PlotLoadRequest
{
    public required string Name { get; init; }
    public required Guid SessionId { get; init; }
}

public sealed record PlotGenerateRequest
{
    public Guid? SessionId { get; init; }
    public string? Prompt { get; init; }
}

public sealed record PlotUnloadRequest
{
    public required Guid SessionId { get; init; }
}

public sealed record PlotMutationResponse
{
    public required Guid SessionId { get; init; }
    public string? ActivePlotFile { get; init; }
}

public sealed record PlotProgressDto
{
    public string? CurrentBeat { get; init; }
    public IReadOnlyList<string> CompletedBeats { get; init; } = [];
    public IReadOnlyList<string> Deviations { get; init; } = [];
}
