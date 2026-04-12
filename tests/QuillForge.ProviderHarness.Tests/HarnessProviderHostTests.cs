using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Providers.Registry;

namespace QuillForge.ProviderHarness.Tests;

public sealed class HarnessProviderHostTests
{
    [Fact]
    public async Task ModelsEndpoint_ReturnsOpenAiCompatibleList()
    {
        var scenario = new HarnessProviderScenario
        {
            Name = "models-list",
            Models = ["harness-basic", "harness-reasoning"],
        };

        await using var host = await HarnessProviderHost.StartAsync(scenario);
        using var httpClient = new HttpClient();

        var response = await httpClient.GetAsync(host.ModelsUri);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var models = document.RootElement.GetProperty("data").EnumerateArray()
            .Select(item => item.GetProperty("id").GetString())
            .ToList();

        Assert.Equal(["harness-basic", "harness-reasoning"], models);
    }

    [Fact]
    public async Task ProviderRegistry_CustomProvider_CallsHarnessThroughNormalConfiguration()
    {
        var scenario = new HarnessProviderScenario
        {
            Name = "custom-provider-complete",
            Models = ["harness-basic"],
            Responses =
            [
                new HarnessResponsePlan
                {
                    Mode = HarnessResponseMode.ScriptedComplete,
                    ExpectedModel = "harness-basic",
                    Message = new HarnessAssistantMessage
                    {
                        Content = "Hello from the harness provider.",
                    },
                    Usage = new HarnessUsage(14, 6),
                    FinishReason = "stop",
                },
            ],
        };

        await using var host = await HarnessProviderHost.StartAsync(scenario);
        var registry = CreateRegistry(host, "harness-basic");
        var service = registry.GetCompletionService("harness");

        var response = await service.CompleteAsync(new CompletionRequest
        {
            Model = "harness-basic",
            MaxTokens = 64,
            SystemPrompt = "Be brief.",
            Messages =
            [
                new CompletionMessage("user", new MessageContent("Say hello from the local harness.")),
            ],
        });

        Assert.Equal("Hello from the harness provider.", response.Content.GetText());
        Assert.Equal(StopReason.EndTurn, response.StopReason);
        Assert.Equal(14, response.Usage.InputTokens);
        Assert.Equal(6, response.Usage.OutputTokens);

        var trace = Assert.Single(host.TraceStore.Snapshot());
        Assert.Equal("custom-provider-complete", trace.ScenarioName);
        Assert.Equal("harness-basic", trace.Model);
        Assert.False(trace.Stream);
        Assert.Equal(2, trace.MessageCount);
        Assert.Equal(0, trace.ToolCount);
        Assert.True(trace.HasAuthorizationHeader);
        Assert.Equal(HarnessResponseMode.ScriptedComplete, trace.ResponseMode);
        Assert.Equal("stop", trace.FinishReason);
        Assert.Empty(trace.EmittedFrames);
        Assert.Equal(["Hello from the harness provider."], trace.TextDeltas);
        Assert.Equal("Hello from the harness provider.", trace.FinalContent);
        Assert.Equal("system", trace.Messages[0].Role);
        Assert.Equal("user", trace.Messages[1].Role);
        Assert.Contains("Say hello from the local harness.", trace.RawRequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProviderRegistry_ReasoningPath_ConsumesStreamedPartialToolCalls()
    {
        var scenario = new HarnessProviderScenario
        {
            Name = "reasoning-stream",
            Models = ["qwq-harness"],
            Responses =
            [
                new HarnessResponsePlan
                {
                    Mode = HarnessResponseMode.ScriptedStream,
                    ExpectedModel = "qwq-harness",
                    StreamEvents =
                    [
                        new HarnessStreamEventPlan
                        {
                            TextDelta = "Lore ",
                            ReasoningDelta = "Thinking ",
                            ToolCalls =
                            [
                                new HarnessToolCallDeltaPlan(
                                    0,
                                    "call_1",
                                    "query_lore",
                                    "{\"query\":\"sun")
                            ],
                        },
                        new HarnessStreamEventPlan
                        {
                            ToolCalls =
                            [
                                new HarnessToolCallDeltaPlan(
                                    0,
                                    "call_1",
                                    "query_lore",
                                    " vault\"}")
                            ],
                            FinishReason = "tool_calls",
                            Usage = new HarnessUsage(13, 8),
                        },
                    ],
                },
            ],
        };

        await using var host = await HarnessProviderHost.StartAsync(scenario);
        var registry = CreateRegistry(host, "qwq-harness", requiresReasoning: true);
        var service = registry.GetCompletionService("harness");

        var events = new List<StreamEvent>();
        await foreach (var streamEvent in service.StreamAsync(new CompletionRequest
        {
            Model = "qwq-harness",
            MaxTokens = 64,
            Messages =
            [
                new CompletionMessage("user", new MessageContent("Find the sun vault.")),
            ],
        }))
        {
            events.Add(streamEvent);
        }

        Assert.Contains(events, streamEvent => streamEvent is TextDeltaEvent text && text.Text == "Lore ");
        Assert.Contains(events, streamEvent => streamEvent is ReasoningDeltaEvent reasoning && reasoning.Text == "Thinking ");

        var toolCall = Assert.Single(events.OfType<ToolCallDeltaReceivedEvent>());
        Assert.Equal("query_lore", toolCall.ToolName);
        Assert.Equal("call_1", toolCall.ToolId);
        Assert.Equal("sun vault", toolCall.Input.GetProperty("query").GetString());

        var done = Assert.IsType<DoneEvent>(events[^1]);
        Assert.Equal(StopReason.ToolUse, done.StopReason);
        Assert.Equal(13, done.Usage.InputTokens);
        Assert.Equal(8, done.Usage.OutputTokens);

        var trace = Assert.Single(host.TraceStore.Snapshot());
        Assert.True(trace.Stream);
        Assert.Equal(HarnessResponseMode.ScriptedStream, trace.ResponseMode);
        Assert.Equal("tool_calls", trace.FinishReason);
        Assert.Equal(3, trace.EmittedFrames.Count);
        Assert.Equal(["Lore "], trace.TextDeltas);
        Assert.Equal(["Thinking "], trace.ReasoningDeltas);
        Assert.Equal("Lore ", trace.FinalContent);
        Assert.Equal("Thinking ", trace.FinalReasoning);
        Assert.Single(trace.EmittedToolCalls);
        Assert.Contains(trace.EmittedFrames, frame => frame.Frame.Contains("reasoning_content", StringComparison.Ordinal));
        Assert.Contains(trace.EmittedFrames, frame => frame.Frame.Contains("[DONE]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FaultInjectedStream_RecordsAbruptDisconnectTrace()
    {
        var scenario = new HarnessProviderScenario
        {
            Name = "disconnecting-stream",
            Models = ["harness-fault"],
            Responses =
            [
                new HarnessResponsePlan
                {
                    Mode = HarnessResponseMode.FaultInjected,
                    ExpectedModel = "harness-fault",
                    FaultLabel = "disconnect_after_first_chunk",
                    EmitDoneMarker = false,
                    DisconnectAfterEventCount = 1,
                    StreamEvents =
                    [
                        new HarnessStreamEventPlan
                        {
                            TextDelta = "partial payload",
                        },
                        new HarnessStreamEventPlan
                        {
                            TextDelta = "never delivered",
                        },
                    ],
                },
            ],
        };

        await using var host = await HarnessProviderHost.StartAsync(scenario);
        using var httpClient = new HttpClient();

        using var request = new HttpRequestMessage(HttpMethod.Post, host.ChatCompletionsUri)
        {
            Content = new StringContent(
                """
                {
                  "model": "harness-fault",
                  "stream": true,
                  "messages": [
                    { "role": "user", "content": "Trigger the disconnect." }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "test-key");

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        Assert.True(response.IsSuccessStatusCode);

        await using var stream = await response.Content.ReadAsStreamAsync();
        var buffer = new byte[256];

        try
        {
            while (await stream.ReadAsync(buffer) > 0)
            {
            }
        }
        catch (HttpRequestException)
        {
        }
        catch (IOException)
        {
        }

        var trace = Assert.Single(host.TraceStore.Snapshot());
        Assert.Equal(HarnessResponseMode.FaultInjected, trace.ResponseMode);
        Assert.Equal("disconnect_after_first_chunk", trace.Fault);
        Assert.Single(trace.EmittedFrames);
        Assert.DoesNotContain(trace.EmittedFrames, frame => frame.Frame.Contains("[DONE]", StringComparison.Ordinal));
    }

    private static ProviderRegistry CreateRegistry(
        HarnessProviderHost host,
        string model,
        bool requiresReasoning = false)
    {
        var appConfig = new AppConfig();
        var factory = new ProviderFactory(NullLogger<ProviderFactory>.Instance, appConfig);
        var registry = new ProviderRegistry(
            factory,
            appConfig,
            NullLogger<ProviderRegistry>.Instance,
            NullLoggerFactory.Instance);

        registry.Register(new ProviderConfig
        {
            Alias = "harness",
            Type = ProviderType.Custom,
            ApiKey = "test-key",
            BaseUrl = host.OpenAiBaseUri.ToString().TrimEnd('/'),
            ModelsUrl = host.ModelsUri.ToString(),
            DefaultModel = model,
            RequiresReasoning = requiresReasoning,
        });

        return registry;
    }
}
