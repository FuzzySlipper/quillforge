using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Pipeline;

/// <summary>
/// Stage 4: ForgeReviewer scores each chapter. Chapters below threshold
/// are sent back for revision (re-enters WritingStage for those chapters).
/// </summary>
public sealed class ReviewStage : IPipelineStage
{
    private readonly ILogger<ReviewStage> _logger;

    public ReviewStage(ILogger<ReviewStage> logger)
    {
        _logger = logger;
    }

    public string StageName => "Review";
    public ForgeStage StageEnum => ForgeStage.Review;

    public async IAsyncEnumerable<ForgeEvent> ExecuteAsync(
        ForgeContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogInformation("Review stage starting for project {Project}", context.Manifest.ProjectName);
        yield return new StageStartedEvent(StageName);

        // Load the style doc for the reviewer
        string styleDoc;
        try
        {
            var stylePath = $"forge/{context.Manifest.ProjectName}/plan/style.md";
            styleDoc = await context.FileService.ReadAsync(stylePath, ct);
        }
        catch (FileNotFoundException)
        {
            styleDoc = context.WritingStyle;
        }

        var chapterIds = context.Manifest.Chapters.Keys.OrderBy(k => k).ToList();
        var previousTail = "";

        foreach (var chapterId in chapterIds)
        {
            ct.ThrowIfCancellationRequested();

            var chapter = context.Manifest.Chapters[chapterId];

            // Only review chapters in Review state
            if (chapter.State != ChapterState.Review)
            {
                if (chapter.State == ChapterState.Done)
                {
                    var draftPath = $"forge/{context.Manifest.ProjectName}/drafts/{chapterId}.md";
                    try { previousTail = GetTail(await context.FileService.ReadAsync(draftPath, ct)); }
                    catch (FileNotFoundException) { }
                }
                continue;
            }

            yield return new ChapterProgressEvent(chapterId, "reviewing");

            // Load draft and brief
            var draft = await context.FileService.ReadAsync(
                $"forge/{context.Manifest.ProjectName}/drafts/{chapterId}.md", ct);
            string brief;
            try
            {
                brief = await context.FileService.ReadAsync(
                    $"forge/{context.Manifest.ProjectName}/plan/{chapterId}-brief.md", ct);
            }
            catch (FileNotFoundException)
            {
                brief = "";
            }

            var result = await context.Reviewer.ReviewAsync(
                draft, brief, styleDoc, previousTail, ct: ct);

            var scores = new Dictionary<string, double>
            {
                ["continuity"] = result.Continuity,
                ["brief_adherence"] = result.BriefAdherence,
                ["voice_consistency"] = result.VoiceConsistency,
                ["quality"] = result.Quality,
                ["overall"] = result.Overall,
            };

            ChapterState newState;
            if (result.Passed)
            {
                newState = ChapterState.Done;
                _logger.LogInformation(
                    "Chapter {ChapterId} passed review: overall={Overall:F1}",
                    chapterId, result.Overall);
            }
            else if (chapter.RevisionCount >= context.MaxRevisions)
            {
                newState = ChapterState.Flagged;
                _logger.LogWarning(
                    "Chapter {ChapterId} flagged: failed after {Revisions} revisions, overall={Overall:F1}",
                    chapterId, chapter.RevisionCount, result.Overall);
            }
            else
            {
                newState = ChapterState.Revision;
                _logger.LogInformation(
                    "Chapter {ChapterId} needs revision: overall={Overall:F1}, attempt {Attempt}",
                    chapterId, result.Overall, chapter.RevisionCount + 1);
            }

            context.Manifest = context.Manifest with
            {
                Chapters = new Dictionary<string, ChapterStatus>(context.Manifest.Chapters)
                {
                    [chapterId] = chapter with
                    {
                        State = newState,
                        Scores = scores,
                        Feedback = [.. chapter.Feedback, result.Feedback],
                        RevisionCount = result.Passed ? chapter.RevisionCount : chapter.RevisionCount + 1,
                    }
                }
            };

            previousTail = GetTail(draft);

            yield return new ChapterProgressEvent(chapterId,
                result.Passed ? "passed" : "needs_revision",
                $"overall={result.Overall:F1}");
        }

        yield return new StageCompletedEvent(StageName);
    }

    private static string GetTail(string text, int length = 500)
    {
        return text.Length <= length ? text : text[^length..];
    }
}
