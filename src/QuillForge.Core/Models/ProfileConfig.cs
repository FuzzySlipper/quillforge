namespace QuillForge.Core.Models;

/// <summary>
/// Durable reusable profile bundle. A profile captures reusable author choices
/// that may seed many sessions.
/// </summary>
public sealed record ProfileConfig
{
    // Legacy migration-only field. Live routing is app-owned by mode, but we
    // continue reading old profile conductor values during the transition.
    public string? Conductor { get; set; }
    public string LoreSet { get; set; } = "default";
    public string NarrativeRules { get; set; } = "default";
    public string WritingStyle { get; set; } = "default";
    public string LibrarianPrompt { get; set; } = "default";
    public RoleplayConfig Roleplay { get; set; } = new();
}
