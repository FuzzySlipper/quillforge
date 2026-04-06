using System.Text.Json;

namespace QuillForge.Core.Models;

/// <summary>
/// Why the model stopped generating. Internal enum — provider adapters translate
/// provider-specific strings at the boundary; web/persistence layers convert back
/// to wire-format strings at their respective boundaries.
/// </summary>
public enum StopReason
{
    /// <summary>The model finished naturally.</summary>
    EndTurn,

    /// <summary>The model hit the max_tokens limit.</summary>
    MaxTokens,

    /// <summary>The model wants to invoke one or more tools.</summary>
    ToolUse,

    /// <summary>A content filter blocked the response.</summary>
    ContentFilter,

    /// <summary>QuillForge-internal: the tool loop exhausted its max rounds budget.</summary>
    MaxRounds,

    /// <summary>QuillForge-internal: a recovery attempt failed.</summary>
    Error,
}

public static class StopReasonExtensions
{
    /// <summary>
    /// Converts to the snake_case wire format used by SSE, DTOs, and persisted metadata.
    /// </summary>
    public static string ToWireString(this StopReason reason) => reason switch
    {
        StopReason.EndTurn => "end_turn",
        StopReason.MaxTokens => "max_tokens",
        StopReason.ToolUse => "tool_use",
        StopReason.ContentFilter => "content_filter",
        StopReason.MaxRounds => "max_rounds",
        StopReason.Error => "error",
        _ => "end_turn",
    };

    /// <summary>
    /// Parses a provider/wire string to a StopReason. Case-insensitive.
    /// Returns EndTurn for unrecognised values.
    /// </summary>
    public static StopReason ParseStopReason(string? value) => value?.ToLowerInvariant() switch
    {
        "end_turn" or "stop" => StopReason.EndTurn,
        "max_tokens" or "length" => StopReason.MaxTokens,
        "tool_use" or "tool_calls" => StopReason.ToolUse,
        "content_filter" => StopReason.ContentFilter,
        "max_rounds" => StopReason.MaxRounds,
        "error" => StopReason.Error,
        _ => StopReason.EndTurn,
    };
}

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
public sealed record CompletionMessage(string Role, MessageContent Content)
{
    /// <summary>
    /// QuillForge-owned replay envelope for adapter-specific round-tripping.
    /// Used by ReasoningCompletionService to preserve reasoning content and tool calls
    /// across tool loop rounds. Ignored by adapters that do not need replay data.
    /// </summary>
    public ProviderReplayEnvelope? ProviderReplay { get; init; }
}

/// <summary>
/// Response from an LLM completion service.
/// </summary>
public sealed record CompletionResponse
{
    public required MessageContent Content { get; init; }
    public required StopReason StopReason { get; init; }
    public required TokenUsage Usage { get; init; }
    /// <summary>
     /// Reasoning/thinking content from the model (for UI display).
     /// </summary>
    public string? Reasoning { get; init; }
    /// <summary>
    /// QuillForge-owned replay envelope for adapter-specific round-tripping.
     /// </summary>
    public ProviderReplayEnvelope? ProviderReplay { get; init; }
}

/// <summary>
/// Token usage for a single completion call.
/// </summary>
public sealed record TokenUsage(int InputTokens, int OutputTokens)
{
    public int TotalTokens => InputTokens + OutputTokens;
}
