using System.Text.Json;
using QuillForge.Core.Models;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

public sealed class CassetteTests
{
    [Fact]
    public async Task RecordAndReplay_CompleteAsync_RoundTrips()
    {
        var inner = new FakeCompletionService();
        inner.EnqueueText("Hello world", StopReason.EndTurn);

        var recorder = new RecordingCompletionService(inner, "test-provider", "test-model");

        var request = new CompletionRequest
        {
            Model = "test-model",
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("Hi"))],
        };

        var response = await recorder.CompleteAsync(request);
        Assert.Equal("Hello world", response.Content.GetText());

        // Serialize and replay
        var json = recorder.SerializeCassette();
        var replay = ReplayCompletionService.FromJson(json);

        var replayResponse = await replay.CompleteAsync(request);
        Assert.Equal("Hello world", replayResponse.Content.GetText());
        Assert.Equal(StopReason.EndTurn, replayResponse.StopReason);
        Assert.Equal(0, replay.InteractionsRemaining);
    }

    [Fact]
    public async Task RecordAndReplay_StreamAsync_PreservesEventOrder()
    {
        var inner = new FakeCompletionService();
        inner.EnqueueText("Token by token");

        var recorder = new RecordingCompletionService(inner);

        var request = new CompletionRequest
        {
            Model = "test",
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("Go"))],
        };

        // Record the stream
        var originalEvents = new List<StreamEvent>();
        await foreach (var evt in recorder.StreamAsync(request))
        {
            originalEvents.Add(evt);
        }

        Assert.Equal(2, originalEvents.Count); // TextDelta + Done

        // Serialize and replay
        var json = recorder.SerializeCassette();
        var replay = ReplayCompletionService.FromJson(json);

        var replayedEvents = new List<StreamEvent>();
        await foreach (var evt in replay.StreamAsync(request))
        {
            replayedEvents.Add(evt);
        }

        Assert.Equal(originalEvents.Count, replayedEvents.Count);
        Assert.IsType<TextDeltaEvent>(replayedEvents[0]);
        Assert.Equal("Token by token", ((TextDeltaEvent)replayedEvents[0]).Text);
        Assert.IsType<DoneEvent>(replayedEvents[1]);
        Assert.Equal(StopReason.EndTurn, ((DoneEvent)replayedEvents[1]).StopReason);
    }

    [Fact]
    public async Task RecordAndReplay_ToolCallStream_PreservesToolData()
    {
        var inner = new FakeCompletionService();
        inner.EnqueueToolCall("query_lore", "call-1", """{"query": "dragon lore"}""");
        inner.EnqueueText("The dragon lived in the mountain.");

        var recorder = new RecordingCompletionService(inner);
        var request = new CompletionRequest
        {
            Model = "test",
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("Tell me about dragons"))],
        };

        // Record two streaming interactions
        await foreach (var _ in recorder.StreamAsync(request)) { }
        await foreach (var _ in recorder.StreamAsync(request)) { }

        var json = recorder.SerializeCassette();
        var replay = ReplayCompletionService.FromJson(json);

        // Replay first interaction (tool call)
        var events1 = new List<StreamEvent>();
        await foreach (var evt in replay.StreamAsync(request))
        {
            events1.Add(evt);
        }

        var toolEvt = Assert.IsType<ToolCallDeltaReceivedEvent>(events1[0]);
        Assert.Equal("query_lore", toolEvt.ToolName);
        Assert.Equal("call-1", toolEvt.ToolId);
        Assert.Contains("dragon lore", toolEvt.Input.GetRawText());
        Assert.Equal(StopReason.ToolUse, ((DoneEvent)events1[1]).StopReason);

        // Replay second interaction (text)
        var events2 = new List<StreamEvent>();
        await foreach (var evt in replay.StreamAsync(request))
        {
            events2.Add(evt);
        }

        Assert.Equal("The dragon lived in the mountain.", ((TextDeltaEvent)events2[0]).Text);
        Assert.Equal(0, replay.InteractionsRemaining);
    }

    [Fact]
    public async Task RecordAndReplay_MultiTurnSession_PreservesSequence()
    {
        var inner = new FakeCompletionService();
        inner.EnqueueText("Response 1");
        inner.EnqueueText("Response 2");
        inner.EnqueueText("Response 3");

        var recorder = new RecordingCompletionService(inner);

        for (int i = 1; i <= 3; i++)
        {
            var req = new CompletionRequest
            {
                Model = "test",
                MaxTokens = 100,
                Messages = [new CompletionMessage("user", new MessageContent($"Turn {i}"))],
            };
            await recorder.CompleteAsync(req);
        }

        var json = recorder.SerializeCassette();
        var cassette = JsonSerializer.Deserialize<Cassette>(json, CassetteSerializer.Options)!;

        Assert.Equal(3, cassette.Interactions.Count);
        Assert.Equal(1, cassette.Version);

        var replay = new ReplayCompletionService(cassette);
        var dummyReq = new CompletionRequest
        {
            Model = "test",
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("anything"))],
        };

        var r1 = await replay.CompleteAsync(dummyReq);
        Assert.Equal("Response 1", r1.Content.GetText());

        var r2 = await replay.CompleteAsync(dummyReq);
        Assert.Equal("Response 2", r2.Content.GetText());

        var r3 = await replay.CompleteAsync(dummyReq);
        Assert.Equal("Response 3", r3.Content.GetText());
    }

    [Fact]
    public async Task Replay_ThrowsWhenCassetteExhausted()
    {
        var cassette = new Cassette();
        var replay = new ReplayCompletionService(cassette);

        var req = new CompletionRequest
        {
            Model = "test",
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("Hi"))],
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => replay.CompleteAsync(req));
    }

    [Fact]
    public async Task Replay_ThrowsOnTypeMismatch()
    {
        // Record a Complete interaction, then try to replay as Stream
        var cassette = new Cassette
        {
            Interactions =
            [
                new CompleteCassetteInteraction
                {
                    Response = new CassetteResponse
                    {
                        Content = [new CassetteTextBlock { Text = "Hello" }],
                        StopReason = "end_turn",
                    },
                },
            ],
        };

        var replay = new ReplayCompletionService(cassette);
        var req = new CompletionRequest
        {
            Model = "test",
            MaxTokens = 100,
            Messages = [new CompletionMessage("user", new MessageContent("Hi"))],
        };

        // StreamAsync should throw because the recorded interaction is Complete, not Stream
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await foreach (var _ in replay.StreamAsync(req)) { }
        });
    }

    [Fact]
    public void CassetteJson_RoundTripsCleanly()
    {
        var cassette = new Cassette
        {
            Provider = "anthropic",
            Model = "claude-sonnet",
            RecordedAt = new DateTimeOffset(2026, 4, 6, 12, 0, 0, TimeSpan.Zero),
            Interactions =
            [
                new StreamCassetteInteraction
                {
                    Request = new CassetteRequestInfo { Model = "claude-sonnet", MaxTokens = 4096, MessageCount = 3, ToolCount = 2 },
                    Events =
                    [
                        new CassetteTextDeltaEvent { OffsetMs = 0, Text = "Once" },
                        new CassetteTextDeltaEvent { OffsetMs = 50, Text = " upon" },
                        new CassetteToolCallDeltaEvent
                        {
                            OffsetMs = 100,
                            ToolName = "query_lore",
                            ToolId = "tc-1",
                            InputJson = """{"query": "dragons"}""",
                        },
                        new CassetteDoneEvent { OffsetMs = 150, StopReason = "tool_use", InputTokens = 500, OutputTokens = 100 },
                    ],
                },
                new CompleteCassetteInteraction
                {
                    Response = new CassetteResponse
                    {
                        Content =
                        [
                            new CassetteTextBlock { Text = "The dragon" },
                            new CassetteToolUseBlock { Id = "tc-2", Name = "write_prose", InputJson = """{"scene": "battle"}""" },
                        ],
                        StopReason = "end_turn",
                        InputTokens = 600,
                        OutputTokens = 200,
                    },
                },
            ],
        };

        var json = JsonSerializer.Serialize(cassette, CassetteSerializer.Options);
        var deserialized = JsonSerializer.Deserialize<Cassette>(json, CassetteSerializer.Options)!;

        Assert.Equal(cassette.Provider, deserialized.Provider);
        Assert.Equal(cassette.Model, deserialized.Model);
        Assert.Equal(2, deserialized.Interactions.Count);

        var stream = Assert.IsType<StreamCassetteInteraction>(deserialized.Interactions[0]);
        Assert.Equal(4, stream.Events.Count);
        Assert.Equal("Once", ((CassetteTextDeltaEvent)stream.Events[0]).Text);
        Assert.Equal("query_lore", ((CassetteToolCallDeltaEvent)stream.Events[2]).ToolName);

        var complete = Assert.IsType<CompleteCassetteInteraction>(deserialized.Interactions[1]);
        Assert.Equal(2, complete.Response.Content.Count);
        Assert.Equal("end_turn", complete.Response.StopReason);
    }
}
