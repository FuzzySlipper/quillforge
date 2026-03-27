namespace QuillForge.Core.Models;

/// <summary>
/// Categorizes agent responses so the frontend can render them appropriately.
/// </summary>
public enum ResponseType
{
    Discussion,
    Prose,
    ProsePending,
    LoreAnswer,
    Confirmation,
    Council,
    Artifact,
}
