using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

public class ToolLoopTests
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
            loggerFactory.CreateLogger<ToolLoop>(), new AppConfig());
    }

    [Fact]
    public async Task SimpleTextResponse_ReturnsImmediately()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("Hello world!");
        var loop = CreateLoop(fake);

        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("hi")),
        };

        var result = await loop.RunAsync(DefaultConfig, [], messages, DefaultContext);

        Assert.Equal("Hello world!", result.Content.GetText());
        Assert.Equal("end_turn", result.StopReason);
        Assert.Equal(0, result.ToolRoundsUsed);
        Assert.Single(fake.ReceivedRequests);
    }

    [Fact]
    public async Task SingleToolCall_DispatchesAndReturns()
    {
        var fake = new FakeCompletionService();
        // Round 1: model calls a tool
        fake.EnqueueToolCall("get_weather", "call_1", """{"city":"London"}""");
        // Round 2: model returns text after seeing tool result
        fake.EnqueueText("The weather in London is sunny.");

        var handler = new FakeToolHandler("get_weather",
            (input, _, _) => Task.FromResult(ToolResult.Ok("sunny, 22°C")));

        var loop = CreateLoop(fake);
        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("what's the weather?")),
        };

        var result = await loop.RunAsync(DefaultConfig, [handler], messages, DefaultContext);

        Assert.Equal("The weather in London is sunny.", result.Content.GetText());
        Assert.Equal(1, result.ToolRoundsUsed);
        Assert.Equal(2, fake.ReceivedRequests.Count);

        // Verify the tool result was appended back to messages
        // messages should be: user, assistant(tool_use), user(tool_result), (and the loop added these)
        Assert.True(messages.Count >= 3);
    }

    [Fact]
    public async Task MultipleToolRounds_ChainCorrectly()
    {
        var fake = new FakeCompletionService();
        // Round 1: tool call
        fake.EnqueueToolCall("step_one", "call_1", "{}");
        // Round 2: another tool call
        fake.EnqueueToolCall("step_two", "call_2", "{}");
        // Round 3: final text
        fake.EnqueueText("All done.");

        var step1 = new FakeToolHandler("step_one");
        var step2 = new FakeToolHandler("step_two");

        var loop = CreateLoop(fake);
        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("do the thing")),
        };

        var result = await loop.RunAsync(DefaultConfig, [step1, step2], messages, DefaultContext);

        Assert.Equal("All done.", result.Content.GetText());
        Assert.Equal(2, result.ToolRoundsUsed);
        Assert.Equal(3, fake.ReceivedRequests.Count);
    }

    [Fact]
    public async Task MaxRoundsEnforced_StopsLoop()
    {
        var fake = new FakeCompletionService();
        // Enqueue more tool calls than max rounds allows
        for (var i = 0; i < 10; i++)
        {
            fake.EnqueueToolCall("infinite", $"call_{i}", "{}");
        }

        var config = DefaultConfig with { MaxToolRounds = 3 };
        var handler = new FakeToolHandler("infinite");
        var loop = CreateLoop(fake);
        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("loop forever")),
        };

        var result = await loop.RunAsync(config, [handler], messages, DefaultContext);

        Assert.Equal("max_rounds", result.StopReason);
        // Should have stopped after 3 tool rounds, meaning 4 requests total:
        // initial + 3 rounds of tool dispatch
        Assert.True(fake.ReceivedRequests.Count <= 4);
    }

    [Fact]
    public async Task UnknownTool_ReturnsError_InToolResult()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueToolCall("nonexistent_tool", "call_1", "{}");
        fake.EnqueueText("I see the tool failed.");

        var loop = CreateLoop(fake);
        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("call a bad tool")),
        };

        var result = await loop.RunAsync(DefaultConfig, [], messages, DefaultContext);

        Assert.Equal("I see the tool failed.", result.Content.GetText());
        // The error should have been sent back as a tool_result
        var toolResultMsg = messages.FirstOrDefault(m =>
            m.Content.Blocks.OfType<ToolResultBlock>().Any());
        Assert.NotNull(toolResultMsg);
        var toolResult = toolResultMsg.Content.Blocks.OfType<ToolResultBlock>().First();
        Assert.True(toolResult.IsError);
        Assert.Contains("not found", toolResult.Content);
    }

    [Fact]
    public async Task ToolHandlerException_ReturnsFail_InToolResult()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueToolCall("exploder", "call_1", "{}");
        fake.EnqueueText("I handled the error.");

        var handler = new FakeToolHandler("exploder",
            (_, _, _) => throw new InvalidOperationException("kaboom"));

        var loop = CreateLoop(fake);
        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("explode")),
        };

        var result = await loop.RunAsync(DefaultConfig, [handler], messages, DefaultContext);

        Assert.Equal("I handled the error.", result.Content.GetText());
        var toolResultMsg = messages.FirstOrDefault(m =>
            m.Content.Blocks.OfType<ToolResultBlock>().Any());
        Assert.NotNull(toolResultMsg);
        var toolResult = toolResultMsg.Content.Blocks.OfType<ToolResultBlock>().First();
        Assert.True(toolResult.IsError);
        Assert.Contains("kaboom", toolResult.Content);
    }

    [Fact]
    public async Task InvalidToolPayload_FailsAtBoundary_BeforeHandlerRuns()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueToolCall("get_weather", "call_1", "{}");
        fake.EnqueueText("I handled the validation error.");

        var handler = new FakeToolHandler(
            "get_weather",
            (_, _, _) => Task.FromResult(ToolResult.Ok("sunny")),
            """
            {
                "type": "object",
                "properties": {
                    "city": { "type": "string" }
                },
                "required": ["city"]
            }
            """);

        var loop = CreateLoop(fake);
        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("what's the weather?")),
        };

        var result = await loop.RunAsync(DefaultConfig, [handler], messages, DefaultContext);

        Assert.Equal("I handled the validation error.", result.Content.GetText());
        Assert.Equal(0, handler.CallCount);

        var toolResult = messages
            .SelectMany(m => m.Content.Blocks.OfType<ToolResultBlock>())
            .Single();

        Assert.True(toolResult.IsError);
        Assert.Contains("invalid input", toolResult.Content);
        Assert.Contains("$.city is required", toolResult.Content);
    }

    [Fact]
    public async Task SchemaValidButTypedDeserializationFailure_ReturnsSanitizedToolResult()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueToolCall("typed_tool", "call_1", """{"count":"seven"}""");
        fake.EnqueueText("I handled the typed-args error.");

        var handler = new StringSchemaIntHandler();
        var loop = CreateLoop(fake);
        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("call the typed tool")),
        };

        var result = await loop.RunAsync(DefaultConfig, [handler], messages, DefaultContext);

        Assert.Equal("I handled the typed-args error.", result.Content.GetText());
        Assert.Equal(0, handler.TypedCallCount);

        var toolResult = messages
            .SelectMany(m => m.Content.Blocks.OfType<ToolResultBlock>())
            .Single();

        Assert.True(toolResult.IsError);
        Assert.Equal("Tool 'typed_tool' received invalid typed arguments.", toolResult.Content);
    }

    [Fact]
    public async Task MaxTokensContinuation_MergesResponses()
    {
        var fake = new FakeCompletionService();
        // First response truncated at max_tokens
        fake.EnqueueText("The beginning of a long ", "max_tokens");
        // Continuation completes
        fake.EnqueueText("story about dragons.", "end_turn");

        var loop = CreateLoop(fake);
        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("tell me a story")),
        };

        var result = await loop.RunAsync(DefaultConfig, [], messages, DefaultContext);

        // The loop should have auto-continued and we get the final text
        Assert.Equal("end_turn", result.StopReason);
        Assert.Equal(2, fake.ReceivedRequests.Count);
    }

    [Fact]
    public async Task Cancellation_Throws()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("should not reach this");

        var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var loop = CreateLoop(fake);
        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("hi")),
        };

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            loop.RunAsync(DefaultConfig, [], messages, DefaultContext, cts.Token));
    }

    [Fact]
    public async Task SystemPrompt_PassedToCompletionRequest()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("ok");

        var loop = CreateLoop(fake);
        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("hi")),
        };

        await loop.RunAsync(DefaultConfig, [], messages, DefaultContext);

        Assert.Single(fake.ReceivedRequests);
        Assert.Equal("You are a test agent.", fake.ReceivedRequests[0].SystemPrompt);
    }

    [Fact]
    public async Task ToolDefinitions_PassedToCompletionRequest()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("ok");

        var handler = new FakeToolHandler("my_tool");
        var loop = CreateLoop(fake);
        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("hi")),
        };

        await loop.RunAsync(DefaultConfig, [handler], messages, DefaultContext);

        var req = fake.ReceivedRequests[0];
        Assert.NotNull(req.Tools);
        Assert.Single(req.Tools);
        Assert.Equal("my_tool", req.Tools[0].Name);
    }

    private sealed class StringSchemaIntHandler : TypedToolHandler<StringSchemaIntArgs>
    {
        public int TypedCallCount { get; private set; }

        public override string Name => "typed_tool";

        public override ToolDefinition Definition => new(
            Name,
            "Test typed handler",
            JsonDocument.Parse(
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
