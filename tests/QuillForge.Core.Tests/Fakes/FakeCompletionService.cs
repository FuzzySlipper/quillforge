using System.Runtime.CompilerServices;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests.Fakes;

/// <summary>
/// A scriptable fake completion service. Enqueue responses and they'll be returned in order.
/// </summary>
public sealed class FakeCompletionService : ICompletionService
{
    private readonly Queue<CompletionResponse> _responses = new();

    public List<CompletionRequest> ReceivedRequests { get; } = [];

    public void EnqueueResponse(CompletionResponse response)
    {
        _responses.Enqueue(response);
    }

    /// <summary>
    /// Helper: enqueue a simple text response with end_turn.
    /// </summary>
    public void EnqueueText(string text, string stopReason = "end_turn")
    {
        EnqueueResponse(new CompletionResponse
        {
            Content = new MessageContent(text),
            StopReason = stopReason,
            Usage = new TokenUsage(10, 20),
        });
    }

    /// <summary>
    /// Helper: enqueue a response that contains a tool_use block.
    /// </summary>
    public void EnqueueToolCall(string toolName, string toolId, string inputJson, string stopReason = "tool_use")
    {
        var input = System.Text.Json.JsonDocument.Parse(inputJson).RootElement;
        var content = new MessageContent([new ToolUseBlock(toolId, toolName, input)]);
        EnqueueResponse(new CompletionResponse
        {
            Content = content,
            StopReason = stopReason,
            Usage = new TokenUsage(10, 20),
        });
    }

    public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
    {
        ReceivedRequests.Add(request);
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                "FakeCompletionService has no more responses. " +
                $"This was request #{ReceivedRequests.Count}.");
        }
        return Task.FromResult(_responses.Dequeue());
    }

    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        CompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var response = await CompleteAsync(request, ct);

        foreach (var block in response.Content.Blocks)
        {
            switch (block)
            {
                case TextBlock text:
                    yield return new TextDeltaEvent(text.Text);
                    break;
                case ToolUseBlock tool:
                    yield return new ToolCallEvent(tool.Name, tool.Id, tool.Input);
                    break;
            }
        }

        yield return new DoneEvent(response.StopReason, response.Usage);
    }
}
