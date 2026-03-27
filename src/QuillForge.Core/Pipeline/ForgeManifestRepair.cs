using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Pipeline;

/// <summary>
/// Repairs and normalizes forge manifests that may have been corrupted
/// or contain LLM-generated irregularities.
/// </summary>
public static class ForgeManifestRepair
{
    private static readonly Regex ChapterKeyPattern = new(@"^ch-\d{2,}$", RegexOptions.Compiled);
    private static readonly Regex NumberExtractor = new(@"(\d+)", RegexOptions.Compiled);
    private static readonly Regex ChapterIdExtractor = new(@"(ch-\d{2,})", RegexOptions.Compiled);

    /// <summary>
    /// Normalizes a manifest: fixes chapter key formats, validates enum values,
    /// clamps scores, removes invalid entries, ensures required defaults.
    /// </summary>
    public static ForgeManifest Normalize(ForgeManifest manifest, ILogger? logger = null)
    {
        var normalized = new Dictionary<string, ChapterStatus>();

        foreach (var (key, chapter) in manifest.Chapters)
        {
            // Normalize chapter key to ch-NN format
            var normalizedKey = NormalizeChapterKey(key);
            if (normalizedKey is null)
            {
                logger?.LogWarning("Removing invalid chapter key: {Key}", key);
                continue;
            }

            // Clamp scores to 0-10
            IReadOnlyDictionary<string, double>? clampedScores = null;
            if (chapter.Scores is not null)
            {
                var scores = new Dictionary<string, double>();
                foreach (var (scoreKey, value) in chapter.Scores)
                {
                    scores[scoreKey] = Math.Clamp(value, 0, 10);
                }

                clampedScores = scores;
            }

            // Ensure revision count is non-negative
            var revisionCount = Math.Max(0, chapter.RevisionCount);

            // Ensure word count is non-negative
            var wordCount = Math.Max(0, chapter.WordCount);

            normalized[normalizedKey] = chapter with
            {
                RevisionCount = revisionCount,
                WordCount = wordCount,
                Scores = clampedScores,
            };
        }

        // Ensure chapter count matches actual chapters
        var chapterCount = manifest.ChapterCount > 0
            ? manifest.ChapterCount
            : normalized.Count;

        return manifest with
        {
            Chapters = normalized,
            ChapterCount = chapterCount,
            UpdatedAt = manifest.UpdatedAt == default ? DateTimeOffset.UtcNow : manifest.UpdatedAt,
            CreatedAt = manifest.CreatedAt == default ? DateTimeOffset.UtcNow : manifest.CreatedAt,
        };
    }

    /// <summary>
    /// Reconstructs a manifest by scanning the project directory on disk.
    /// Used as fallback when the manifest file is corrupted or missing.
    /// </summary>
    public static async Task<ForgeManifest> RebuildFromFilesAsync(
        string projectName,
        IContentFileService fileService,
        ILogger? logger = null,
        CancellationToken ct = default)
    {
        var basePath = $"forge/{projectName}";
        var chapters = new Dictionary<string, ChapterStatus>();

        // Scan for chapter files
        var planFiles = await fileService.ListAsync($"{basePath}/plan", "*.md", ct);
        var draftFiles = await fileService.ListAsync($"{basePath}/drafts", "*.md", ct);

        // Extract chapter IDs from plan and draft files
        var chapterIds = new HashSet<string>();
        foreach (var file in planFiles.Concat(draftFiles))
        {
            var fileName = file.Split('/').Last();
            var chId = ExtractChapterId(fileName);
            if (chId is not null)
                chapterIds.Add(chId);
        }

        // Build lookup sets from filenames for quick membership checks
        var briefSet = planFiles
            .Select(f => f.Split('/').Last())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var draftSet = draftFiles
            .Select(f => f.Split('/').Last())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // For each chapter, determine state from files present
        foreach (var chId in chapterIds.OrderBy(c => c))
        {
            var hasBrief = briefSet.Contains($"{chId}-brief.md");
            var hasDraft = draftSet.Contains($"{chId}-draft.md") || draftSet.Contains($"{chId}.md");

            var state = (hasBrief, hasDraft) switch
            {
                (_, true) => ChapterState.Review, // Has draft, needs review
                (true, false) => ChapterState.Pending, // Has brief, no draft yet
                _ => ChapterState.Pending,
            };

            // Count words in draft if it exists
            var wordCount = 0;
            if (hasDraft)
            {
                var draftPath = draftSet.Contains($"{chId}-draft.md")
                    ? $"{basePath}/drafts/{chId}-draft.md"
                    : $"{basePath}/drafts/{chId}.md";
                try
                {
                    var content = await fileService.ReadAsync(draftPath, ct);
                    wordCount = content.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                }
                catch
                {
                    // Ignore read failures during rebuild
                }
            }

            chapters[chId] = new ChapterStatus
            {
                State = state,
                WordCount = wordCount,
            };
        }

        // Determine overall stage from what files exist
        var hasOutline = await fileService.ExistsAsync($"{basePath}/plan/outline.md", ct);
        var hasStyle = await fileService.ExistsAsync($"{basePath}/plan/style.md", ct);
        var hasBible = await fileService.ExistsAsync($"{basePath}/plan/bible.md", ct);
        var hasOutput = await fileService.ExistsAsync($"{basePath}/output/story.md", ct);

        var stage = ForgeStage.Planning;
        if (hasOutline && hasStyle && hasBible)
            stage = ForgeStage.Design;
        if (chapters.Values.Any(c => c.State != ChapterState.Pending))
            stage = ForgeStage.Writing;
        if (chapters.Count > 0 && chapters.Values.All(c => c.State is ChapterState.Done or ChapterState.Flagged))
            stage = ForgeStage.Assembly;
        if (hasOutput)
            stage = ForgeStage.Done;

        logger?.LogInformation(
            "Rebuilt manifest for {Project}: {ChapterCount} chapters, stage={Stage}",
            projectName, chapters.Count, stage);

        return new ForgeManifest
        {
            ProjectName = projectName,
            Stage = stage,
            ChapterCount = chapters.Count,
            Chapters = chapters,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    /// <summary>
    /// Normalizes a chapter key to the ch-NN format.
    /// Handles variations like "ch01", "chapter-1", "ch_01", "1", etc.
    /// Returns null if the key cannot be normalized.
    /// </summary>
    internal static string? NormalizeChapterKey(string key)
    {
        // Already in correct format
        if (ChapterKeyPattern.IsMatch(key))
            return key;

        // Try to extract a number from the key
        var match = NumberExtractor.Match(key);
        if (!match.Success)
            return null;

        var num = int.Parse(match.Groups[1].Value);
        return $"ch-{num:D2}";
    }

    /// <summary>
    /// Extracts a chapter ID (ch-NN) from a filename like "ch-01-brief.md" or "ch-01-draft.md".
    /// </summary>
    private static string? ExtractChapterId(string fileName)
    {
        var match = ChapterIdExtractor.Match(fileName);
        return match.Success ? match.Groups[1].Value : null;
    }
}
