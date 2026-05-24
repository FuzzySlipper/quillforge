using QuillForge.Core.Models;
using QuillForge.Core.Pipeline;

namespace QuillForge.Core.Tests;

public class ForgeStatsTrackerTests
{
    [Fact]
    public void NewTracker_StartsAtZero()
    {
        var tracker = new ForgeStatsTracker();
        var stats = tracker.Snapshot();

        Assert.Equal(0, stats.TotalInputTokens);
        Assert.Equal(0, stats.TotalOutputTokens);
        Assert.Equal(0, stats.AgentCalls);
        Assert.Equal(0, stats.ChaptersRevised);
        Assert.Equal(0, stats.TotalLatencyMs);
        Assert.Equal(0, stats.AverageLatencyMs);
        Assert.Empty(stats.StageTiming);
    }

    [Fact]
    public void SeededTracker_PreservesExistingStats()
    {
        var existing = new ForgeStats
        {
            TotalInputTokens = 500,
            TotalOutputTokens = 1000,
            AgentCalls = 10,
            ChaptersRevised = 3,
            TotalLatencyMs = 4500,
            StageTiming = new Dictionary<string, StageTiming>
            {
                ["Planning"] = new(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow),
            },
        };

        var tracker = new ForgeStatsTracker(existing);
        var stats = tracker.Snapshot();

        Assert.Equal(500, stats.TotalInputTokens);
        Assert.Equal(1000, stats.TotalOutputTokens);
        Assert.Equal(10, stats.AgentCalls);
        Assert.Equal(3, stats.ChaptersRevised);
        Assert.Equal(4500, stats.TotalLatencyMs);
        Assert.Equal(450, stats.AverageLatencyMs); // 4500 / 10
        Assert.True(stats.StageTiming.ContainsKey("Planning"));
    }

    [Fact]
    public void RecordCompletion_AccumulatesTokensAndCalls()
    {
        var tracker = new ForgeStatsTracker();

        tracker.RecordCompletion("planner", new TokenUsage(100, 200));
        tracker.RecordCompletion("writer", new TokenUsage(150, 300));

        var stats = tracker.Snapshot();
        Assert.Equal(250, stats.TotalInputTokens);
        Assert.Equal(500, stats.TotalOutputTokens);
        Assert.Equal(2, stats.AgentCalls);
        Assert.Equal(0, stats.TotalLatencyMs); // RecordCompletion does not track latency
    }

    [Fact]
    public void RecordCompletionWithLatency_AccumulatesTokensCallsAndLatency()
    {
        var tracker = new ForgeStatsTracker();

        tracker.RecordCompletionWithLatency("planner", new TokenUsage(100, 200), 1200);
        tracker.RecordCompletionWithLatency("writer", new TokenUsage(150, 300), 800);

        var stats = tracker.Snapshot();
        Assert.Equal(250, stats.TotalInputTokens);
        Assert.Equal(500, stats.TotalOutputTokens);
        Assert.Equal(2, stats.AgentCalls);
        Assert.Equal(2000, stats.TotalLatencyMs);
        Assert.Equal(1000, stats.AverageLatencyMs); // 2000 / 2
    }

    [Fact]
    public void RecordChapterRevision_IncrementsCount()
    {
        var tracker = new ForgeStatsTracker();

        tracker.RecordChapterRevision("ch-01");
        tracker.RecordChapterRevision("ch-02");
        tracker.RecordChapterRevision("ch-01");

        var stats = tracker.Snapshot();
        Assert.Equal(3, stats.ChaptersRevised);
    }

    [Fact]
    public void StageTiming_RecordsStartAndCompletion()
    {
        var tracker = new ForgeStatsTracker();
        var start = DateTimeOffset.UtcNow;
        var end = start.AddMinutes(5);

        tracker.StageStarted("Planning", start);
        tracker.StageCompleted("Planning", end);

        var stats = tracker.Snapshot();
        Assert.True(stats.StageTiming.ContainsKey("Planning"));
        Assert.Equal(start, stats.StageTiming["Planning"].Start);
        Assert.Equal(end, stats.StageTiming["Planning"].End);
    }

    [Fact]
    public void StageFailed_RecordsEndTimestamp()
    {
        var tracker = new ForgeStatsTracker();
        var start = DateTimeOffset.UtcNow;
        var failedAt = start.AddSeconds(30);

        tracker.StageStarted("Writing", start);
        tracker.StageFailed("Writing", failedAt);

        var stats = tracker.Snapshot();
        Assert.Equal(start, stats.StageTiming["Writing"].Start);
        Assert.Equal(failedAt, stats.StageTiming["Writing"].End);
    }

    [Fact]
    public void ApplyTo_ReturnsManifestWithUpdatedStats()
    {
        var tracker = new ForgeStatsTracker();
        tracker.RecordCompletionWithLatency("planner", new TokenUsage(100, 200), 500);

        var manifest = new ForgeManifest
        {
            ProjectName = "test",
            Stats = new ForgeStats(), // zeroed
        };

        var updated = tracker.ApplyTo(manifest);

        Assert.Equal(100, updated.Stats.TotalInputTokens);
        Assert.Equal(200, updated.Stats.TotalOutputTokens);
        Assert.Equal(500, updated.Stats.TotalLatencyMs);
        Assert.Equal("test", updated.ProjectName); // other fields preserved
    }

    [Fact]
    public void Snapshot_ReturnsImmutableCopy()
    {
        var tracker = new ForgeStatsTracker();
        tracker.RecordCompletion("planner", new TokenUsage(10, 20));

        var snapshot1 = tracker.Snapshot();

        // Record more after snapshot
        tracker.RecordCompletion("writer", new TokenUsage(30, 40));

        var snapshot2 = tracker.Snapshot();

        // First snapshot should be unchanged
        Assert.Equal(10, snapshot1.TotalInputTokens);
        Assert.Equal(1, snapshot1.AgentCalls);

        // Second snapshot includes both
        Assert.Equal(40, snapshot2.TotalInputTokens);
        Assert.Equal(2, snapshot2.AgentCalls);
    }

    [Fact]
    public void AverageLatencyMs_ReturnsZero_WhenNoCalls()
    {
        var tracker = new ForgeStatsTracker();
        var stats = tracker.Snapshot();
        Assert.Equal(0, stats.AverageLatencyMs);
    }
}
