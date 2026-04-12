namespace QuillForge.ProviderHarness.Tests;

public sealed record DualSidedHarnessRun
{
    public string RunId { get; init; } = Guid.NewGuid().ToString("N");
    public string ScenarioName { get; init; } = "unnamed-run";
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; init; }
    public IReadOnlyList<HarnessProviderTrace> ProviderTraces { get; init; } = [];
    public HarnessAppTrace? AppTrace { get; init; }
    public HarnessForgeAppTrace? ForgeTrace { get; init; }
    public HarnessForgeManifestSnapshot? ForgeManifest { get; init; }
    public HarnessArtifactTrace? ArtifactTrace { get; init; }
}

public sealed record HarnessCollectedAppStream
{
    public required Guid SessionId { get; init; }
    public required string Mode { get; init; }
    public required string FinalContent { get; init; }
    public string? FinalReasoning { get; init; }
    public required string StopReason { get; init; }
    public required int MessageCount { get; init; }
    public required int ToolRounds { get; init; }
    public required HarnessUsage Usage { get; init; }
    public required IReadOnlyList<HarnessCollectedAppEvent> Events { get; init; }
    public string? WriterState { get; init; }
}

public sealed record HarnessCollectedAppEvent
{
    public required string Type { get; init; }
    public string? Text { get; init; }
    public string? ToolName { get; init; }
    public string? ToolId { get; init; }
    public string? Category { get; init; }
    public string? Message { get; init; }
    public string? Level { get; init; }
    public string? StopReason { get; init; }
    public HarnessUsage? Usage { get; init; }
}

