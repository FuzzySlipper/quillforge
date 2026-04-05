using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Providers.Adapters;

namespace QuillForge.Providers.Tests;

public sealed class ReasoningCompletionServiceTests
{
    [Fact]
    public async Task CompleteAsync_ParsesTypedReplayEnvelope()
    {
        var handler = new RecordingHandler(
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "I found something.",
                    "reasoning_content": "Let me think this through.",
                    "tool_calls": [
                      {
                        "id": "call_1",
                        "type": "function",
                        "function": {
                          "name": "query_lore",
                          "arguments": "{\"query\":\"moon temple\"}"
                        }
                      }
                    ]
                  },
                  "finish_reason": "tool_calls"
                }
              ],
              "usage": {
                "prompt_tokens": 11,
                "completion_tokens": 7
              }
            }
            """);

        var service = CreateService(handler);

        var response = await service.CompleteAsync(new CompletionRequest
        {
            Model = "default",
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("Tell me about the moon temple."))],
        });

        Assert.Equal("tool_use", response.StopReason);
        Assert.Equal("I found something.", response.Content.GetText());

        var replay = Assert.IsType<ReasoningReplayEnvelope>(response.ProviderReplay);
        Assert.Equal("I found something.", replay.Content);
        Assert.Equal("Let me think this through.", replay.ReasoningContent);
        Assert.Single(replay.ToolCalls);
        Assert.Equal("call_1", replay.ToolCalls[0].Id);
        Assert.Equal("query_lore", replay.ToolCalls[0].Name);
        Assert.Equal("{\"query\":\"moon temple\"}", replay.ToolCalls[0].ArgumentsJson);
    }

    [Fact]
    public async Task CompleteAsync_ReplaysTypedEnvelopeIntoOutgoingRequest()
    {
        var handler = new RecordingHandler(
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "done"
                  },
                  "finish_reason": "stop"
                }
              ],
              "usage": {
                "prompt_tokens": 2,
                "completion_tokens": 1
              }
            }
            """);

        var service = CreateService(handler);

        await service.CompleteAsync(new CompletionRequest
        {
            Model = "default",
            MaxTokens = 100,
            Messages =
            [
                new CompletionMessage(
                    "assistant",
                    new MessageContent("ignored in favor of replay"))
                {
                    ProviderReplay = new ReasoningReplayEnvelope(
                        "Replayed assistant content",
                        "Replayed reasoning",
                        [new ReasoningReplayToolCall("call_9", "query_lore", "{\"query\":\"silver sea\"}")])
                }
            ],
        });

        Assert.Single(handler.RequestBodies);

        using var doc = JsonDocument.Parse(handler.RequestBodies[0]);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(1, messages.GetArrayLength());

        var replayedMessage = messages[0];
        Assert.Equal("assistant", replayedMessage.GetProperty("role").GetString());
        Assert.Equal("Replayed assistant content", replayedMessage.GetProperty("content").GetString());
        Assert.Equal("Replayed reasoning", replayedMessage.GetProperty("reasoning_content").GetString());

        var toolCall = replayedMessage.GetProperty("tool_calls")[0];
        Assert.Equal("call_9", toolCall.GetProperty("id").GetString());
        Assert.Equal("query_lore", toolCall.GetProperty("function").GetProperty("name").GetString());
        Assert.Equal("{\"query\":\"silver sea\"}", toolCall.GetProperty("function").GetProperty("arguments").GetString());
    }

    [Fact]
    public async Task StreamAsync_EmitsDoneEventWithTypedReplayEnvelope()
    {
        var handler = new RecordingHandler(
            """
            data: {"choices":[{"delta":{"content":"Lore ","reasoning_content":"Thinking ","tool_calls":[{"index":0,"id":"call_4","function":{"name":"query_lore","arguments":"{\"query\":\"sun vault\"}"}}]}}]}

            data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":13,"completion_tokens":8}}

            data: [DONE]
            """);

        var service = CreateService(handler);

        var events = new List<StreamEvent>();
        await foreach (var evt in service.StreamAsync(new CompletionRequest
        {
            Model = "default",
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("Find the sun vault."))],
        }))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e => e is TextDeltaEvent text && text.Text == "Lore ");
        Assert.Contains(events, e => e is ReasoningDeltaEvent reasoning && reasoning.Text == "Thinking ");
        Assert.Contains(events, e => e is ToolCallDeltaReceivedEvent tc && tc.ToolName == "query_lore");

        var done = Assert.IsType<DoneEvent>(events[^1]);
        var replay = Assert.IsType<ReasoningReplayEnvelope>(done.ProviderReplay);
        Assert.Equal("Lore ", replay.Content);
        Assert.Equal("Thinking ", replay.ReasoningContent);
        Assert.Single(replay.ToolCalls);
        Assert.Equal("call_4", replay.ToolCalls[0].Id);
        Assert.Equal("{\"query\":\"sun vault\"}", replay.ToolCalls[0].ArgumentsJson);
    }

    private static ReasoningCompletionService CreateService(RecordingHandler handler)
    {
        var httpClient = new HttpClient(handler);
        return new ReasoningCompletionService(
            httpClient,
            "https://example.test/v1",
            "test-key",
            "test-model",
            NullLogger<ReasoningCompletionService>.Instance);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new();

        public RecordingHandler(params string[] responseBodies)
        {
            foreach (var responseBody in responseBodies)
            {
                _responses.Enqueue(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseBody, Encoding.UTF8, "application/json"),
                });
            }
        }

        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Content is not null)
            {
                RequestBodies.Add(await request.Content.ReadAsStringAsync(cancellationToken));
            }

            if (_responses.Count == 0)
            {
                throw new InvalidOperationException("No queued HTTP responses remain.");
            }

            return _responses.Dequeue();
        }
    }
}
