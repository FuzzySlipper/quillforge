using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Pipeline;

/// <summary>
/// Stage 5: Combines all approved chapter drafts into the final story output.
/// Flagged chapters are included with a warning marker.
/// </summary>
public sealed class AssemblyStage : IPipelineStage
{
    private readonly ILogger<AssemblyStage> _logger;

    public AssemblyStage(ILogger<AssemblyStage> logger)
    {
        _logger = logger;
    }

    public string StageName => "Assembly";
    public ForgeStage StageEnum => ForgeStage.Assembly;

    public async IAsyncEnumerable<ForgeEvent> ExecuteAsync(
        ForgeContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogInformation("Assembly stage starting for project {Project}", context.Manifest.ProjectName);
        yield return new StageStartedEvent(StageName);

        var chapterIds = context.Manifest.Chapters.Keys.OrderBy(k => k).ToList();
        context.Log($"Assembling {chapterIds.Count} chapters...", "assembly");
        var assembled = new StringBuilder();
        var totalWords = 0;

        foreach (var chapterId in chapterIds)
        {
            ct.ThrowIfCancellationRequested();

            var chapter = context.Manifest.Chapters[chapterId];
            var draftPath = $"forge/{context.Manifest.ProjectName}/drafts/{chapterId}.md";

            string draft;
            try
            {
                draft = await context.FileService.ReadAsync(draftPath, ct);
            }
            catch (FileNotFoundException)
            {
                _logger.LogWarning("No draft found for {ChapterId} during assembly", chapterId);
                context.Log($"WARNING: No draft found for {chapterId}", "assembly");
                continue;
            }

            if (chapter.State == ChapterState.Flagged)
            {
                assembled.AppendLine($"<!-- WARNING: Chapter {chapterId} was flagged for quality issues -->");
                _logger.LogWarning("Including flagged chapter {ChapterId} in assembly", chapterId);
                context.Log($"Including flagged chapter {chapterId} with warning marker", "assembly");
            }

            assembled.AppendLine(draft);
            assembled.AppendLine();
            var chapterWords = draft.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            totalWords += chapterWords;
            context.Log($"{chapterId} assembled ({chapterWords} words)", "assembly");

            yield return new ChapterProgressEvent(chapterId, "assembled");
        }

        // Write the assembled output
        var outputPath = $"forge/{context.Manifest.ProjectName}/output/story.md";
        await context.FileService.WriteAsync(outputPath, assembled.ToString(), ct);
        context.Log($"Final output written to {outputPath}: {totalWords} total words", "assembly");

        _logger.LogInformation(
            "Assembly complete: {ChapterCount} chapters, {TotalWords} words",
            chapterIds.Count, totalWords);

        yield return new StageCompletedEvent(StageName);
    }
}
