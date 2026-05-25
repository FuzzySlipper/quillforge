using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Storage.FileSystem;

namespace QuillForge.LibrarianEval;

/// <summary>
/// Runs a batch of LibrarianEvalQuestions against a LibrarianAgent and produces
/// LibrarianEvalResults with structural scores.
/// </summary>
public sealed class LibrarianEvalRunner
{
    private readonly LibrarianAgent _librarian;
    private readonly ILoreStore _loreStore;
    private readonly LibrarianEvalScorer _scorer;
    private readonly ILogger<LibrarianEvalRunner> _logger;

    public LibrarianEvalRunner(LibrarianAgent librarian, ILoreStore loreStore, LibrarianEvalScorer scorer, ILogger<LibrarianEvalRunner> logger)
    {
        _librarian = librarian;
        _loreStore = loreStore;
        _scorer = scorer;
        _logger = logger;
    }

    /// <summary>
    /// Runs all questions and returns results.
    /// </summary>
    public async Task<IReadOnlyList<LibrarianEvalResult>> RunAsync(
        IReadOnlyList<LibrarianEvalQuestion> questions,
        string loreSetName,
        CancellationToken ct = default)
    {
        var results = new List<LibrarianEvalResult>();
        var context = BuildAgentContext();

        // Pre-load lore to fail fast if corpus is missing
        var lore = await _loreStore.LoadLoreSetAsync(loreSetName, ct);
        _logger.LogInformation("Evaluating against lore set \"{LoreSet}\" with {FileCount} files", loreSetName, lore.Count);

        foreach (var question in questions)
        {
            _logger.LogInformation("Running question {QuestionId}: {Query}", question.Id, question.Query);

            LibrarianEvalResult result;
            try
            {
                var librarianResult = await _librarian.QueryAsync(question.Query, loreSetName, context, ct: ct);
                var rawResponse = ExtractRawResponse(librarianResult);
                var bundle = librarianResult.Bundle;

                result = new LibrarianEvalResult
                {
                    QuestionId = question.Id,
                    Query = question.Query,
                    RawResponse = rawResponse,
                    ParsedBundle = bundle,
                    ParseFailed = false,
                    Usage = librarianResult.Usage,
                    Scores = new LibrarianEvalScores(), // placeholder, scored below
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Question {QuestionId} failed with exception", question.Id);
                result = new LibrarianEvalResult
                {
                    QuestionId = question.Id,
                    Query = question.Query,
                    RawResponse = $"EXCEPTION: {ex.Message}",
                    ParsedBundle = null,
                    ParseFailed = true,
                    Usage = new TokenUsage(0, 0),
                    Scores = new LibrarianEvalScores(),
                    Notes = $"Exception: {ex.GetType().Name}",
                };
            }

            // Score after running so we can apply scorer to both success and failure paths
            result = result with { Scores = _scorer.Score(result, question) };
            results.Add(result);
        }

        return results;
    }

    private static AgentContext BuildAgentContext()
    {
        return new AgentContext
        {
            SessionId = Guid.NewGuid(),
            ActiveMode = Mode.Guide,
            ActiveLoreSet = "default",
            LibrarianPrompt = "default",
        };
    }

    private static string ExtractRawResponse(LibrarianResult result)
    {
        // Best-effort: serialize the bundle back to JSON as the "raw" structured response.
        // The actual raw LLM text is not exposed by LibrarianResult, so we reconstruct.
        try
        {
            return JsonSerializer.Serialize(new
            {
                relevant_passages = result.Bundle.RelevantPassages,
                source_files = result.Bundle.SourceFiles,
                confidence = result.Bundle.Confidence.ToString().ToLowerInvariant(),
            });
        }
        catch
        {
            return "{ }";
        }
    }
}
