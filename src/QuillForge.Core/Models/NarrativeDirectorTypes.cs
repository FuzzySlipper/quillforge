namespace QuillForge.Core.Models;

/// <summary>
/// Request to direct the next step of an interactive scene.
/// </summary>
public sealed record NarrativeDirectionRequest
{
    public required string UserMessage { get; init; }
}

/// <summary>
/// Result of scene direction for an interactive turn.
/// </summary>
public sealed record NarrativeDirectionResult
{
    public required string ResponseText { get; init; }
    public required int ToolRoundsUsed { get; init; }
}

/// <summary>
/// Request to generate a reusable plot arc document.
/// </summary>
public sealed record PlotGenerationRequest
{
    public string? Prompt { get; init; }
}

/// <summary>
/// Result of plot arc generation.
/// </summary>
public sealed record PlotGenerationResult
{
    public required string Markdown { get; init; }
    public required int ToolRoundsUsed { get; init; }
}
