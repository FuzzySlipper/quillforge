using Microsoft.AspNetCore.Http;
using System.Text.Json;

namespace QuillForge.ProviderHarness.Tests;

public enum HarnessResponseMode
{
    ScriptedComplete,
    ScriptedStream,
    FaultInjected,
}

public sealed record HarnessProviderScenario
{
    public string Name { get; init; } = "default-harness";
    public IReadOnlyList<string> Models { get; init; } = ["harness-scripted"];
    public IReadOnlyList<HarnessResponsePlan> Responses { get; init; } = [];
}

public sealed record HarnessResponsePlan
{
    public HarnessResponseMode Mode { get; init; } = HarnessResponseMode.ScriptedComplete;
    public string? ExpectedModel { get; init; }
    public int StatusCode { get; init; } = StatusCodes.Status200OK;
    public int InitialDelayMs { get; init; }
    public HarnessAssistantMessage Message { get; init; } = new();
    public IReadOnlyList<HarnessStreamEventPlan> StreamEvents { get; init; } = [];
    public HarnessUsage Usage { get; init; } = new(0, 0);
    public string FinishReason { get; init; } = "stop";
    public bool EmitDoneMarker { get; init; } = true;
    public int? DisconnectAfterEventCount { get; init; }
    public string? FaultLabel { get; init; }
    public HarnessWorkerTrace? WorkerTrace { get; init; }
}

public sealed record HarnessAssistantMessage
{
    public string? Content { get; init; }
    public string? ReasoningContent { get; init; }
    public IReadOnlyList<HarnessToolCallPlan> ToolCalls { get; init; } = [];
}

public sealed record HarnessToolCallPlan(
    string Id,
    string Name,
    string ArgumentsJson);

public sealed record HarnessToolCallTrace(
    string Id,
    string Name,
    string ArgumentsJson,
    int? Index = null);

public sealed record HarnessUsage(
    int PromptTokens,
    int CompletionTokens)
{
    public int TotalTokens => PromptTokens + CompletionTokens;
}

public sealed record HarnessStreamEventPlan
{
    public int DelayMs { get; init; }
    public string? TextDelta { get; init; }
    public string? ReasoningDelta { get; init; }
    public IReadOnlyList<HarnessToolCallDeltaPlan> ToolCalls { get; init; } = [];
    public string? FinishReason { get; init; }
    public HarnessUsage? Usage { get; init; }
    public string? RawJson { get; init; }
    public string? RawSseFrame { get; init; }
}

public sealed record HarnessToolCallDeltaPlan(
    int Index,
    string Id,
    string Name,
    string ArgumentsJson);

public sealed record HarnessObservedRequest
{
    public required string Method { get; init; }
    public required string Path { get; init; }
    public required string RawBody { get; init; }
    public required string? Model { get; init; }
    public required bool Stream { get; init; }
    public required int MessageCount { get; init; }
    public required int ToolCount { get; init; }
    public required IReadOnlyList<HarnessMessageSummary> Messages { get; init; }
    public required bool HasAuthorizationHeader { get; init; }
    public required string? ContentType { get; init; }
    public required string? UserAgent { get; init; }

    public static HarnessObservedRequest FromHttpRequest(HttpRequest request, string rawBody)
    {
        string? model = null;
        var stream = false;
        var messageSummaries = new List<HarnessMessageSummary>();
        var toolCount = 0;

        if (!string.IsNullOrWhiteSpace(rawBody))
        {
            try
            {
                using var document = JsonDocument.Parse(rawBody);
                var root = document.RootElement;

                if (root.TryGetProperty("model", out var modelElement) && modelElement.ValueKind == JsonValueKind.String)
                {
                    model = modelElement.GetString();
                }

                if (root.TryGetProperty("stream", out var streamElement) &&
                    (streamElement.ValueKind == JsonValueKind.True || streamElement.ValueKind == JsonValueKind.False))
                {
                    stream = streamElement.GetBoolean();
                }

                if (root.TryGetProperty("messages", out var messagesElement) && messagesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var message in messagesElement.EnumerateArray())
                    {
                        var role = message.TryGetProperty("role", out var roleElement) && roleElement.ValueKind == JsonValueKind.String
                            ? roleElement.GetString() ?? "unknown"
                            : "unknown";
                        var preview = message.TryGetProperty("content", out var contentElement)
                            ? ExtractContentPreview(contentElement)
                            : null;
                        messageSummaries.Add(new HarnessMessageSummary(role, preview));
                    }
                }

                if (root.TryGetProperty("tools", out var toolsElement) && toolsElement.ValueKind == JsonValueKind.Array)
                {
                    toolCount = toolsElement.GetArrayLength();
                }
            }
            catch (JsonException)
            {
                // Keep raw request body in the trace even when the request summary cannot be parsed.
            }
        }

