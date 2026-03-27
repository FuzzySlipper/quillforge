using System.Text.Json;

namespace QuillForge.Core.Models;

/// <summary>
/// A tool definition that the model can invoke.
/// </summary>
public sealed record ToolDefinition(string Name, string Description, JsonElement InputSchema);

/// <summary>
/// Request to an LLM completion service. Pure data — no provider SDK types.
/// </summary>
public sealed record CompletionRequest
{
    public required string Model { get; init; }
    public required int MaxTokens { get; init; }
    public string? SystemPrompt { get; init; }
    public required IReadOnlyList<CompletionMessage> Messages { get; init; }
    public IReadOnlyList<ToolDefinition>? Tools { get; init; }
    public double? Temperature { get; init; }

    /// <summary>
    /// When true, signals that the system prompt should be cached for repeated use
    /// (Anthropic prompt caching). This avoids re-charging input tokens when the
    /// same large system prompt (e.g. lore corpus) is sent across multiple requests.
    /// Non-Anthropic providers silently ignore this flag.
    /// </summary>
    public bool CacheSystemPrompt { get; init; }
}

/// <summary>
/// A message in the completion request's conversation history.
/// </summary>
public sealed record CompletionMessage(string Role, MessageContent Content);

/// <summary>
/// Response from an LLM completion service.
/// </summary>
public sealed record CompletionResponse
{
    public required MessageContent Content { get; init; }
    public required string StopReason { get; init; }
    public required TokenUsage Usage { get; init; }
    public string? Reasoning { get; init; }
}

/// <summary>
/// Token usage for a single completion call.
/// </summary>
public sealed record TokenUsage(int InputTokens, int OutputTokens)
{
    public int TotalTokens => InputTokens + OutputTokens;
}
