using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

public class ToolLoopStreamingTests
{
    private static readonly AgentConfig DefaultConfig = new()
    {
        Model = "test-model",
        MaxTokens = 1024,
        SystemPrompt = "You are a test agent.",
        MaxToolRounds = 5,
    };

    private static readonly AgentContext DefaultContext = new()
    {
        SessionId = Guid.CreateVersion7(),
        ActiveMode = "general",
    };

    private static ToolLoop CreateLoop(FakeCompletionService fakeService)
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var continuation = new ContinuationStrategy(
            loggerFactory.CreateLogger<ContinuationStrategy>());
        return new ToolLoop(fakeService, continuation,
            loggerFactory.CreateLogger<ToolLoop>());
    }

    [Fact]
    public async Task StreamSimpleText_YieldsTextDeltaAndDone()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("streamed text");

        var loop = CreateLoop(fake);
        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("hi")),
        };

        var events = new List<StreamEvent>();
        await foreach (var evt in loop.RunStreamAsync(DefaultConfig, [], messages, DefaultContext))
        {
            events.Add(evt);
        }

        Assert.Equal(2, events.Count);
        Assert.IsType<TextDeltaEvent>(events[0]);
        Assert.Equal("streamed text", ((TextDeltaEvent)events[0]).Text);
        Assert.IsType<DoneEvent>(events[1]);
        Assert.Equal("end_turn", ((DoneEvent)events[1]).StopReason);
    }

    [Fact]
    public async Task StreamWithToolCall_DispatchesAndContinues()
    {
        var fake = new FakeCompletionService();
        // Round 1: tool call
        fake.EnqueueToolCall("my_tool", "call_1", "{}");
        // Round 2: final text
        fake.EnqueueText("result after tool");

        var handler = new FakeToolHandler("my_tool");
        var loop = CreateLoop(fake);
        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("use tool")),
        };

        var events = new List<StreamEvent>();
        await foreach (var evt in loop.RunStreamAsync(DefaultConfig, [handler], messages, DefaultContext))
        {
            events.Add(evt);
        }

        // Should have: ToolCallEvent, DoneEvent (from round 1), TextDeltaEvent, DoneEvent (from round 2)
        Assert.Contains(events, e => e is ToolCallEvent tc && tc.ToolName == "my_tool");
        Assert.Contains(events, e => e is TextDeltaEvent td && td.Text == "result after tool");

        var doneEvents = events.OfType<DoneEvent>().ToList();
        Assert.Single(doneEvents); // Only the final done
        Assert.Equal("end_turn", doneEvents[0].StopReason);
    }

    [Fact]
    public async Task StreamMaxRounds_StopsWithMaxRoundsDone()
    {
        var fake = new FakeCompletionService();
        for (var i = 0; i < 10; i++)
        {
            fake.EnqueueToolCall("looper", $"call_{i}", "{}");
        }

        var config = DefaultConfig with { MaxToolRounds = 2 };
        var handler = new FakeToolHandler("looper");
        var loop = CreateLoop(fake);
        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("loop")),
        };

        var events = new List<StreamEvent>();
        await foreach (var evt in loop.RunStreamAsync(config, [handler], messages, DefaultContext))
        {
            events.Add(evt);
        }

        var done = events.OfType<DoneEvent>().Last();
        Assert.Equal("max_rounds", done.StopReason);
    }
}
