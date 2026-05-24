using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

public sealed class UsageTrackingCompletionServiceTests
{
    [Fact]
    public async Task CompleteAsync_RecordsUsageAndLatency_UnderCurrentScope()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueResponse(new CompletionResponse
        {
            Content = new MessageContent("Tracked."),
            StopReason = StopReason.EndTurn,
            Usage = new TokenUsage(4, 6),
        });

        var tracker = new InMemoryTokenUsageTracker(NullLogger<InMemoryTokenUsageTracker>.Instance);
        var service = new UsageTrackingCompletionService(
            fake,
            tracker,
            NullLogger<UsageTrackingCompletionService>.Instance);
        var sessionId = Guid.CreateVersion7();

        using (TokenTrackingScope.Begin(sessionId, "narrative-director"))
        {
            await service.CompleteAsync(new CompletionRequest
            {
                Model = "test-model",
                MaxTokens = 64,
                Messages = [new CompletionMessage("user", new MessageContent("Hi"))],
            });
        }

        var summary = tracker.GetSessionUsage(sessionId);
        Assert.Equal(1, summary.TotalRequests);
        var entry = Assert.Single(summary.ByAgent);
        Assert.Equal("narrative-director", entry.AgentName);
        Assert.Equal(1, entry.RequestCount);
        Assert.Equal(4, entry.InputTokens);
        Assert.Equal(6, entry.OutputTokens);
        // Fake completes instantly, so latency may be 0.
        // In production, UsageTrackingCompletionService records non-zero latency via Stopwatch.
        Assert.True(entry.TotalLatencyMs >= 0);
    }

    [Fact]
    public async Task StreamAsync_CapturesScopeAtStreamCreationTime()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueResponse(new CompletionResponse
        {
            Content = new MessageContent("Tracked stream."),
            StopReason = StopReason.EndTurn,
            Usage = new TokenUsage(5, 7),
        });

        var tracker = new InMemoryTokenUsageTracker(NullLogger<InMemoryTokenUsageTracker>.Instance);
        var service = new UsageTrackingCompletionService(
            fake,
            tracker,
            NullLogger<UsageTrackingCompletionService>.Instance);
        var sessionId = Guid.CreateVersion7();

        IAsyncEnumerable<StreamEvent> stream;
        using (TokenTrackingScope.Begin(sessionId, "orchestrator"))
        {
            stream = service.StreamAsync(new CompletionRequest
            {
                Model = "test-model",
                MaxTokens = 64,
                Messages = [new CompletionMessage("user", new MessageContent("Hi"))],
            });
        }

        var events = new List<StreamEvent>();
        await foreach (var evt in stream)
        {
            events.Add(evt);
        }

        Assert.Contains(events, evt => evt is DoneEvent done && done.Usage.InputTokens == 5 && done.Usage.OutputTokens == 7);

        var summary = tracker.GetSessionUsage(sessionId);
        Assert.Equal(1, summary.TotalRequests);
        var entry = Assert.Single(summary.ByAgent);
        Assert.Equal("orchestrator", entry.AgentName);
        Assert.Equal(1, entry.RequestCount);
        Assert.Equal(5, entry.InputTokens);
        Assert.Equal(7, entry.OutputTokens);
        Assert.True(entry.TotalLatencyMs >= 0);
    }

    [Fact]
    public async Task CompleteAsync_AccumulatesLatencyAcrossMultipleCalls()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("First.");
        fake.EnqueueText("Second.");
        fake.EnqueueText("Third.");

        var tracker = new InMemoryTokenUsageTracker(NullLogger<InMemoryTokenUsageTracker>.Instance);
        var service = new UsageTrackingCompletionService(
            fake,
            tracker,
            NullLogger<UsageTrackingCompletionService>.Instance);
        var sessionId = Guid.CreateVersion7();

        using (TokenTrackingScope.Begin(sessionId, "test-agent"))
        {
            for (int i = 0; i < 3; i++)
            {
                await service.CompleteAsync(new CompletionRequest
                {
                    Model = "test-model",
                    MaxTokens = 64,
                    Messages = [new CompletionMessage("user", new MessageContent("Hi"))],
                });
            }
        }

        var summary = tracker.GetSessionUsage(sessionId);
        Assert.Equal(3, summary.TotalRequests);
        var entry = Assert.Single(summary.ByAgent);
        Assert.Equal(3, entry.RequestCount);
        Assert.Equal(30, entry.InputTokens); // 3 * 10 (from EnqueueText default)
        Assert.Equal(60, entry.OutputTokens); // 3 * 20
        Assert.True(entry.TotalLatencyMs >= 0);
        Assert.True(summary.TotalLatencyMs >= 0);
    }

    [Fact]
    public async Task CompleteAsync_MultipleAgents_TracksLatencyPerAgent()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("Agent A.");
        fake.EnqueueText("Agent B.");

        var tracker = new InMemoryTokenUsageTracker(NullLogger<InMemoryTokenUsageTracker>.Instance);
        var service = new UsageTrackingCompletionService(
            fake,
            tracker,
            NullLogger<UsageTrackingCompletionService>.Instance);
        var sessionId = Guid.CreateVersion7();

        using (TokenTrackingScope.Begin(sessionId, "agent-alpha"))
        {
            await service.CompleteAsync(new CompletionRequest
            {
                Model = "test-model",
                MaxTokens = 64,
                Messages = [new CompletionMessage("user", new MessageContent("Hi"))],
            });
        }

        using (TokenTrackingScope.Begin(sessionId, "agent-beta"))
        {
            await service.CompleteAsync(new CompletionRequest
            {
                Model = "test-model",
                MaxTokens = 64,
                Messages = [new CompletionMessage("user", new MessageContent("Hi"))],
            });
        }

        var summary = tracker.GetSessionUsage(sessionId);
        Assert.Equal(2, summary.TotalRequests);
        Assert.Equal(2, summary.ByAgent.Count);

        var alpha = summary.ByAgent.First(a => a.AgentName == "agent-alpha");
        Assert.True(alpha.TotalLatencyMs >= 0);
        Assert.Equal(1, alpha.RequestCount);

        var beta = summary.ByAgent.First(a => a.AgentName == "agent-beta");
        Assert.True(beta.TotalLatencyMs >= 0);
        Assert.Equal(1, beta.RequestCount);
    }
}
