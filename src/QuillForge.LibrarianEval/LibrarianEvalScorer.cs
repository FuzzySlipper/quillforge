using System.Globalization;
using QuillForge.Core.Models;

namespace QuillForge.LibrarianEval;

/// <summary>
/// Structural scorer for Librarian evaluation results.
/// Operates purely on the parsed LoreBundle and ground-truth question expectations.
/// No LLM judge is used.
/// </summary>
public sealed class LibrarianEvalScorer
{
    /// <summary>
    /// Scores a single result against its ground-truth question.
    /// </summary>
    public LibrarianEvalScores Score(LibrarianEvalResult result, LibrarianEvalQuestion question)
    {
        var bundle = result.ParsedBundle;
        var passages = bundle?.RelevantPassages ?? [];
        var sources = bundle?.SourceFiles ?? [];
        var allText = string.Join("\n", passages);

        var scores = new Dictionary<string, double?>();

        // 1. Correct source included
        scores["correct_source_included"] = ScoreExpectedSources(sources, question.ExpectedSources);

        // 2. Off-character source excluded
        scores["off_character_source_excluded"] = ScoreForbiddenSources(sources, question.ForbiddenSources);

        // 3. No forbidden facts grafted
        scores["no_forbidden_graft"] = ScoreForbiddenFacts(allText, question.ForbiddenFacts);

        // 4. Asked for clarification when ambiguous
        scores["asked_for_clarification"] = ScoreClarification(bundle, question.RequiresClarification, result.ParseFailed);

        // 5. Shared facts accessible
        scores["shared_facts_accessible"] = ScoreSharedFacts(sources, question.SharedFactSources, question.ExpectedSources);

        // 6. Expected passages present
        scores["expected_passages_present"] = ScoreExpectedPassages(passages, question.ExpectedPassageSubstrings);

        var applicable = scores.Values.Where(v => v.HasValue).Select(v => v!.Value).ToList();
        var overall = applicable.Count > 0 ? applicable.Average() : 0.0;

        return new LibrarianEvalScores
        {
            CorrectSourceIncluded = scores.GetValueOrDefault("correct_source_included"),
            OffCharacterSourceExcluded = scores.GetValueOrDefault("off_character_source_excluded"),
            NoForbiddenGraft = scores.GetValueOrDefault("no_forbidden_graft"),
            AskedForClarification = scores.GetValueOrDefault("asked_for_clarification"),
            SharedFactsAccessible = scores.GetValueOrDefault("shared_facts_accessible"),
            ExpectedPassagesPresent = scores.GetValueOrDefault("expected_passages_present"),
            OverallScore = overall,
        };
    }

    private static double? ScoreExpectedSources(IReadOnlyList<string> sources, IReadOnlyList<string> expected)
    {
        if (expected.Count == 0) return null;

        var sourceSet = new HashSet<string>(sources.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
        int matches = 0;
        foreach (var exp in expected)
        {
            if (sourceSet.Contains(NormalizePath(exp))) matches++;
        }
        return (double)matches / expected.Count;
    }

    private static double? ScoreForbiddenSources(IReadOnlyList<string> sources, IReadOnlyList<string> forbidden)
    {
        if (forbidden.Count == 0) return null;

        var sourceSet = new HashSet<string>(sources.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
        int violations = 0;
        foreach (var f in forbidden)
        {
            if (sourceSet.Contains(NormalizePath(f))) violations++;
        }
        return violations == 0 ? 1.0 : Math.Max(0.0, 1.0 - ((double)violations / forbidden.Count));
    }

    private static double? ScoreForbiddenFacts(string text, IReadOnlyList<string> forbiddenFacts)
    {
        if (forbiddenFacts.Count == 0) return null;

        int violations = 0;
        foreach (var fact in forbiddenFacts)
        {
            if (text.Contains(fact, StringComparison.OrdinalIgnoreCase)) violations++;
        }
        return violations == 0 ? 1.0 : Math.Max(0.0, 1.0 - ((double)violations / forbiddenFacts.Count));
    }

    private static double? ScoreClarification(LoreBundle? bundle, bool requiresClarification, bool parseFailed)
    {
        if (!requiresClarification) return null;

        if (parseFailed) return 0.5; // partial: at least didn't hallucinate, but couldn't clarify

        // Good if low confidence or empty passages (acknowledging ambiguity)
        var isLowConfidence = bundle?.Confidence == LoreConfidence.Low;
        var hasEmptyPassages = bundle?.RelevantPassages.Count == 0 || bundle?.RelevantPassages.All(string.IsNullOrWhiteSpace) == true;
        var mentionsClarify = bundle?.RelevantPassages.Any(p =>
            p.Contains("clarify", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("ambiguous", StringComparison.OrdinalIgnoreCase) ||
            p.Contains("more specific", StringComparison.OrdinalIgnoreCase)) == true;

        if (mentionsClarify) return 1.0;
        if (isLowConfidence && hasEmptyPassages) return 1.0;
        if (isLowConfidence) return 0.5;
        return 0.0;
    }

    private static double? ScoreSharedFacts(IReadOnlyList<string> sources, IReadOnlyList<string> sharedFactSources, IReadOnlyList<string> expectedSources)
    {
        if (sharedFactSources.Count == 0) return null;

        var sourceSet = new HashSet<string>(sources.Select(NormalizePath), StringComparer.OrdinalIgnoreCase);
        int matches = 0;
        foreach (var s in sharedFactSources)
        {
            if (sourceSet.Contains(NormalizePath(s))) matches++;
        }

        // If no expected sources are required, shared facts are optional bonus
        if (expectedSources.Count == 0) return matches > 0 ? 1.0 : null;

        return (double)matches / sharedFactSources.Count;
    }

    private static double? ScoreExpectedPassages(IReadOnlyList<string> passages, IReadOnlyList<string> expectedSubstrings)
    {
        if (expectedSubstrings.Count == 0) return null;

        var allText = string.Join("\n", passages);
        int matches = 0;
        foreach (var sub in expectedSubstrings)
        {
            if (allText.Contains(sub, StringComparison.OrdinalIgnoreCase)) matches++;
        }
        return (double)matches / expectedSubstrings.Count;
    }

    private static string NormalizePath(string path)
    {
        return path.Replace('\\', '/').TrimStart('/');
    }
}
