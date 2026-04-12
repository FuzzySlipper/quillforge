using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

public sealed class UsageTrackingCompletionServiceTests
{
    [Fact]
    public async Task CompleteAsync_RecordsUsageUnderCurrentScope()
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
    }
}
