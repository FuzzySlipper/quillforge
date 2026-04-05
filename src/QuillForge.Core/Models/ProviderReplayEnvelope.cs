namespace QuillForge.Core.Models;

/// <summary>
/// Opaque replay payload that may cross Core so a provider adapter can preserve
/// provider-specific response details across tool-loop rounds without leaking SDK
/// types or raw JSON container types into the rest of the application.
/// </summary>
public abstract record ProviderReplayEnvelope;

/// <summary>
/// Replay envelope for reasoning-style chat providers that need to preserve
/// assistant text, reasoning content, and tool calls across a tool round-trip.
/// </summary>
public sealed record ReasoningReplayEnvelope(
    string? Content,
    string? ReasoningContent,
    IReadOnlyList<ReasoningReplayToolCall> ToolCalls) : ProviderReplayEnvelope;

public sealed record ReasoningReplayToolCall(
    string Id,
    string Name,
    string ArgumentsJson);
