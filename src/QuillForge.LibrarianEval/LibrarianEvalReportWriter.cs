using System.Globalization;
using System.Text.Json;

namespace QuillForge.LibrarianEval;

/// <summary>
/// Writes evaluation artifacts to disk:
/// - questions.jsonl
/// - retrieval-trace.jsonl
/// - answers.jsonl
/// - evaluation.json
/// - summary.md
/// </summary>
public sealed class LibrarianEvalReportWriter
{
    private static readonly JsonSerializerOptions s_jsonlOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public void WriteAll(
        string outputDir,
        IReadOnlyList<LibrarianEvalQuestion> questions,
        IReadOnlyList<LibrarianEvalResult> results)
    {
        Directory.CreateDirectory(outputDir);

        WriteQuestionsJsonl(outputDir, questions);
        WriteRetrievalTraceJsonl(outputDir, results);
        WriteAnswersJsonl(outputDir, results);
        WriteEvaluationJson(outputDir, questions, results);
        WriteSummaryMd(outputDir, questions, results);
    }

    private static void WriteQuestionsJsonl(string outputDir, IReadOnlyList<LibrarianEvalQuestion> questions)
    {
        var path = Path.Combine(outputDir, "questions.jsonl");
        using var writer = new StreamWriter(path);
        foreach (var q in questions)
        {
            writer.WriteLine(JsonSerializer.Serialize(q, s_jsonlOptions));
        }
    }

    private static void WriteRetrievalTraceJsonl(string outputDir, IReadOnlyList<LibrarianEvalResult> results)
    {
        var path = Path.Combine(outputDir, "retrieval-trace.jsonl");
        using var writer = new StreamWriter(path);
        foreach (var r in results)
        {
            var trace = new
            {
                question_id = r.QuestionId,
                query = r.Query,
                raw_response = r.RawResponse,
                parse_failed = r.ParseFailed,
                passages = r.ParsedBundle?.RelevantPassages ?? [],
                sources = r.ParsedBundle?.SourceFiles ?? [],
                confidence = r.ParsedBundle?.Confidence.ToString().ToLowerInvariant(),
                usage = new { r.Usage.InputTokens, r.Usage.OutputTokens, r.Usage.TotalTokens },
            };
            writer.WriteLine(JsonSerializer.Serialize(trace, s_jsonlOptions));
        }
    }

    private static void WriteAnswersJsonl(string outputDir, IReadOnlyList<LibrarianEvalResult> results)
    {
        var path = Path.Combine(outputDir, "answers.jsonl");
        using var writer = new StreamWriter(path);
        foreach (var r in results)
        {
            var answer = new
            {
                question_id = r.QuestionId,
                query = r.Query,
                passages = r.ParsedBundle?.RelevantPassages ?? [],
                sources = r.ParsedBundle?.SourceFiles ?? [],
                confidence = r.ParsedBundle?.Confidence.ToString().ToLowerInvariant(),
                parse_failed = r.ParseFailed,
            };
            writer.WriteLine(JsonSerializer.Serialize(answer, s_jsonlOptions));
        }
    }

