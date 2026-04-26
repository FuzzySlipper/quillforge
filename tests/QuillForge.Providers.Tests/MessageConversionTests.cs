using System.Text.Json;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Providers.Adapters;

namespace QuillForge.Providers.Tests;

/// <summary>
/// Tests for message format conversion between Core types and Microsoft.Extensions.AI types.
/// Uses a FakeIChatClient to verify the adapter translates correctly.
/// </summary>
public class MessageConversionTests
{
    private static ChatResponse MakeResponse(string text, ChatFinishReason reason, int inTokens = 5, int outTokens = 3)
    {
        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
        response.FinishReason = reason;
        response.Usage = new UsageDetails { InputTokenCount = inTokens, OutputTokenCount = outTokens };
        return response;
    }

    [Fact]
    public async Task SimpleTextMessage_ConvertedCorrectly()
    {
        var fakeClient = new FakeChatClient(MakeResponse("Hello!", ChatFinishReason.Stop));

        var service = new ChatClientCompletionService(fakeClient,
            NullLoggerFactory.Instance.CreateLogger<ChatClientCompletionService>());

        var request = new CompletionRequest
        {
            Model = "test",
            MaxTokens = 100,
            SystemPrompt = "Be helpful.",
            Messages = [new CompletionMessage("user", new MessageContent("Hi"))],
        };

        var response = await service.CompleteAsync(request);

        Assert.Equal("Hello!", response.Content.GetText());
        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Equal(5, response.Usage.InputTokens);
        Assert.Equal(3, response.Usage.OutputTokens);
    }

    [Fact]
    public async Task ToolCallResponse_ConvertedCorrectly()
    {
        var funcCall = new FunctionCallContent("call_123", "get_weather",
            new Dictionary<string, object?> { ["city"] = "London" });

        var chatResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, [funcCall])]);
        chatResponse.FinishReason = ChatFinishReason.ToolCalls;
        chatResponse.Usage = new UsageDetails { InputTokenCount = 10, OutputTokenCount = 20 };

        var fakeClient = new FakeChatClient(chatResponse);
        var service = new ChatClientCompletionService(fakeClient,
            NullLoggerFactory.Instance.CreateLogger<ChatClientCompletionService>());

        var request = new CompletionRequest
        {
            Model = "test",
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("weather?"))],
        };

        var response = await service.CompleteAsync(request);

        Assert.Equal(StopReason.ToolUse, response.StopReason);
        var toolCalls = response.Content.GetToolCalls().ToList();
        Assert.Single(toolCalls);
        Assert.Equal("get_weather", toolCalls[0].Name);
        Assert.Equal("call_123", toolCalls[0].Id);
    }

    [Fact]
    public async Task MaxTokensFinishReason_MappedCorrectly()
    {
        var fakeClient = new FakeChatClient(MakeResponse("Truncated text", ChatFinishReason.Length, 10, 10));

        var service = new ChatClientCompletionService(fakeClient,
            NullLoggerFactory.Instance.CreateLogger<ChatClientCompletionService>());

        var request = new CompletionRequest
        {
            Model = "test",
            MaxTokens = 10,
            Messages = [new CompletionMessage("user", new MessageContent("Tell me everything"))],
        };

        var response = await service.CompleteAsync(request);
        Assert.Equal(StopReason.MaxTokens, response.StopReason);
    }

    [Fact]
    public async Task SystemPrompt_SentAsSystemMessage()
    {
        var fakeClient = new FakeChatClient(MakeResponse("ok", ChatFinishReason.Stop));

        var service = new ChatClientCompletionService(fakeClient,
            NullLoggerFactory.Instance.CreateLogger<ChatClientCompletionService>());

        var request = new CompletionRequest
        {
            Model = "test",
            MaxTokens = 100,
            SystemPrompt = "You are a pirate.",
            Messages = [new CompletionMessage("user", new MessageContent("Hello"))],
        };

        await service.CompleteAsync(request);

        var sentMessages = fakeClient.LastMessages!;
        Assert.Equal(ChatRole.System, sentMessages[0].Role);
        Assert.Contains("pirate", sentMessages[0].Text!);
    }

    [Fact]
    public async Task SamplingOptions_SentAsChatOptions()
    {
        var fakeClient = new FakeChatClient(MakeResponse("ok", ChatFinishReason.Stop));

        var service = new ChatClientCompletionService(fakeClient,
            NullLoggerFactory.Instance.CreateLogger<ChatClientCompletionService>());

        var request = new CompletionRequest
        {
            Model = "test",
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("Hello"))],
            Temperature = 0.8,
            TopP = 0.9,
            TopK = 40,
            FrequencyPenalty = 0.2,
            PresencePenalty = 0.3,
            Seed = 1234,
        };

        await service.CompleteAsync(request);

        var options = Assert.IsType<ChatOptions>(fakeClient.LastOptions);
        Assert.Equal(0.8f, options.Temperature);
        Assert.Equal(0.9f, options.TopP);
        Assert.Equal(40, options.TopK);
        Assert.Equal(0.2f, options.FrequencyPenalty);
        Assert.Equal(0.3f, options.PresencePenalty);
        Assert.Equal(1234, options.Seed);
    }

    [Fact]
    public async Task ToolResultMessage_ConvertedToFunctionResult()
    {
        var fakeClient = new FakeChatClient(MakeResponse("The weather is sunny.", ChatFinishReason.Stop));

        var service = new ChatClientCompletionService(fakeClient,
            NullLoggerFactory.Instance.CreateLogger<ChatClientCompletionService>());

        var toolResultContent = new MessageContent(
            [new ToolResultBlock("call_123", "sunny, 22°C")]);

        var request = new CompletionRequest
        {
            Model = "test",
            MaxTokens = 100,
            Messages =
            [
                new CompletionMessage("user", new MessageContent("weather?")),
                new CompletionMessage("assistant", new MessageContent(
                    [new ToolUseBlock("call_123", "get_weather",
                        new ToolInput(JsonDocument.Parse("""{"city":"London"}""").RootElement))])),
                new CompletionMessage("user", toolResultContent),
            ],
        };

        await service.CompleteAsync(request);

        // System prompt is null, so only 3 messages sent
        Assert.Equal(3, fakeClient.LastMessages!.Count);
    }

    [Fact]
    public async Task UnknownFinishReason_StreamingPath_TreatedAsEndTurn()
    {
        // Simulate a provider that returns text content, then throws on the final
        // chunk because the finish_reason is unrecognized by the OpenAI SDK.
        var fakeClient = new ThrowingFinishReasonChatClient(
            textToYield: "Chapter one content",
            throwOnStreaming: true,
            throwOnNonStreaming: false);

        var service = new ChatClientCompletionService(fakeClient,
            NullLoggerFactory.Instance.CreateLogger<ChatClientCompletionService>());

        var request = new CompletionRequest
        {
            Model = "test",
            MaxTokens = 1000,
            Messages = [new CompletionMessage("user", new MessageContent("Write chapter 1"))],
        };

        var events = new List<StreamEvent>();
        await foreach (var evt in service.StreamAsync(request))
        {
            events.Add(evt);
        }

        // Text content should be preserved from before the exception
        var textEvents = events.OfType<TextDeltaEvent>().ToList();
        Assert.NotEmpty(textEvents);
        Assert.Equal("Chapter one content", string.Concat(textEvents.Select(e => e.Text)));

        // Should end with DoneEvent defaulting to EndTurn
        var doneEvent = events.OfType<DoneEvent>().Single();
        Assert.Equal(StopReason.EndTurn, doneEvent.StopReason);
    }

    [Fact]
    public async Task UnknownFinishReason_NonStreamingPath_FallsBackToStreaming()
    {
        // Non-streaming GetResponseAsync throws, but streaming works (content is
        // available before the finish_reason chunk fails).
        var fakeClient = new ThrowingFinishReasonChatClient(
            textToYield: "Design output here",
            throwOnStreaming: true,
            throwOnNonStreaming: true);

        var service = new ChatClientCompletionService(fakeClient,
            NullLoggerFactory.Instance.CreateLogger<ChatClientCompletionService>());

        var request = new CompletionRequest
        {
            Model = "test",
            MaxTokens = 1000,
            Messages = [new CompletionMessage("user", new MessageContent("Design the story"))],
        };

        var response = await service.CompleteAsync(request);

        // Content should be recovered via the streaming fallback
        Assert.Equal("Design output here", response.Content.GetText());
        Assert.Equal(StopReason.EndTurn, response.StopReason);
    }
}

