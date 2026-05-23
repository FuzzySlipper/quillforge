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

        Assert.Equal(StopReason.ToolUse, response.StopReason);
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
    public async Task CompleteAsync_AppliesRequestOptionsAndProviderQuirks()
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
                new CompletionMessage("user", new MessageContent("Use the tool.")),
                new CompletionMessage(
                    "assistant",
                    new MessageContent(
                    [
                        new ToolUseBlock(
                            "call_1",
                            "query_lore",
                            new ToolInput(Json("""{"query":"silver sea"}""")))
                    ])),
            ],
            Tools =
            [
                new ToolDefinition(
                    "query_lore",
                    "Query lore",
                    Json("""{"type":"object","properties":{},"required":[]}"""))
            ],
            TopP = 0.91,
            TopK = 42,
            FrequencyPenalty = 0.12,
            PresencePenalty = 0.34,
            RepetitionPenalty = 1.05,
            MinP = 0.03,
            Seed = 5678,
            AdditionalOptions = new Dictionary<string, JsonElement>
            {
                ["strip_empty_required"] = Json("true"),
                ["reasoning_content"] = Json("false"),
                ["num_ctx"] = Json("4096"),
                ["extra_body"] = Json("""{"reasoning":{"effort":"high"}}"""),
            },
        });

        using var doc = JsonDocument.Parse(handler.RequestBodies[0]);
        var root = doc.RootElement;

        Assert.Equal(0.91m, root.GetProperty("top_p").GetDecimal());
        Assert.Equal(42, root.GetProperty("top_k").GetInt32());
        Assert.Equal(0.12m, root.GetProperty("frequency_penalty").GetDecimal());
        Assert.Equal(0.34m, root.GetProperty("presence_penalty").GetDecimal());
        Assert.Equal(1.05m, root.GetProperty("repetition_penalty").GetDecimal());
        Assert.Equal(0.03m, root.GetProperty("min_p").GetDecimal());
        Assert.Equal(5678, root.GetProperty("seed").GetInt32());
        Assert.Equal(4096, root.GetProperty("num_ctx").GetInt32());
        Assert.Equal("high", root.GetProperty("reasoning").GetProperty("effort").GetString());

        var parameters = root.GetProperty("tools")[0].GetProperty("function").GetProperty("parameters");
        Assert.False(parameters.TryGetProperty("required", out _));

        var assistantMessage = root.GetProperty("messages")[1];
        Assert.False(assistantMessage.TryGetProperty("reasoning_content", out _));
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

    [Fact]
    public async Task StreamAsync_MalformedToolArguments_EmitDiagnosticAndMarkedToolCall()
    {
        var handler = new RecordingHandler(
            """
            data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_bad","function":{"name":"query_lore","arguments":"{\"query\": "}}]}}]}

            data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":9,"completion_tokens":4}}

            data: [DONE]
            """);

        var service = CreateService(handler);

        var events = new List<StreamEvent>();
        await foreach (var evt in service.StreamAsync(new CompletionRequest
        {
            Model = "default",
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("Find the broken payload."))],
        }))
        {
            events.Add(evt);
        }

        Assert.Contains(events, e =>
            e is DiagnosticEvent diag &&
            diag.Category == DiagnosticCategory.Tool &&
            diag.Level == DiagnosticLevel.Error &&
            diag.Message.Contains("malformed JSON arguments", StringComparison.Ordinal));

        var toolCall = Assert.Single(events.OfType<ToolCallDeltaReceivedEvent>());
        Assert.Equal("query_lore", toolCall.ToolName);
        Assert.Equal("call_bad", toolCall.ToolId);
        Assert.Equal("Provider emitted malformed JSON arguments for tool 'query_lore'.", toolCall.ParseError);
        Assert.Equal(JsonValueKind.Object, toolCall.Input.ValueKind);
        Assert.Empty(toolCall.Input.EnumerateObject().ToList());

        var done = Assert.IsType<DoneEvent>(events[^1]);
        Assert.Equal(StopReason.ToolUse, done.StopReason);
    }

    [Fact]
    public async Task StreamAsync_ToolCallWithReasoningContent_ReplaysReasoningInNextRequest()
    {
        // Round 1: model streams reasoning + tool call
        var handler = new RecordingHandler(
            """
            data: {"choices":[{"delta":{"content":"","reasoning_content":"Let me check the lore.","tool_calls":[{"index":0,"id":"call_lore","function":{"name":"query_lore","arguments":"{\"query\":\"sun vault\"}"}}]}}]}

            data: {"choices":[{"delta":{},"finish_reason":"tool_calls"}],"usage":{"prompt_tokens":10,"completion_tokens":15}}

            data: [DONE]
            """,
            // Round 2: model continues after tool result
            """
            {
              "choices": [
                {
                  "message": {
                    "role": "assistant",
                    "content": "The sun vault holds ancient treasures.",
                    "reasoning_content": "Now I can answer."
                  },
                  "finish_reason": "stop"
                }
              ],
              "usage": {
                "prompt_tokens": 20,
                "completion_tokens": 8
              }
            }
            """);

        var service = CreateService(handler);

        // --- Round 1: stream with reasoning + tool call ---
        var events = new List<StreamEvent>();
        await foreach (var evt in service.StreamAsync(new CompletionRequest
        {
            Model = "deepseek-reasoner",
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("Tell me about the sun vault."))],
        }))
        {
            events.Add(evt);
        }

        var done = Assert.IsType<DoneEvent>(events[^1]);
        var replay = Assert.IsType<ReasoningReplayEnvelope>(done.ProviderReplay);
        Assert.Equal("Let me check the lore.", replay.ReasoningContent);
        Assert.Single(replay.ToolCalls);

        // --- Round 2: send replay back in next request (non-streaming) ---
        await service.CompleteAsync(new CompletionRequest
        {
            Model = "deepseek-reasoner",
            MaxTokens = 100,
            Messages =
            [
                new CompletionMessage("user", new MessageContent("Tell me about the sun vault.")),
                new CompletionMessage(
                    "assistant",
                    new MessageContent("ignored"))
                {
                    ProviderReplay = replay,
                },
                new CompletionMessage("user", new MessageContent("The sun vault is in the east.")),
            ],
        });

        Assert.Equal(2, handler.RequestBodies.Count);

        using var doc = JsonDocument.Parse(handler.RequestBodies[1]);
        var messages = doc.RootElement.GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());

        var replayedMessage = messages[1];
        Assert.Equal("assistant", replayedMessage.GetProperty("role").GetString());
        Assert.Equal("Let me check the lore.", replayedMessage.GetProperty("reasoning_content").GetString());
        Assert.Equal("", replayedMessage.GetProperty("content").GetString());

        var toolCall = replayedMessage.GetProperty("tool_calls")[0];
        Assert.Equal("call_lore", toolCall.GetProperty("id").GetString());
        Assert.Equal("query_lore", toolCall.GetProperty("function").GetProperty("name").GetString());
    }

    [Fact]
    public async Task CompleteAsync_AssistantToolCallWithoutProviderReplay_DoesNotEmitContentNull()
    {
        // Regression for Object-vs-Null failure: assistant messages with tool calls
        // and no visible text must not serialize "content": null.
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
            Model = "deepseek-reasoner",
            MaxTokens = 100,
            Messages =
            [
                new CompletionMessage("user", new MessageContent("Use the tool.")),
                new CompletionMessage(
                    "assistant",
                    new MessageContent([
                        new ToolUseBlock(
                            "call_1",
                            "query_lore",
                            new ToolInput(Json("""{"query":"silver sea"}""")))
                    ])),
            ],
        });

        using var doc = JsonDocument.Parse(handler.RequestBodies[0]);
        var assistantMessage = doc.RootElement.GetProperty("messages")[1];
        Assert.Equal("assistant", assistantMessage.GetProperty("role").GetString());
        Assert.True(assistantMessage.TryGetProperty("content", out var contentEl));
        Assert.NotEqual(JsonValueKind.Null, contentEl.ValueKind);
        Assert.Equal("", contentEl.GetString());
        Assert.Equal("", assistantMessage.GetProperty("reasoning_content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_OldSessionTextMessage_InjectedEmptyReasoningContent()
    {
        // Old assistant text messages (created before ProviderReplay existed) should
        // still get reasoning_content injected so the provider sees a consistent shape.
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
            Model = "deepseek-reasoner",
            MaxTokens = 100,
            Messages =
            [
                new CompletionMessage("user", new MessageContent("Hello.")),
                new CompletionMessage("assistant", new MessageContent("Hello there.")),
            ],
        });

        using var doc = JsonDocument.Parse(handler.RequestBodies[0]);
        var assistantMessage = doc.RootElement.GetProperty("messages")[1];
        Assert.Equal("assistant", assistantMessage.GetProperty("role").GetString());
        Assert.Equal("Hello there.", assistantMessage.GetProperty("content").GetString());
        Assert.Equal("", assistantMessage.GetProperty("reasoning_content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_NonReasoningModel_DoesNotInjectReasoningContentByDefault()
    {
        // When no explicit reasoning_content option is set, non-reasoning models should
        // NOT have reasoning_content injected.
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
            Model = "gpt-4o",
            MaxTokens = 100,
            Messages =
            [
                new CompletionMessage("user", new MessageContent("Hello.")),
                new CompletionMessage("assistant", new MessageContent("Hello there.")),
            ],
        });

        using var doc = JsonDocument.Parse(handler.RequestBodies[0]);
        var assistantMessage = doc.RootElement.GetProperty("messages")[1];
        Assert.Equal("assistant", assistantMessage.GetProperty("role").GetString());
        Assert.False(assistantMessage.TryGetProperty("reasoning_content", out _));
    }

    [Fact]
    public async Task CompleteAsync_ReplaysTypedEnvelopeWithNullReasoning_EmitsEmptyString()
    {
        // When a replay envelope has null ReasoningContent, the adapter must still
        // emit reasoning_content (empty string) so the provider sees a consistent field.
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
            Model = "deepseek-reasoner",
            MaxTokens = 100,
            Messages =
            [
                new CompletionMessage(
                    "assistant",
                    new MessageContent("ignored"))
                {
                    ProviderReplay = new ReasoningReplayEnvelope(
                        "Replayed content",
                        null, // null reasoning — should be emitted as ""
                        []),
                }
            ],
        });

        using var doc = JsonDocument.Parse(handler.RequestBodies[0]);
        var replayedMessage = doc.RootElement.GetProperty("messages")[0];
        Assert.Equal("assistant", replayedMessage.GetProperty("role").GetString());
        Assert.Equal("Replayed content", replayedMessage.GetProperty("content").GetString());
        Assert.Equal("", replayedMessage.GetProperty("reasoning_content").GetString());
    }

    private static JsonElement Json(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        return doc.RootElement.Clone();
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
