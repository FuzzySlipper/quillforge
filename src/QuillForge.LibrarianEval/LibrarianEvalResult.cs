using QuillForge.Core.Models;

namespace QuillForge.LibrarianEval;

/// <summary>
/// Outcome of running a single LibrarianEvalQuestion through the Librarian agent.
/// Distinguishes retrieval failure from answer synthesis failure at the boundary.
/// </summary>
public sealed record LibrarianEvalResult
{
    public required string QuestionId { get; init; }
    public required string Query { get; init; }

    /// <summary>
    /// The raw JSON/text response from the LLM before parsing.
    /// </summary>
    public required string RawResponse { get; init; }

    /// <summary>
    /// The parsed LoreBundle returned by the LibrarianAgent.
    /// </summary>
    public required LoreBundle? ParsedBundle { get; init; }

    /// <summary>
    /// True if the response could not be parsed into a LoreBundle at all.
    /// This is a retrieval/formatting failure, not a synthesis failure.
    /// </summary>
    public required bool ParseFailed { get; init; }

    /// <summary>
    /// Token usage for this query.
    /// </summary>
    public required TokenUsage Usage { get; init; }

    /// <summary>
    /// Structural scores for this question.
    /// </summary>
    public required LibrarianEvalScores Scores { get; init; }

    /// <summary>
    /// Human-readable notes about what happened.
    /// </summary>
    public string? Notes { get; init; }

    /// <summary>
    /// True when the Librarian did not produce the expected source provenance or
    /// included explicitly forbidden/off-scope sources. This classifies source
    /// selection/scoping failures separately from answer-text synthesis failures.
    /// </summary>
    public bool RetrievalFailed => ParseFailed ||
        IsFailing(Scores.CorrectSourceIncluded) ||
        IsFailing(Scores.OffCharacterSourceExcluded) ||
        IsFailing(Scores.SharedFactsAccessible);

    /// <summary>
    /// True when retrieval/provenance was acceptable but the answer text still
    /// missed expected details, grafted forbidden facts, or failed ambiguity
    /// handling checks.
    /// </summary>
    public bool SynthesisFailed => !RetrievalFailed &&
        (Scores.OverallScore < 0.5 ||
            IsFailing(Scores.NoForbiddenGraft) ||
            IsFailing(Scores.AskedForClarification) ||
            IsFailing(Scores.ExpectedPassagesPresent));

    private static bool IsFailing(double? score) => score.HasValue && score.Value < 1.0;
}

/// <summary>
/// Structural scores for a single question. Each score is 0.0–1.0 or null if not applicable.
/// </summary>
public sealed record LibrarianEvalScores
{
    /// <summary>
    /// Were all expected source files included in the answer?
    /// </summary>
    public double? CorrectSourceIncluded { get; init; }

    /// <summary>
    /// Were all forbidden source files excluded from the answer?
    /// </summary>
    public double? OffCharacterSourceExcluded { get; init; }

    /// <summary>
    /// Did the answer avoid including any forbidden facts?
    /// </summary>
    public double? NoForbiddenGraft { get; init; }

    /// <summary>
    /// When the query is ambiguous, did the Librarian ask for clarification or return
    /// low confidence with empty passages?
    /// </summary>
    public double? AskedForClarification { get; init; }

    /// <summary>
    /// Did the answer include world-level shared facts when relevant?
    /// </summary>
    public double? SharedFactsAccessible { get; init; }

    /// <summary>
    /// Did the answer include expected passage substrings?
    /// </summary>
    public double? ExpectedPassagesPresent { get; init; }

    /// <summary>
    /// Overall score (average of applicable non-null scores).
    /// </summary>
    public double OverallScore { get; init; }
}
