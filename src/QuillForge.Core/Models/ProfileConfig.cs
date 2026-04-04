namespace QuillForge.Core.Models;

/// <summary>
/// Durable reusable profile bundle. A profile captures reusable author choices
/// that may seed many sessions.
/// </summary>
public sealed record ProfileConfig
{
    public string Conductor { get; set; } = "default";
    public string LoreSet { get; set; } = "default";
    public string NarrativeRules { get; set; } = "default";
    public string WritingStyle { get; set; } = "default";
    public RoleplayConfig Roleplay { get; set; } = new();
}
