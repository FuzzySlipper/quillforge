using System.Text.Json.Serialization;

namespace QuillForge.RoleplayDriftHarness.Models;

/// <summary>
/// A single scripted turn in a roleplay drift scenario.
/// Each turn defines the user message and the expected component boundary outputs.
/// </summary>
public sealed record ScriptedTurn
{
    /// <summary>Turn number (1-based).</summary>
    [JsonPropertyName("turn_number")]
    public required int TurnNumber { get; init; }

    /// <summary>User/player message that drives this turn.</summary>
    [JsonPropertyName("user_message")]
    public required string UserMessage { get; init; }

    /// <summary>Sequence of component boundary outputs for this turn.</summary>
    [JsonPropertyName("boundaries")]
    public required IReadOnlyList<ScriptedBoundaryOutput> Boundaries { get; init; }
}
