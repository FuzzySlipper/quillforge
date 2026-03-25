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
        Assert.Equal("end_turn", response.StopReason);
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

        Assert.Equal("tool_use", response.StopReason);
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
        Assert.Equal("max_tokens", response.StopReason);
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
                        JsonDocument.Parse("""{"city":"London"}""").RootElement)])),
                new CompletionMessage("user", toolResultContent),
            ],
        };

        await service.CompleteAsync(request);

        // System prompt is null, so only 3 messages sent
        Assert.Equal(3, fakeClient.LastMessages!.Count);
    }
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
