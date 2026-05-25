using System.Text.Json.Serialization;

namespace QuillForge.RoleplayDriftHarness.Models;

/// <summary>
/// A single event in the roleplay trace, recording what happened at a specific
/// component boundary during a multi-turn scenario.
/// </summary>
public sealed record TraceEvent
{
    /// <summary>Turn number in the scenario (1-based).</summary>
    [JsonPropertyName("turn")]
    public required int Turn { get; init; }

    /// <summary>Component/boundary name, e.g. "user_turn", "query_lore", "director", "prose_writer", "visible_response".</summary>
    [JsonPropertyName("component")]
    public required string Component { get; init; }

    /// <summary>Boundary type classification.</summary>
    [JsonPropertyName("boundary")]
    public required string Boundary { get; init; }

    /// <summary>Agent name if applicable, e.g. "LibrarianAgent", "NarrativeDirector", "ProseWriter".</summary>
    [JsonPropertyName("agent")]
    public string? Agent { get; init; }

    /// <summary>Provider alias used for this interaction.</summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; init; }

    /// <summary>Model name used for this interaction.</summary>
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    /// <summary>Timestamp of the event.</summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; init; }

    /// <summary>Duration of the operation in milliseconds, if available.</summary>
    [JsonPropertyName("duration_ms")]
    public long? DurationMs { get; init; }

    /// <summary>Source file references from lore retrieval, if any.</summary>
    [JsonPropertyName("source_refs")]
    public IReadOnlyList<string>? SourceRefs { get; init; }

    /// <summary>Compact preview of the content at this boundary.</summary>
    [JsonPropertyName("preview")]
    public required string Preview { get; init; }

    /// <summary>
    /// Full content at this boundary. May be truncated for very large payloads.
    /// For UserTurn events this is the user message.
    /// For QueryLore events this is the raw lore passage text or Librarian response.
    /// For NarrativeDirector events this is the scene brief or direction text.
    /// For ProseWriter events this is the draft prose output.
    /// For VisibleResponse events this is the final assistant message.
    /// </summary>
    [JsonPropertyName("content")]
    public string? Content { get; init; }

    /// <summary>
    /// Structured knowledge payload suitable for #1661 RoleplayKnowledgePacket /
    /// StructuredSceneBrief. Records active_subject, applicability, allowed_use,
    /// and source refs for each knowledge element at this boundary.
    /// </summary>
    [JsonPropertyName("structured_payload")]
    public StructuredPayload? StructuredPayload { get; init; }
}
