using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
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

    private static ToolLoop CreateLoop(ICompletionService completionService)
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var continuation = new ContinuationStrategy(
            loggerFactory.CreateLogger<ContinuationStrategy>());
        return new ToolLoop(completionService, continuation,
            loggerFactory.CreateLogger<ToolLoop>(), new AppConfig());
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

        // Should have: ToolCallValidatedEvent, TextDeltaEvent, DoneEvent
        Assert.Contains(events, e => e is ToolCallValidatedEvent tc && tc.ToolName == "my_tool");
        Assert.DoesNotContain(events, e => e is ToolCallDeltaReceivedEvent);
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

    [Fact]
    public async Task StreamToolUseWithoutToolPayload_RetriesNonStreamingAndDispatches()
    {
        var fake = new RecoveryStreamingCompletionService();
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

        Assert.Contains(events, e => e is TextDeltaEvent td && td.Text == "result after tool");
        Assert.True(fake.StreamRequests.Count >= 1);
        Assert.True(fake.NonStreamingRequests.Count >= 1);
    }

    [Fact]
    public async Task StreamInvalidToolPayload_EmitsDiagnosticAndSkipsHandler()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueToolCall("my_tool", "call_1", "{}");
        fake.EnqueueText("validation handled");

        var config = DefaultConfig with { MaxToolRounds = 2 };
        var handler = new FakeToolHandler(
            "my_tool",
            (_, _, _) => Task.FromResult(ToolResult.Ok("should not run")),
            """
            {
                "type": "object",
                "properties": {
                    "topic": { "type": "string" }
                },
                "required": ["topic"]
            }
            """);

        var loop = CreateLoop(fake);
        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("use tool")),
        };

        var events = new List<StreamEvent>();
        await foreach (var evt in loop.RunStreamAsync(config, [handler], messages, DefaultContext))
        {
            events.Add(evt);
        }

        Assert.Equal(0, handler.CallCount);
        Assert.DoesNotContain(events, e => e is ToolCallValidatedEvent);
        Assert.Contains(events, e =>
            e is DiagnosticEvent diag &&
            diag.Category == "tool" &&
            diag.Level == DiagnosticLevel.Error &&
            diag.Message.Contains("invalid input", StringComparison.Ordinal));
        Assert.Contains(events, e => e is TextDeltaEvent td && td.Text == "validation handled");
    }

    [Fact]
    public async Task StreamTypedDeserializationFailure_EmitsDiagnosticAndSkipsHandler()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueToolCall("typed_tool", "call_1", """{"count":"seven"}""");
        fake.EnqueueText("typed validation handled");

        var handler = new StringSchemaIntHandler();
        var loop = CreateLoop(fake);
        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("use typed tool")),
        };

        var events = new List<StreamEvent>();
        await foreach (var evt in loop.RunStreamAsync(DefaultConfig, [handler], messages, DefaultContext))
        {
            events.Add(evt);
        }

        Assert.Equal(0, handler.TypedCallCount);
        Assert.Contains(events, e =>
            e is DiagnosticEvent diag &&
            diag.Category == "tool" &&
            diag.Level == DiagnosticLevel.Error &&
            diag.Message.Contains("invalid typed arguments", StringComparison.Ordinal));
        Assert.Contains(events, e => e is TextDeltaEvent td && td.Text == "typed validation handled");
    }

    private sealed class RecoveryStreamingCompletionService : ICompletionService
    {
        public List<CompletionRequest> StreamRequests { get; } = [];
        public List<CompletionRequest> NonStreamingRequests { get; } = [];

        public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
        {
            NonStreamingRequests.Add(request);

            if (NonStreamingRequests.Count == 1)
            {
                var toolInput = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone();
                return Task.FromResult(new CompletionResponse
                {
                    Content = new MessageContent([new ToolUseBlock("call_1", "my_tool", new ToolInput(toolInput))]),
                    StopReason = "tool_use",
                    Usage = new TokenUsage(10, 20),
                });
            }

            return Task.FromResult(new CompletionResponse
            {
                Content = new MessageContent("result after tool"),
                StopReason = "end_turn",
                Usage = new TokenUsage(10, 20),
            });
        }

        public async IAsyncEnumerable<StreamEvent> StreamAsync(
            CompletionRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            StreamRequests.Add(request);

            if (StreamRequests.Count == 1)
            {
                yield return new DoneEvent("tool_use", new TokenUsage(10, 20));
                yield break;
            }

            yield return new TextDeltaEvent("result after tool");
            yield return new DoneEvent("end_turn", new TokenUsage(10, 20));
            await Task.CompletedTask;
        }
    }

    private sealed class StringSchemaIntHandler : TypedToolHandler<StringSchemaIntArgs>
    {
        public int TypedCallCount { get; private set; }

        public override string Name => "typed_tool";

        public override ToolDefinition Definition => new(
            Name,
            "Test typed handler",
            System.Text.Json.JsonDocument.Parse(
                """
                {
                    "type": "object",
                    "properties": {
                        "count": { "type": "string" }
                    },
                    "required": ["count"]
                }
                """).RootElement);

        protected override Task<ToolResult> HandleTypedAsync(StringSchemaIntArgs input, AgentContext context, CancellationToken ct = default)
        {
            TypedCallCount++;
            return Task.FromResult(ToolResult.Ok(input.Count.ToString()));
        }
    }

    private sealed record StringSchemaIntArgs
    {
        public int Count { get; init; }
    }
}
