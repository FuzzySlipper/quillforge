using System.Text.Json.Serialization;
using QuillForge.Core.Models;

namespace QuillForge.RoleplayDriftHarness.Models;

/// <summary>
/// Structured payload that records how a piece of knowledge relates to the
/// active subject/character. Aligned with #1661 RoleplayKnowledgePacket types.
/// Uses the Core protocol enums for typed applicability and allowed-use.
/// </summary>
public sealed record StructuredPayload
{
    /// <summary>
    /// The active character/subject this payload pertains to.
    /// E.g. "Xavier", "Caleb", or null for shared/world-level.
    /// </summary>
    [JsonPropertyName("active_subject")]
    public string? ActiveSubject { get; init; }

    /// <summary>
    /// Applicability classification: how the lore applies to the active subject.
    /// </summary>
    [JsonPropertyName("applicability")]
    public string Applicability { get; init; } = "unknown";

    /// <summary>
    /// Allowed use: how this knowledge may be used in generation.
    /// </summary>
    [JsonPropertyName("allowed_use")]
    public string AllowedUse { get; init; } = "unknown";

    /// <summary>
    /// References to lore source files that contributed to this payload.
    /// </summary>
    [JsonPropertyName("lore_refs")]
    public IReadOnlyList<string>? LoreRefs { get; init; }

    /// <summary>
    /// The component that produced this payload, e.g. "query_lore", "scene_brief", "direct_scene".
    /// </summary>
    [JsonPropertyName("source_component")]
    public string? SourceComponent { get; init; }

    /// <summary>
    /// Optional reference to a Core RoleplayKnowledgePacket for consumers
    /// that can handle the full typed protocol format.
    /// </summary>
    [JsonPropertyName("knowledge_packet")]
    public RoleplayKnowledgePacket? KnowledgePacket { get; init; }
}
