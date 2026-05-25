using System.Text.Json.Serialization;

namespace QuillForge.RoleplayDriftHarness.Models;

/// <summary>
/// Structured payload that records how a piece of knowledge relates to the
/// active subject/character. Designed to be forward-compatible with #1661
/// RoleplayKnowledgePacket and StructuredSceneBrief formats.
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
    /// "active_character" — directly about the active character (inline lore).
    /// "shared_world" — shared/background world knowledge.
    /// "off_character" — about a different character entirely.
    /// "unknown" — could not be determined.
    /// </summary>
    [JsonPropertyName("applicability")]
    public string Applicability { get; init; } = "unknown";

    /// <summary>
    /// Allowed use: how this knowledge may be used in generation.
    /// "inline" — may be incorporated directly into the active subject's narrative.
    /// "context" — available for narrative context but not inline character specifics.
    /// "excluded" — must not be used for this character.
    /// "unknown" — not yet classified.
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
}
