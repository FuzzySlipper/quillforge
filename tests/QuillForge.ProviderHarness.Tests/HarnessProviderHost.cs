using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace QuillForge.ProviderHarness.Tests;

public sealed class HarnessProviderHost : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly WebApplication _app;

    private HarnessProviderHost(WebApplication app, IHarnessResponseSource responseSource, HarnessTraceStore traceStore)
    {
        _app = app;
        ResponseSource = responseSource;
        TraceStore = traceStore;
    }

    public IHarnessResponseSource ResponseSource { get; }
    public HarnessTraceStore TraceStore { get; }
    public required Uri BaseUri { get; init; }

    public Uri OpenAiBaseUri => new(BaseUri, "v1/");
    public Uri ModelsUri => new(OpenAiBaseUri, "models");
    public Uri ChatCompletionsUri => new(OpenAiBaseUri, "chat/completions");

    public static async Task<HarnessProviderHost> StartAsync(
        HarnessProviderScenario scenario,
        CancellationToken ct = default)
    {
        return await StartAsync(new ScriptedHarnessResponseSource(scenario), ct);
    }

    public static async Task<HarnessProviderHost> StartAsync(
        IHarnessResponseSource responseSource,
        CancellationToken ct = default)
    {
        var port = GetAvailablePort();
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(HarnessProviderHost).Assembly.GetName().Name,
            EnvironmentName = Environments.Development,
        });

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Kestrel:Endpoints:Http:Url"] = $"http://127.0.0.1:{port}",
        });

        var traceStore = new HarnessTraceStore();

        builder.Services.AddSingleton<IHarnessResponseSource>(responseSource);
        builder.Services.AddSingleton(traceStore);

        var app = builder.Build();

        app.MapGet("/v1/models", (
            IHarnessResponseSource source) =>
        {
            var payload = new JsonObject
            {
                ["object"] = "list",
                ["data"] = new JsonArray(source.Models
                    .Select(model => (JsonNode)new JsonObject
                    {
                        ["id"] = model,
                        ["object"] = "model",
                        ["created"] = 0,
                        ["owned_by"] = "quillforge-harness",
                    })
                    .ToArray()),
            };

            return Results.Text(payload.ToJsonString(JsonOptions), "application/json");
        });

        app.MapPost("/v1/chat/completions", HandleChatCompletionsAsync);

        await app.StartAsync(ct);

        var addressFeature = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        var address = addressFeature?.Addresses
            .FirstOrDefault(static candidate => candidate.StartsWith("http://", StringComparison.OrdinalIgnoreCase));
        if (address is null)
        {
            throw new InvalidOperationException("Harness provider host did not expose an HTTP address.");
        }

        return new HarnessProviderHost(app, responseSource, traceStore)
        {
            BaseUri = new Uri(address.EndsWith("/", StringComparison.Ordinal) ? address : address + "/"),
        };
    }

    public ValueTask DisposeAsync()
    {
        return _app.DisposeAsync();
    }

    private static async Task HandleChatCompletionsAsync(
        HttpContext context,
        IHarnessResponseSource responseSource,
        HarnessTraceStore traceStore,
        CancellationToken ct)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        var rawBody = await ReadBodyAsync(context.Request, ct);
        var observedRequest = HarnessObservedRequest.FromHttpRequest(context.Request, rawBody);

        var statusCode = StatusCodes.Status200OK;
        var responseMode = HarnessResponseMode.ScriptedComplete;
        string? finishReason = null;
        string? fault = null;
        string? error = null;
        var emittedFrames = new List<HarnessEmittedFrameTrace>();
        var responseTextDeltas = new List<string>();
        var responseReasoningDeltas = new List<string>();
        var emittedToolCallAccumulators = new Dictionary<string, (string Name, StringBuilder Arguments, int? Index)>(StringComparer.Ordinal);
        HarnessUsage? usage = null;
        string? finalContent = null;
        string? finalReasoning = null;
        HarnessWorkerTrace? workerTrace = null;

        try
        {
            var plan = await responseSource.GetNextResponseAsync(observedRequest, ct);
            responseMode = plan.Mode;
            statusCode = plan.StatusCode;
            finishReason = plan.FinishReason;
            usage = plan.Usage;
            workerTrace = plan.WorkerTrace;

            if (plan.InitialDelayMs > 0)
            {
                await Task.Delay(plan.InitialDelayMs, ct);
            }

            context.Response.StatusCode = plan.StatusCode;

            if (plan.Mode == HarnessResponseMode.ScriptedComplete)
            {
                context.Response.ContentType = "application/json";
                if (!string.IsNullOrEmpty(plan.Message.Content))
                {
                    responseTextDeltas.Add(plan.Message.Content);
                    finalContent = plan.Message.Content;
                }

                if (!string.IsNullOrEmpty(plan.Message.ReasoningContent))
                {
                    responseReasoningDeltas.Add(plan.Message.ReasoningContent);
                    finalReasoning = plan.Message.ReasoningContent;
                }

                foreach (var toolCall in plan.Message.ToolCalls)
                {
                    AppendToolCallChunk(emittedToolCallAccumulators, toolCall.Id, toolCall.Name, toolCall.ArgumentsJson, null);
                }
                var payload = BuildCompleteResponsePayload(plan, observedRequest.Model);
                await context.Response.WriteAsync(payload, ct);
            }
            else
            {
                context.Response.ContentType = "text/event-stream";
                context.Response.Headers.CacheControl = "no-cache";
                await context.Response.Body.FlushAsync(ct);

                var sentEvents = 0;
                var includeRole = true;

                foreach (var streamEvent in plan.StreamEvents)
                {
                    if (streamEvent.DelayMs > 0)
                    {
                        await Task.Delay(streamEvent.DelayMs, ct);
                    }

                    string frame;
                    if (!string.IsNullOrWhiteSpace(streamEvent.RawSseFrame))
                    {
                        frame = EnsureDoubleNewline(streamEvent.RawSseFrame);
                    }
                    else
                    {
                        var payload = !string.IsNullOrWhiteSpace(streamEvent.RawJson)
                            ? streamEvent.RawJson!
                            : BuildStreamEventPayload(streamEvent, observedRequest.Model, includeRole);
                        frame = $"data: {payload}\n\n";
                    }

                    includeRole = false;

                    await context.Response.WriteAsync(frame, ct);
                    await context.Response.Body.FlushAsync(ct);

                    if (!string.IsNullOrEmpty(streamEvent.TextDelta))
                    {
                        responseTextDeltas.Add(streamEvent.TextDelta);
                    }

                    if (!string.IsNullOrEmpty(streamEvent.ReasoningDelta))
                    {
                        responseReasoningDeltas.Add(streamEvent.ReasoningDelta);
                        finalReasoning = string.Concat(responseReasoningDeltas);
                    }

                    if (streamEvent.ToolCalls.Count > 0)
                    {
                        foreach (var toolCall in streamEvent.ToolCalls)
                        {
                            AppendToolCallChunk(
                                emittedToolCallAccumulators,
                                toolCall.Id,
                                toolCall.Name,
                                toolCall.ArgumentsJson,
                                toolCall.Index);
                        }
                    }

                    if (streamEvent.Usage is not null)
                    {
                        usage = streamEvent.Usage;
                    }

                    sentEvents++;
                    emittedFrames.Add(new HarnessEmittedFrameTrace(sentEvents, DateTimeOffset.UtcNow, frame));

                    if (streamEvent.FinishReason is not null)
                    {
                        finishReason = streamEvent.FinishReason;
                    }

                    finalContent = string.Concat(responseTextDeltas);

                    if (plan.DisconnectAfterEventCount.HasValue && sentEvents >= plan.DisconnectAfterEventCount.Value)
                    {
                        fault = plan.FaultLabel ?? $"disconnect_after_event_{sentEvents}";
                        break;
                    }
                }

                if (fault is null && plan.EmitDoneMarker)
                {
                    const string doneFrame = "data: [DONE]\n\n";
                    await context.Response.WriteAsync(doneFrame, ct);
                    await context.Response.Body.FlushAsync(ct);
                    emittedFrames.Add(new HarnessEmittedFrameTrace(emittedFrames.Count + 1, DateTimeOffset.UtcNow, doneFrame));
                }

                if (fault is not null)
                {
                    traceStore.Append(BuildTrace(
                        responseSource.ScenarioName,
                        startedAt,
                        stopwatch.ElapsedMilliseconds,
                        observedRequest,
                        responseMode,
                        statusCode,
                        emittedFrames,
                        responseTextDeltas,
                        responseReasoningDeltas,
                        SnapshotToolCalls(emittedToolCallAccumulators),
                        workerTrace,
                        usage,
                        finalContent,
                        finalReasoning,
                        finishReason,
                        fault,
                        error));

                    context.Abort();
                    return;
                }
            }
        }
        catch (Exception ex)
        {
            statusCode = StatusCodes.Status500InternalServerError;
            error = ex.Message;
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";

            var payload = new JsonObject
            {
                ["error"] = new JsonObject
                {
                    ["message"] = ex.Message,
                    ["type"] = "harness_error",
                },
            };

            await context.Response.WriteAsync(payload.ToJsonString(JsonOptions), ct);
        }
        finally
        {
            if (fault is null)
            {
                traceStore.Append(BuildTrace(
                    responseSource.ScenarioName,
                    startedAt,
                    stopwatch.ElapsedMilliseconds,
                    observedRequest,
                    responseMode,
                    statusCode,
                    emittedFrames,
                    responseTextDeltas,
                    responseReasoningDeltas,
                    SnapshotToolCalls(emittedToolCallAccumulators),
                    workerTrace,
                    usage,
                    finalContent,
                    finalReasoning,
                    finishReason,
                    fault,
                    error));
        }
        }
    }

    private static string BuildCompleteResponsePayload(HarnessResponsePlan plan, string? requestModel)
    {
        var message = new JsonObject
        {
            ["role"] = "assistant",
            ["content"] = plan.Message.Content,
            ["reasoning_content"] = plan.Message.ReasoningContent,
        };

        if (plan.Message.ToolCalls.Count > 0)
        {
            message["tool_calls"] = BuildToolCalls(plan.Message.ToolCalls);
        }

        var payload = new JsonObject
        {
            ["id"] = $"chatcmpl_{Guid.NewGuid():N}",
            ["object"] = "chat.completion",
            ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["model"] = requestModel ?? plan.ExpectedModel ?? "harness-scripted",
            ["choices"] = new JsonArray(
                new JsonObject
                {
                    ["index"] = 0,
                    ["message"] = message,
                    ["finish_reason"] = plan.FinishReason,
                }),
            ["usage"] = new JsonObject
            {
                ["prompt_tokens"] = plan.Usage.PromptTokens,
                ["completion_tokens"] = plan.Usage.CompletionTokens,
                ["total_tokens"] = plan.Usage.TotalTokens,
            },
        };

        return payload.ToJsonString(JsonOptions);
    }

    private static JsonArray BuildToolCalls(IReadOnlyList<HarnessToolCallPlan> toolCalls)
    {
        return new JsonArray(toolCalls
            .Select(toolCall => (JsonNode)new JsonObject
            {
                ["id"] = toolCall.Id,
                ["type"] = "function",
                ["function"] = new JsonObject
                {
                    ["name"] = toolCall.Name,
                    ["arguments"] = toolCall.ArgumentsJson,
                },
            })
            .ToArray());
    }

    private static string BuildStreamEventPayload(
        HarnessStreamEventPlan streamEvent,
        string? requestModel,
        bool includeRole)
    {
        var delta = new JsonObject();
        if (includeRole)
        {
            delta["role"] = "assistant";
        }

        if (streamEvent.TextDelta is not null)
        {
            delta["content"] = streamEvent.TextDelta;
        }

        if (streamEvent.ReasoningDelta is not null)
        {
            delta["reasoning_content"] = streamEvent.ReasoningDelta;
        }

        if (streamEvent.ToolCalls.Count > 0)
        {
            delta["tool_calls"] = new JsonArray(streamEvent.ToolCalls
                .Select(toolCall => (JsonNode)new JsonObject
                {
                    ["index"] = toolCall.Index,
                    ["id"] = toolCall.Id,
                    ["type"] = "function",
                    ["function"] = new JsonObject
                    {
                        ["name"] = toolCall.Name,
                        ["arguments"] = toolCall.ArgumentsJson,
                    },
                })
                .ToArray());
        }

        var choice = new JsonObject
        {
            ["index"] = 0,
            ["delta"] = delta,
            ["finish_reason"] = streamEvent.FinishReason,
        };

        var payload = new JsonObject
        {
            ["id"] = $"chatcmpl_{Guid.NewGuid():N}",
            ["object"] = "chat.completion.chunk",
            ["created"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ["model"] = requestModel ?? "harness-scripted",
            ["choices"] = new JsonArray(choice),
        };

        if (streamEvent.Usage is not null)
        {
            payload["usage"] = new JsonObject
            {
                ["prompt_tokens"] = streamEvent.Usage.PromptTokens,
                ["completion_tokens"] = streamEvent.Usage.CompletionTokens,
                ["total_tokens"] = streamEvent.Usage.TotalTokens,
            };
        }

        return payload.ToJsonString(JsonOptions);
    }

    private static HarnessProviderTrace BuildTrace(
        string scenarioName,
        DateTimeOffset startedAt,
        long durationMs,
        HarnessObservedRequest observedRequest,
        HarnessResponseMode responseMode,
        int statusCode,
        IReadOnlyList<HarnessEmittedFrameTrace> emittedFrames,
        IReadOnlyList<string> textDeltas,
        IReadOnlyList<string> reasoningDeltas,
        IReadOnlyList<HarnessToolCallTrace> emittedToolCalls,
        HarnessWorkerTrace? workerTrace,
        HarnessUsage? usage,
        string? finalContent,
        string? finalReasoning,
        string? finishReason,
        string? fault,
        string? error)
    {
        return new HarnessProviderTrace
        {
            TraceId = Guid.NewGuid().ToString("N"),
            ScenarioName = scenarioName,
            StartedAt = startedAt,
            CompletedAt = startedAt.AddMilliseconds(durationMs),
            Method = observedRequest.Method,
            Path = observedRequest.Path,
            RawRequestBody = observedRequest.RawBody,
            Model = observedRequest.Model,
            Stream = observedRequest.Stream,
            MessageCount = observedRequest.MessageCount,
            ToolCount = observedRequest.ToolCount,
            Messages = observedRequest.Messages,
            HasAuthorizationHeader = observedRequest.HasAuthorizationHeader,
            ContentType = observedRequest.ContentType,
            UserAgent = observedRequest.UserAgent,
            ResponseMode = responseMode,
            StatusCode = statusCode,
            EmittedFrames = emittedFrames.ToList(),
            TextDeltas = textDeltas.ToList(),
            ReasoningDeltas = reasoningDeltas.ToList(),
            EmittedToolCalls = emittedToolCalls.ToList(),
            Usage = usage,
            FinalContent = finalContent,
            FinalReasoning = finalReasoning,
            FinishReason = finishReason,
            Fault = fault,
            Error = error,
            DurationMs = durationMs,
            WorkerTrace = workerTrace,
        };
    }

    private static void AppendToolCallChunk(
        IDictionary<string, (string Name, StringBuilder Arguments, int? Index)> accumulators,
        string id,
        string name,
        string argumentsJson,
        int? index)
    {
        if (!accumulators.TryGetValue(id, out var existing))
        {
            existing = (name, new StringBuilder(), index);
        }

        existing.Arguments.Append(argumentsJson);
        accumulators[id] = (existing.Name, existing.Arguments, existing.Index ?? index);
    }

    private static List<HarnessToolCallTrace> SnapshotToolCalls(
        IDictionary<string, (string Name, StringBuilder Arguments, int? Index)> accumulators)
    {
        return accumulators
            .Select(pair => new HarnessToolCallTrace(
                pair.Key,
                pair.Value.Name,
                pair.Value.Arguments.ToString(),
                pair.Value.Index))
            .ToList();
    }

    private static async Task<string> ReadBodyAsync(HttpRequest request, CancellationToken ct)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8);
        return await reader.ReadToEndAsync(ct);
    }

    private static string EnsureDoubleNewline(string frame)
    {
        return frame.EndsWith("\n\n", StringComparison.Ordinal)
            ? frame
            : frame.TrimEnd('\n') + "\n\n";
    }

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
