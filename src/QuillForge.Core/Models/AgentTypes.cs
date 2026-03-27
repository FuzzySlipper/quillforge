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

    /// <summary>
    /// When true, the system prompt will be marked for prompt caching on providers
    /// that support it (Anthropic). This is useful for agents with large, stable
    /// system prompts (e.g. the Librarian's lore corpus) to reduce input token costs.
    /// </summary>
    public bool CacheSystemPrompt { get; init; }
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
    public ResponseType ResponseType { get; init; } = ResponseType.Discussion;
    public IReadOnlyList<string>? SuggestedNext { get; init; }
}

/// <summary>
/// Context passed to tool handlers during execution.
/// </summary>
public sealed record AgentContext
{
    public required Guid SessionId { get; init; }
    public required string ActiveMode { get; init; }
}