public sealed record HarnessCollectedSessionSnapshot
{
    public required Guid SessionId { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<HarnessCollectedSessionMessage> Messages { get; init; }
}

public sealed record HarnessCollectedSessionMessage
{
    public required Guid Id { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public Guid? ParentId { get; init; }
    public string? Reasoning { get; init; }
    public IReadOnlyList<HarnessCollectedSessionMessageVariant> Variants { get; init; } = [];
}

public sealed record HarnessCollectedSessionMessageVariant
{
    public required string Content { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Reasoning { get; init; }
}

public sealed record HarnessAppTrace
{
    public required Guid SessionId { get; init; }
    public required string Mode { get; init; }
    public required string FinalContent { get; init; }
    public string? FinalReasoning { get; init; }
    public required string StopReason { get; init; }
    public required int MessageCount { get; init; }
    public required int ToolRounds { get; init; }
    public required HarnessUsage Usage { get; init; }
    public IReadOnlyList<HarnessAppTraceEvent> Events { get; init; } = [];
    public IReadOnlyList<string> TextDeltas { get; init; } = [];
    public IReadOnlyList<string> ReasoningDeltas { get; init; } = [];
    public IReadOnlyList<HarnessToolCallTrace> Tools { get; init; } = [];
    public IReadOnlyList<HarnessDiagnosticTrace> Diagnostics { get; init; } = [];
    public IReadOnlyList<HarnessPersistedMessageTrace> PersistedMessages { get; init; } = [];
    public string? WriterState { get; init; }
}

public abstract record HarnessAppTraceEvent;

public sealed record AppTextDeltaObserved(string Text) : HarnessAppTraceEvent;

public sealed record AppReasoningDeltaObserved(string Text) : HarnessAppTraceEvent;

public sealed record AppToolEventObserved(string ToolName, string? ToolId) : HarnessAppTraceEvent;

public sealed record AppDiagnosticObserved(string Category, string Message, string? Level) : HarnessAppTraceEvent;

public sealed record AppDoneObserved(string StopReason, HarnessUsage? Usage) : HarnessAppTraceEvent;

public sealed record AppUnknownEventObserved(string Type) : HarnessAppTraceEvent;

public sealed record HarnessDiagnosticTrace(
    string Category,
    string Message,
    string? Level);

public sealed record HarnessPersistedMessageTrace
{
    public required Guid Id { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public Guid? ParentId { get; init; }
    public string? Reasoning { get; init; }
    public IReadOnlyList<HarnessPersistedMessageVariantTrace> Variants { get; init; } = [];
}

public sealed record HarnessPersistedMessageVariantTrace(
    string Content,
    DateTimeOffset CreatedAt,
    string? Reasoning);

public sealed record HarnessArtifactTrace
{
    public required string RootPath { get; init; }
    public required IReadOnlyList<HarnessArtifactSnapshot> Snapshots { get; init; }
}

public sealed record HarnessArtifactSnapshot
{
    public required string RelativePath { get; init; }
    public required string AbsolutePath { get; init; }
    public required bool Exists { get; init; }
    public string? Content { get; init; }
    public DateTimeOffset? LastModified { get; init; }
    public long? Length { get; init; }
}

public sealed record HarnessCollectedForgeRun
{
    public required string ProjectName { get; init; }
    public required string Operation { get; init; }
    public required IReadOnlyList<HarnessCollectedForgeEvent> Events { get; init; }
    public string? FinalEventType { get; init; }
    public HarnessForgeStatusSnapshot? Status { get; init; }
}

public sealed record HarnessCollectedForgeEvent
{
    public required string Type { get; init; }
    public string? Message { get; init; }
    public string? Source { get; init; }
    public string? Chapter { get; init; }
    public string? Status { get; init; }
    public int? WordCount { get; init; }
    public string? Detail { get; init; }
    public int? ChaptersComplete { get; init; }
    public long? TotalTokens { get; init; }
}

public sealed record HarnessForgeAppTrace
{
    public required string ProjectName { get; init; }
    public required string Operation { get; init; }
    public required IReadOnlyList<HarnessForgeTraceEvent> Events { get; init; }
    public string? FinalEventType { get; init; }
    public HarnessForgeStatusSnapshot? Status { get; init; }
}

public abstract record HarnessForgeTraceEvent;

public sealed record ForgeStageStartedObserved(string StageName) : HarnessForgeTraceEvent;

public sealed record ForgeStageCompletedObserved(string StageName) : HarnessForgeTraceEvent;

public sealed record ForgeChapterObserved(
    string ChapterId,
    string ChapterStatus,
    string? Detail) : HarnessForgeTraceEvent;

public sealed record ForgeProgressObserved(string Message, string? Source) : HarnessForgeTraceEvent;

public sealed record ForgePausedObserved(string Message) : HarnessForgeTraceEvent;

public sealed record ForgeCompletedObserved(
    string Message,
    int? ChaptersComplete,
    long? TotalTokens) : HarnessForgeTraceEvent;

public sealed record ForgeErrorObserved(string Message, string? Source) : HarnessForgeTraceEvent;

public sealed record ForgeUnknownObserved(string Type, string? Message) : HarnessForgeTraceEvent;

public sealed record HarnessForgeManifestSnapshot
{
    public required string ProjectName { get; init; }
    public required string Stage { get; init; }
    public required int ChapterCount { get; init; }
    public required bool Paused { get; init; }
    public required IReadOnlyDictionary<string, HarnessForgeChapterSnapshot> Chapters { get; init; }
    public required HarnessForgeStatsSnapshot Stats { get; init; }
}

public sealed record HarnessForgeStatusSnapshot
{
    public required string ProjectName { get; init; }
    public required string Stage { get; init; }
    public required int ChapterCount { get; init; }
    public required bool Paused { get; init; }
    public required IReadOnlyDictionary<string, HarnessForgeChapterSnapshot> Chapters { get; init; }
    public required HarnessForgeStatsSnapshot Stats { get; init; }
}

public sealed record HarnessForgeChapterSnapshot(
    string State,
    int RevisionCount,
    int WordCount);

public sealed record HarnessForgeStatsSnapshot(
    long TotalInputTokens,
    long TotalOutputTokens,
    int AgentCalls,
    int ChaptersRevised);
