namespace QuillForge.LibrarianEval;

/// <summary>
/// A single evaluation question with ground-truth expectations for structural scoring.
/// </summary>
public sealed record LibrarianEvalQuestion
{
    public required string Id { get; init; }
    public required string Query { get; init; }

    /// <summary>
    /// Source files that should be referenced in the answer.
    /// </summary>
    public IReadOnlyList<string> ExpectedSources { get; init; } = [];

    /// <summary>
    /// Source files that must NOT be referenced (off-character / off-scope).
    /// </summary>
    public IReadOnlyList<string> ForbiddenSources { get; init; } = [];

    /// <summary>
    /// Facts or phrases that must NOT appear in the answer (forbidden grafts).
    /// </summary>
    public IReadOnlyList<string> ForbiddenFacts { get; init; } = [];

    /// <summary>
    /// When true, the query is genuinely ambiguous and the Librarian should ask for
    /// clarification or return low confidence with empty passages.
    /// </summary>
    public bool RequiresClarification { get; init; }

    /// <summary>
    /// World-level shared facts that should remain accessible even when character-specific
    /// lore is filtered. These are sources the answer may legitimately include.
    /// </summary>
    public IReadOnlyList<string> SharedFactSources { get; init; } = [];

    /// <summary>
    /// Expected passage substrings that should appear in relevant_passages.
    /// </summary>
    public IReadOnlyList<string> ExpectedPassageSubstrings { get; init; } = [];

    /// <summary>
    /// Human-readable description of what this question is testing.
    /// </summary>
    public string? Notes { get; init; }
}
