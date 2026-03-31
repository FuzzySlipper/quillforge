using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Pipeline;

/// <summary>
/// Stage 3: ForgeWriter drafts each chapter based on its brief.
/// Processes chapters sequentially, passing the full previous chapter for continuity.
/// </summary>
public sealed class WritingStage : IPipelineStage
{
    private readonly ILogger<WritingStage> _logger;

    public WritingStage(ILogger<WritingStage> logger)
    {
        _logger = logger;
    }

    public string StageName => "Writing";
    public ForgeStage StageEnum => ForgeStage.Writing;

    public async IAsyncEnumerable<ForgeEvent> ExecuteAsync(
        ForgeContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogInformation("Writing stage starting for project {Project}", context.Manifest.ProjectName);
        yield return new StageStartedEvent(StageName);

        var chapterIds = context.Manifest.Chapters.Keys.OrderBy(k => k).ToList();
        var previousChapter = "";

        foreach (var chapterId in chapterIds)
        {
            ct.ThrowIfCancellationRequested();

            var chapter = context.Manifest.Chapters[chapterId];

            // Skip completed or flagged chapters (resume support + don't re-write max-revision failures)
            if (chapter.State is ChapterState.Done or ChapterState.Flagged)
            {
                _logger.LogDebug("Skipping {State} chapter {ChapterId}", chapter.State, chapterId);
                var draftPath = $"forge/{context.Manifest.ProjectName}/drafts/{chapterId}.md";
                try
                {
                    previousChapter = await context.FileService.ReadAsync(draftPath, ct);
                }
                catch (FileNotFoundException)
                {
                    previousChapter = "";
                }
                continue;
            }

            yield return new ChapterProgressEvent(chapterId, "writing");

            // Load the chapter brief
            var briefPath = $"forge/{context.Manifest.ProjectName}/plan/{chapterId}-brief.md";
            string brief;
            try
            {
                brief = await context.FileService.ReadAsync(briefPath, ct);
            }
            catch (FileNotFoundException)
            {
                _logger.LogWarning("No brief found for {ChapterId}, skipping", chapterId);
                continue;
            }

            var result = await context.Writer.WriteChapterAsync(
                brief, previousChapter, context.WritingStyle,
                context.WriterTools, context.AgentContext, ct: ct);

            // Save draft
            var outputPath = $"forge/{context.Manifest.ProjectName}/drafts/{chapterId}.md";
            await context.FileService.WriteAsync(outputPath, result.GeneratedText, ct);

            // Update chapter status
            context.Manifest = context.Manifest with
            {
                Chapters = new Dictionary<string, ChapterStatus>(context.Manifest.Chapters)
                {
                    [chapterId] = chapter with
                    {
                        State = ChapterState.Review,
                        WordCount = result.WordCount,
                    }
                }
            };

            previousChapter = result.GeneratedText;

            yield return new ChapterProgressEvent(chapterId, "draft_complete",
                $"{result.WordCount} words, {result.LoreQueriesMade.Count} lore queries");

            _logger.LogInformation(
                "Chapter {ChapterId} drafted: {Words} words",
                chapterId, result.WordCount);
        }

        yield return new StageCompletedEvent(StageName);
    }
}
