namespace QuillForge.RoleplayDriftHarness.Models;

/// <summary>
/// Boundary types for trace events in a roleplay scenario.
/// Each represents a component boundary where lore/knowledge could be
/// retrieved, transformed, or rendered, making it a potential drift origin.
/// </summary>
public enum BoundaryType
{
    /// <summary>Driver/user input turn boundary.</summary>
    UserTurn,

    /// <summary>query_lore / query_context or Librarian result/evidence boundary.</summary>
    QueryLore,

    /// <summary>Narrative Director output or structured scene brief boundary.</summary>
    NarrativeDirector,

    /// <summary>ProseWriter / direct_scene output boundary.</summary>
    ProseWriter,

    /// <summary>Visible assistant response boundary.</summary>
    VisibleResponse,

    /// <summary>Summary/history boundary — placeholder for future extension.</summary>
    SummaryHistory,
}
