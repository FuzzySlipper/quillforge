using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests.Fakes;

/// <summary>
/// Decorator that wraps a real ICompletionService and records all interactions
/// into a <see cref="Cassette"/>. The inner service handles real requests;
/// this layer captures the results for later replay.
/// </summary>
public sealed class RecordingCompletionService : ICompletionService
{
    private readonly ICompletionService _inner;
    private readonly Cassette _cassette;

    public RecordingCompletionService(ICompletionService inner, string? provider = null, string? model = null)
    {
        _inner = inner;
        _cassette = new Cassette
        {
            Provider = provider,
            Model = model,
            RecordedAt = DateTimeOffset.UtcNow,
        };
    }

    public Cassette Cassette => _cassette;

    public async Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
    {
        var response = await _inner.CompleteAsync(request, ct);

        _cassette.Interactions.Add(new CompleteCassetteInteraction
        {
            Request = CaptureRequest(request),
            Response = CaptureResponse(response),
        });

        return response;
    }

    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        CompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var interaction = new StreamCassetteInteraction
        {
            Request = CaptureRequest(request),
        };

        var sw = Stopwatch.StartNew();

        await foreach (var evt in _inner.StreamAsync(request, ct))
        {
            interaction.Events.Add(CaptureStreamEvent(evt, sw.ElapsedMilliseconds));
            yield return evt;
        }

        _cassette.Interactions.Add(interaction);
    }

    public string SerializeCassette()
    {
        return JsonSerializer.Serialize(_cassette, CassetteSerializer.Options);
    }

    private static CassetteRequestInfo CaptureRequest(CompletionRequest request) => new()
    {
        Model = request.Model,
        MaxTokens = request.MaxTokens,
        MessageCount = request.Messages.Count,
        ToolCount = request.Tools?.Count ?? 0,
    };

    private static CassetteResponse CaptureResponse(CompletionResponse response) => new()
    {
        Content = response.Content.Blocks.Select(CaptureBlock).ToList(),
        StopReason = response.StopReason.ToWireString(),
        InputTokens = response.Usage.InputTokens,
        OutputTokens = response.Usage.OutputTokens,
        Reasoning = response.Reasoning,
    };

    private static CassetteContentBlock CaptureBlock(ContentBlock block) => block switch
    {
        TextBlock text => new CassetteTextBlock { Text = text.Text },
        ToolUseBlock tool => new CassetteToolUseBlock
        {
            Id = tool.Id,
            Name = tool.Name,
            InputJson = tool.Input.GetRawText(),
        },
        _ => new CassetteTextBlock { Text = "" },
    };

    private static CassetteStreamEvent CaptureStreamEvent(StreamEvent evt, long offsetMs) => evt switch
    {
        TextDeltaEvent text => new CassetteTextDeltaEvent { OffsetMs = offsetMs, Text = text.Text },
        ReasoningDeltaEvent reasoning => new CassetteReasoningDeltaEvent { OffsetMs = offsetMs, Text = reasoning.Text },
        ToolCallDeltaReceivedEvent tool => new CassetteToolCallDeltaEvent
        {
            OffsetMs = offsetMs,
            ToolName = tool.ToolName,
            ToolId = tool.ToolId,
            InputJson = tool.Input.GetRawText(),
            ParseError = tool.ParseError,
        },
        DoneEvent done => new CassetteDoneEvent
        {
            OffsetMs = offsetMs,
            StopReason = done.StopReason.ToWireString(),
            InputTokens = done.Usage.InputTokens,
            OutputTokens = done.Usage.OutputTokens,
        },
        DiagnosticEvent diag => new CassetteDiagnosticEvent
        {
            OffsetMs = offsetMs,
            Category = diag.Category.ToString().ToLowerInvariant(),
            Message = diag.Message,
            Level = diag.Level.ToString().ToLowerInvariant(),
        },
        _ => new CassetteTextDeltaEvent { OffsetMs = offsetMs, Text = "" },
    };
}

internal static class CassetteSerializer
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };
}
