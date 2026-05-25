using System.Text.Json.Serialization;

namespace QuillForge.RoleplayDriftHarness.Models;

/// <summary>
/// Output at a single component boundary during a scripted turn.
/// This is what the harness uses to simulate a component's output deterministically.
/// </summary>
public sealed record ScriptedBoundaryOutput
{
    /// <summary>Boundary type.</summary>
    [JsonPropertyName("boundary")]
    public required string Boundary { get; init; }

    /// <summary>Component name.</summary>
    [JsonPropertyName("component")]
    public string Component { get; init; } = "";

    /// <summary>The content produced at this boundary.</summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }

    /// <summary>Source file references, if any.</summary>
    [JsonPropertyName("source_refs")]
    public IReadOnlyList<string>? SourceRefs { get; init; }

    /// <summary>Optional structured payload metadata.</summary>
    [JsonPropertyName("payload")]
    public StructuredPayload? Payload { get; init; }
}
