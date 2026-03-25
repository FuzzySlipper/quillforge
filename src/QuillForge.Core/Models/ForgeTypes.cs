namespace QuillForge.Core.Models;

/// <summary>
/// The persistent manifest tracking a forge project's state.
/// Updated after each pipeline stage completes.
/// </summary>
public sealed record ForgeManifest
{
    public required string ProjectName { get; init; }
    public ForgeStage Stage { get; init; } = ForgeStage.Planning;
    public int ChapterCount { get; init; }
    public Dictionary<string, ChapterStatus> Chapters { get; init; } = [];
    public bool Paused { get; init; }
    public bool PauseAfterChapter1 { get; init; } = true;
    public string ArcType { get; init; } = "complete";
    public ForgeStats Stats { get; init; } = new();
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
}

public enum ForgeStage
{
    Planning,
    Design,
    Writing,
    Review,
    Assembly,
    Done
}

/// <summary>
/// Status of a single chapter in the forge pipeline.
/// </summary>
public sealed record ChapterStatus
{
    public ChapterState State { get; init; } = ChapterState.Pending;
    public int RevisionCount { get; init; }
    public int WordCount { get; init; }
    public IReadOnlyDictionary<string, double>? Scores { get; init; }
    public IReadOnlyList<string> Feedback { get; init; } = [];
}

public enum ChapterState
{
    Pending,
    Writing,
    Review,
    Revision,
    Done,
    Flagged
}

/// <summary>
/// Aggregate statistics for a forge pipeline run.
/// </summary>
public sealed record ForgeStats
{
    public long TotalInputTokens { get; init; }
    public long TotalOutputTokens { get; init; }
    public int AgentCalls { get; init; }
    public int ChaptersRevised { get; init; }
    public IReadOnlyDictionary<string, StageTiming> StageTiming { get; init; } =
        new Dictionary<string, StageTiming>();
}

public sealed record StageTiming(DateTimeOffset Start, DateTimeOffset? End);

/// <summary>
/// Events emitted by the forge pipeline during execution.
/// </summary>
public abstract class ForgeEvent;

public sealed class StageStartedEvent(string stageName) : ForgeEvent
{
    public string StageName { get; } = stageName;
}

public sealed class StageCompletedEvent(string stageName) : ForgeEvent
{
    public string StageName { get; } = stageName;
}

public sealed class ChapterProgressEvent(string chapterId, string status, string? detail = null) : ForgeEvent
{
    public string ChapterId { get; } = chapterId;
    public string Status { get; } = status;
    public string? Detail { get; } = detail;
}

public sealed class ForgeErrorEvent(string message, string? stageName = null) : ForgeEvent
{
    public string Message { get; } = message;
    public string? StageName { get; } = stageName;
}

public sealed class ForgeCompletedEvent(ForgeStats stats) : ForgeEvent
{
    public ForgeStats Stats { get; } = stats;
}
