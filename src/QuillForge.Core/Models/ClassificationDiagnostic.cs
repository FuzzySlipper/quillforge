namespace QuillForge.Core.Models;

/// <summary>
/// Full diagnostic trace for a single classification decision. Records which heuristic
/// rules fired and the source file, enabling downstream tools to trace suspicious
/// facts back to their origin.
/// </summary>
public sealed record ClassificationDiagnostic
{
    /// <summary>The passage that was classified (truncated to 500 chars).</summary>
    public required string Passage { get; init; }

    /// <summary>The active subject at time of classification.</summary>
    public string? ActiveSubject { get; init; }

    /// <summary>Source file path, if available.</summary>
    public string? SourcePath { get; init; }

    /// <summary>What was classified.</summary>
    public required ActiveSubjectApplicability Applicability { get; init; }

    /// <summary>Allowed use for this classification.</summary>
    public required AllowedUse AllowedUse { get; init; }

    /// <summary>Knowledge scope determined.</summary>
    public required RoleplayKnowledgeScope Scope { get; init; }

    /// <summary>Source kind inferred from path.</summary>
    public SubjectSourceKind SourceKind { get; init; }

    /// <summary>Canon authority inferred from path.</summary>
    public CanonAuthority Authority { get; init; }

    /// <summary>Which heuristic rules fired, in order, with their arguments.</summary>
    public required IReadOnlyList<string> RulesFired { get; init; }
}
