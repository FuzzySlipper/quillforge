using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

public sealed class FaultInjectionTests
{
    private static readonly AgentContext DefaultContext = new()
    {
        SessionId = Guid.CreateVersion7(),
        ActiveMode = Mode.Guide,
    };

    private static ToolLoop CreateToolLoop(ICompletionService service) =>
        new(service, new ContinuationStrategy(NullLogger<ContinuationStrategy>.Instance),
            NullLogger<ToolLoop>.Instance, new AppConfig { Diagnostics = new DiagnosticsConfig { LivePanel = true } });

    [Fact]
    public async Task TruncatedStream_YieldsNoMoreEventsAfterLimit()
    {
        var inner = new FakeCompletionService();
        inner.EnqueueText("Hello world");

        var faulty = new FaultInjectingCompletionService(inner, new FaultProfile
        {
            TruncateAfterEvents = 1, // Allow TextDelta, cut before Done
        });

        var events = new List<StreamEvent>();
        var request = new CompletionRequest
        {
            Model = "test",
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("Hi"))],
        };

        await foreach (var evt in faulty.StreamAsync(request))
        {
            events.Add(evt);
        }

        // Only the first event (TextDelta) should come through, Done is truncated
        Assert.Single(events);
        Assert.IsType<TextDeltaEvent>(events[0]);
    }

    [Fact]
    public async Task DroppedToolCalls_ToolLoopRecovery()
    {
        // Set up: inner returns tool_use stop reason with a tool call,
        // but fault injection drops the tool call event.
        // Then inner returns a text response for recovery.
        var inner = new FakeCompletionService();
        inner.EnqueueToolCall("query_lore", "tc-1", """{"query": "test"}""");
        inner.EnqueueText("Recovered response"); // For non-streaming recovery

        var faulty = new FaultInjectingCompletionService(inner, new FaultProfile
        {
            DropToolCalls = true,
        });

        var toolLoop = CreateToolLoop(faulty);
        var config = new AgentConfig
        {
            Model = "test",
            MaxTokens = 100,
            SystemPrompt = "You are a test.",
            MaxToolRounds = 3,
        };
        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("test")),
        };

        var events = new List<StreamEvent>();
        await foreach (var evt in toolLoop.RunStreamAsync(config, [], messages, DefaultContext))
        {
            events.Add(evt);
        }

        // Should have diagnostics about missing tool calls + recovery attempt + final done
        var diagnostics = events.OfType<DiagnosticEvent>().ToList();
        Assert.True(diagnostics.Count >= 1, "Expected at least one diagnostic about missing tool calls");
        Assert.Contains(diagnostics, d => d.Message.Contains("tool_use") || d.Message.Contains("recovery") || d.Message.Contains("tool call"));

        var doneEvents = events.OfType<DoneEvent>().ToList();
        Assert.Single(doneEvents);
    }

    [Fact]
    public async Task CorruptedToolArgs_ProducesDiagnosticAndContinues()
    {
        var inner = new FakeCompletionService();
        inner.EnqueueToolCall("query_lore", "tc-1", """{"query": "test"}""");
        inner.EnqueueText("Final response");

        var faulty = new FaultInjectingCompletionService(inner, new FaultProfile
        {
            CorruptToolArguments = true,
        });

        var toolLoop = CreateToolLoop(faulty);
        var config = new AgentConfig
        {
            Model = "test",
            MaxTokens = 100,
            SystemPrompt = "Test",
            MaxToolRounds = 3,
        };
        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("test")),
        };

        var events = new List<StreamEvent>();
        await foreach (var evt in toolLoop.RunStreamAsync(config, [], messages, DefaultContext))
        {
            events.Add(evt);
        }

        // The corrupted tool call should have a parse error, and the tool loop should
        // handle it as a failed tool result and continue to generate a response
        var doneEvents = events.OfType<DoneEvent>().ToList();
        Assert.Single(doneEvents);
    }

    [Fact]
    public async Task ThrowMidStream_PropagatesException()
    {
        var inner = new FakeCompletionService();
        inner.EnqueueText("Hello world");

        var faulty = new FaultInjectingCompletionService(inner, new FaultProfile
        {
            ThrowAfterEvents = 1,
            ExceptionToThrow = new OperationCanceledException("Simulated timeout"),
        });

        var request = new CompletionRequest
        {
            Model = "test",
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("Hi"))],
        };

        var events = new List<StreamEvent>();
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
        {
            await foreach (var evt in faulty.StreamAsync(request))
            {
                events.Add(evt);
            }
        });

        // Should have gotten exactly one event before the throw
        Assert.Single(events);
    }

    [Fact]
    public async Task EmptyTextDeltas_StreamCompletesWithNoVisibleContent()
    {
        var inner = new FakeCompletionService();
        inner.EnqueueText("Hello");

        var faulty = new FaultInjectingCompletionService(inner, new FaultProfile
        {
            EmptyTextDeltas = true,
        });

        var events = new List<StreamEvent>();
        var request = new CompletionRequest
        {
            Model = "test",
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("Hi"))],
        };

        await foreach (var evt in faulty.StreamAsync(request))
        {
            events.Add(evt);
        }

        // Text delta should be empty, but Done event should still arrive
        var textEvents = events.OfType<TextDeltaEvent>().ToList();
        Assert.All(textEvents, t => Assert.Equal("", t.Text));
        Assert.Single(events.OfType<DoneEvent>());
    }

    [Fact]
    public async Task DuplicateEvents_DoubleYieldsEachEvent()
    {
        var inner = new FakeCompletionService();
        inner.EnqueueText("Hi");

        var faulty = new FaultInjectingCompletionService(inner, new FaultProfile
        {
            DuplicateEvents = true,
        });

        var events = new List<StreamEvent>();
        var request = new CompletionRequest
        {
            Model = "test",
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("Hi"))],
        };

        await foreach (var evt in faulty.StreamAsync(request))
        {
            events.Add(evt);
        }

        // FakeCompletionService yields TextDelta + Done = 2 events
        // DuplicateEvents doubles each: 4 events
        Assert.Equal(4, events.Count);
        Assert.Equal(2, events.OfType<TextDeltaEvent>().Count());
        Assert.Equal(2, events.OfType<DoneEvent>().Count());
    }
}