/// <summary>
/// Fake IChatClient that throws ArgumentOutOfRangeException (simulating the OpenAI SDK's
/// behavior when it encounters an unrecognized finish_reason like "end_turn").
/// </summary>
internal sealed class ThrowingFinishReasonChatClient : IChatClient
{
    private readonly string _textToYield;
    private readonly bool _throwOnStreaming;
    private readonly bool _throwOnNonStreaming;

    public ThrowingFinishReasonChatClient(string textToYield, bool throwOnStreaming, bool throwOnNonStreaming)
    {
        _textToYield = textToYield;
        _throwOnStreaming = throwOnStreaming;
        _throwOnNonStreaming = throwOnNonStreaming;
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (_throwOnNonStreaming)
            throw new ArgumentOutOfRangeException("value", "end_turn", "Unknown ChatFinishReason value.");
        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, _textToYield)]);
        response.FinishReason = ChatFinishReason.Stop;
        return Task.FromResult(response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return StreamUpdates();
    }

    private async IAsyncEnumerable<ChatResponseUpdate> StreamUpdates()
    {
        // Yield the text content first
        yield return new ChatResponseUpdate
        {
            Role = ChatRole.Assistant,
            Contents = [new TextContent(_textToYield)],
        };

        await Task.CompletedTask;

        // Then throw on what would be the finish_reason chunk
        if (_throwOnStreaming)
            throw new ArgumentOutOfRangeException("value", "end_turn", "Unknown ChatFinishReason value.");

        yield return new ChatResponseUpdate
        {
            FinishReason = ChatFinishReason.Stop,
        };
    }

    public void Dispose() { }
    public object? GetService(Type serviceType, object? serviceKey = null) => null;
}

/// <summary>
/// Simple fake IChatClient for testing the adapter without hitting real APIs.
/// </summary>
internal sealed class FakeChatClient : IChatClient
{
    private readonly ChatResponse _response;

    public FakeChatClient(ChatResponse response)
    {
        _response = response;
    }

    public IList<ChatMessage>? LastMessages { get; private set; }
    public ChatOptions? LastOptions { get; private set; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        LastMessages = messages.ToList();
        LastOptions = options;
        return Task.FromResult(_response);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return ToAsyncEnumerable(_response.ToChatResponseUpdates());
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ToAsyncEnumerable(ChatResponseUpdate[] updates)
    {
        foreach (var update in updates)
        {
            yield return update;
        }
        await Task.CompletedTask;
    }

    public void Dispose() { }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
}