    private static void WriteEvaluationJson(string outputDir, IReadOnlyList<LibrarianEvalQuestion> questions, IReadOnlyList<LibrarianEvalResult> results)
    {
        var path = Path.Combine(outputDir, "evaluation.json");

        var perQuestion = new List<object>();
        foreach (var r in results)
        {
            perQuestion.Add(new
            {
                question_id = r.QuestionId,
                query = r.Query,
                scores = new
                {
                    correct_source_included = r.Scores.CorrectSourceIncluded,
                    off_character_source_excluded = r.Scores.OffCharacterSourceExcluded,
                    no_forbidden_graft = r.Scores.NoForbiddenGraft,
                    asked_for_clarification = r.Scores.AskedForClarification,
                    shared_facts_accessible = r.Scores.SharedFactsAccessible,
                    expected_passages_present = r.Scores.ExpectedPassagesPresent,
                    overall = r.Scores.OverallScore,
                },
                parse_failed = r.ParseFailed,
                retrieval_failed = r.RetrievalFailed,
                synthesis_failed = r.SynthesisFailed,
                notes = r.Notes,
            });
        }

        var overallScores = results.Select(r => r.Scores.OverallScore).ToList();
        var avgOverall = overallScores.Count > 0 ? overallScores.Average() : 0.0;

        var retrievalFailures = results.Where(r => r.RetrievalFailed).ToList();
        var synthesisFailures = results.Where(r => r.SynthesisFailed).ToList();

        var summary = new
        {
            ran_at = DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            total_questions = questions.Count,
            average_overall_score = avgOverall,
            retrieval_failure_count = retrievalFailures.Count,
            synthesis_failure_count = synthesisFailures.Count,
            per_question = perQuestion,
        };

        File.WriteAllText(path, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower }));
    }

    private static void WriteSummaryMd(string outputDir, IReadOnlyList<LibrarianEvalQuestion> questions, IReadOnlyList<LibrarianEvalResult> results)
    {
        var path = Path.Combine(outputDir, "summary.md");
        var overallScores = results.Select(r => r.Scores.OverallScore).ToList();
        var avgOverall = overallScores.Count > 0 ? overallScores.Average() : 0.0;
        var retrievalFailures = results.Where(r => r.RetrievalFailed).ToList();
        var synthesisFailures = results.Where(r => r.SynthesisFailed).ToList();

        using var writer = new StreamWriter(path);
        writer.WriteLine("# Librarian Evaluation Summary");
        writer.WriteLine();
        writer.WriteLine($"- **Total questions**: {questions.Count}");
        writer.WriteLine($"- **Average overall score**: {avgOverall:F2}");
        writer.WriteLine($"- **Retrieval failures** (parse failed, missing expected source, or off-scope source included): {retrievalFailures.Count}");
        writer.WriteLine($"- **Synthesis failures** (sources acceptable but answer text failed structural checks): {synthesisFailures.Count}");
        writer.WriteLine();
        writer.WriteLine("## Score Breakdown");
        writer.WriteLine();
        writer.WriteLine("| Question | Overall | Source | Off-Char | No-Graft | Clarify | Shared | Passages | Parse Fail | Notes |");
        writer.WriteLine("|----------|---------|--------|----------|----------|---------|--------|----------|------------|-------|");

        foreach (var r in results)
        {
            writer.WriteLine(
                $"| {r.QuestionId} | {r.Scores.OverallScore:F2} | " +
                $"{Fmt(r.Scores.CorrectSourceIncluded)} | {Fmt(r.Scores.OffCharacterSourceExcluded)} | " +
                $"{Fmt(r.Scores.NoForbiddenGraft)} | {Fmt(r.Scores.AskedForClarification)} | " +
                $"{Fmt(r.Scores.SharedFactsAccessible)} | {Fmt(r.Scores.ExpectedPassagesPresent)} | " +
                $"{r.ParseFailed} | {r.Notes ?? ""} |");
        }

        writer.WriteLine();
        writer.WriteLine("## Retrieval vs Synthesis Failures");
        writer.WriteLine();
        writer.WriteLine("The Librarian boundary is defined as: **retrieval** = producing a parseable LoreBundle " +
            "with correct source provenance; **synthesis** = answering the query faithfully from the retrieved passages.");
        writer.WriteLine();

        if (retrievalFailures.Count > 0)
        {
            writer.WriteLine("### Retrieval Failures");
            foreach (var r in retrievalFailures)
            {
                writer.WriteLine($"- **{r.QuestionId}**: {r.Query} — source/provenance check failed; score {r.Scores.OverallScore:F2}; {r.Notes ?? "see evaluation.json"}");
            }
            writer.WriteLine();
        }

        if (synthesisFailures.Count > 0)
        {
            writer.WriteLine("### Synthesis Failures");
            foreach (var r in synthesisFailures)
            {
                writer.WriteLine($"- **{r.QuestionId}**: {r.Query} — score {r.Scores.OverallScore:F2}");
            }
            writer.WriteLine();
        }

        writer.WriteLine("## Recommendations");
        writer.WriteLine();
        writer.WriteLine("Based on this evaluation:");
        writer.WriteLine();

        bool needsMetadata = synthesisFailures.Count > 0 || retrievalFailures.Count > 0;
        if (needsMetadata)
        {
            writer.WriteLine("- **Structured lore metadata is recommended.** The synthetic (and likely real) messy corpus " +
                "produces collisions, ambiguous references, and off-scope leakage. A minimal schema with per-document " +
                "tags (`character`, `organization`, `world`, `canon`) and canonical name aliases would let the Librarian " +
                "filter sources before synthesis.");
            writer.WriteLine("- **Smallest durable schema:** Add YAML front-matter to each lore markdown file with " +
                "`type`, `canonical_names`, `canon`, and `excludes` fields. The `FileSystemLoreStore` can expose " +
                "this as a lightweight metadata index without requiring a database migration.");
            writer.WriteLine("- **App-editing path:** Add a `POST /api/lore/{set}/{file}/metadata` endpoint and a " +
                "UI panel in the existing Librarian prompt editor to let users tag documents interactively.");
        }
        else
        {
            writer.WriteLine("- **No structured metadata required yet.** The Librarian is scoring well on this corpus.");
            writer.WriteLine("- **Keep monitoring:** Add metadata if the real corpus shows significantly more collisions.");
        }

        writer.WriteLine();
        writer.WriteLine("---");
        writer.WriteLine($"Report generated at {DateTimeOffset.UtcNow:O}");
    }

    private static string Fmt(double? value)
    {
        return value.HasValue ? value.Value.ToString("F2", CultureInfo.InvariantCulture) : "n/a";
    }
}
