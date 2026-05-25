using System.Text.Json.Serialization;

namespace QuillForge.RoleplayDriftHarness.Models;

/// <summary>
/// A complete scripted roleplay scenario used for drift evaluation.
/// Defines the characters, forbidden lore, shared evidence, and scripted turns.
/// </summary>
public sealed record RoleplayScenario
{
    /// <summary>Unique scenario name for identification.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>The active character (subject of the roleplay).</summary>
    [JsonPropertyName("active_character")]
    public required string ActiveCharacter { get; init; }

    /// <summary>The off-character whose details must not leak.</summary>
    [JsonPropertyName("off_character")]
    public required string OffCharacter { get; init; }

    /// <summary>Forbidden details that must never appear when roleplaying the active character.</summary>
    [JsonPropertyName("forbidden_details")]
    public required IReadOnlyList<string> ForbiddenDetails { get; init; }

    /// <summary>Scripted turns in the scenario.</summary>
    [JsonPropertyName("turns")]
    public required IReadOnlyList<ScriptedTurn> Turns { get; init; }

    /// <summary>
    /// Generic/shared body-tech evidence that may appear in lore but is not
    /// specific to the active character. Used to test that shared lore is
    /// classified as background/unknown rather than asserted about the active subject.
    /// </summary>
    [JsonPropertyName("shared_body_tech_evidence")]
    public IReadOnlyList<string>? SharedBodyTechEvidence { get; init; }
}