        return new HarnessObservedRequest
        {
            Method = request.Method,
            Path = request.Path.Value ?? "/",
            RawBody = rawBody,
            Model = model,
            Stream = stream,
            MessageCount = messageSummaries.Count,
            ToolCount = toolCount,
            Messages = messageSummaries,
            HasAuthorizationHeader = request.Headers.Authorization.Count > 0,
            ContentType = request.ContentType,
            UserAgent = request.Headers.UserAgent.ToString(),
        };
    }

    private static string? ExtractContentPreview(JsonElement contentElement)
    {
        return contentElement.ValueKind switch
        {
            JsonValueKind.String => Truncate(contentElement.GetString()),
            JsonValueKind.Array => ExtractArrayPreview(contentElement),
            JsonValueKind.Object => ExtractObjectPreview(contentElement),
            _ => null,
        };
    }

    private static string? ExtractArrayPreview(JsonElement arrayElement)
    {
        foreach (var item in arrayElement.EnumerateArray())
        {
            var preview = ExtractObjectPreview(item);
            if (!string.IsNullOrWhiteSpace(preview))
            {
                return preview;
            }
        }

        return null;
    }

    private static string? ExtractObjectPreview(JsonElement objectElement)
    {
        if (objectElement.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (objectElement.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
        {
            return Truncate(textElement.GetString());
        }

        if (objectElement.TryGetProperty("content", out var contentElement))
        {
            return ExtractContentPreview(contentElement);
        }

        return null;
    }

    private static string? Truncate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        return value.Length <= 80 ? value : value[..80] + "...";
    }
}

public sealed record HarnessMessageSummary(
    string Role,
    string? Preview);

public sealed record HarnessEmittedFrameTrace(
    int Sequence,
    DateTimeOffset EmittedAt,
    string Frame);

public sealed record HarnessProviderTrace
{
    public required string TraceId { get; init; }
    public required string ScenarioName { get; init; }
    public required DateTimeOffset StartedAt { get; init; }
    public required DateTimeOffset CompletedAt { get; init; }
    public required string Method { get; init; }
    public required string Path { get; init; }
    public required string RawRequestBody { get; init; }
    public required string? Model { get; init; }
    public required bool Stream { get; init; }
    public required int MessageCount { get; init; }
    public required int ToolCount { get; init; }
    public required IReadOnlyList<HarnessMessageSummary> Messages { get; init; }
    public required bool HasAuthorizationHeader { get; init; }
    public required string? ContentType { get; init; }
    public required string? UserAgent { get; init; }
    public required HarnessResponseMode ResponseMode { get; init; }
    public required int StatusCode { get; init; }
    public required IReadOnlyList<HarnessEmittedFrameTrace> EmittedFrames { get; init; }
    public IReadOnlyList<string> TextDeltas { get; init; } = [];
    public IReadOnlyList<string> ReasoningDeltas { get; init; } = [];
    public IReadOnlyList<HarnessToolCallTrace> EmittedToolCalls { get; init; } = [];
    public HarnessUsage? Usage { get; init; }
    public string? FinalContent { get; init; }
    public string? FinalReasoning { get; init; }
    public required string? FinishReason { get; init; }
    public required string? Fault { get; init; }
    public required string? Error { get; init; }
    public required long DurationMs { get; init; }
    public HarnessWorkerTrace? WorkerTrace { get; init; }
}

public interface IHarnessResponseSource
{
    string ScenarioName { get; }
    IReadOnlyList<string> Models { get; }
    Task<HarnessResponsePlan> GetNextResponseAsync(HarnessObservedRequest request, CancellationToken ct);
}

public sealed class ScriptedHarnessResponseSource : IHarnessResponseSource
{
    private readonly Queue<HarnessResponsePlan> _responses;
    private readonly Lock _lock = new();

    public ScriptedHarnessResponseSource(HarnessProviderScenario scenario)
    {
        ScenarioName = scenario.Name;
        Models = scenario.Models.Count == 0 ? ["harness-scripted"] : scenario.Models;
        _responses = new Queue<HarnessResponsePlan>(scenario.Responses);
    }

    public string ScenarioName { get; }
    public IReadOnlyList<string> Models { get; }

    public Task<HarnessResponsePlan> GetNextResponseAsync(HarnessObservedRequest request, CancellationToken ct)
    {
        lock (_lock)
        {
            if (_responses.Count == 0)
            {
                throw new InvalidOperationException(
                    $"Harness scenario '{ScenarioName}' has no scripted responses remaining for {request.Method} {request.Path}.");
            }

            var response = _responses.Dequeue();
            if (!string.IsNullOrWhiteSpace(response.ExpectedModel) &&
                !string.Equals(response.ExpectedModel, request.Model, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Harness scenario '{ScenarioName}' expected model '{response.ExpectedModel}' but received '{request.Model ?? "(null)"}'.");
            }

            return Task.FromResult(response);
        }
    }
}

public sealed class HarnessTraceStore
{
    private readonly List<HarnessProviderTrace> _traces = [];
    private readonly Lock _lock = new();
    private readonly HarnessRunArtifactStore _artifactStore;

    public HarnessTraceStore(HarnessRunArtifactStore artifactStore)
    {
        _artifactStore = artifactStore;
    }

    public HarnessRunArtifactStore ArtifactStore => _artifactStore;

    public void Append(HarnessProviderTrace trace)
    {
        lock (_lock)
        {
            _traces.Add(trace);
            _artifactStore.PersistProviderTrace(trace);
        }
    }

    public IReadOnlyList<HarnessProviderTrace> Snapshot()
    {
        lock (_lock)
        {
            return _traces.ToList();
        }
    }
}
