namespace QuillForge.Core.Models;

/// <summary>
/// Configuration for an agent's tool loop execution.
/// </summary>
public sealed record AgentConfig
{
    public required string Model { get; init; }
    public required int MaxTokens { get; init; }
    public required string SystemPrompt { get; init; }
    public int MaxToolRounds { get; init; } = 10;
    public double? Temperature { get; init; }
}

/// <summary>
/// The final response from an agent after the tool loop completes.
/// </summary>
public sealed record AgentResponse
{
    public required MessageContent Content { get; init; }
    public required string StopReason { get; init; }
    public required TokenUsage Usage { get; init; }
    public required int ToolRoundsUsed { get; init; }
}

/// <summary>
/// Context passed to tool handlers during execution.
/// </summary>
public sealed record AgentContext
{
    public required Guid SessionId { get; init; }
    public required string ActiveMode { get; init; }
}
