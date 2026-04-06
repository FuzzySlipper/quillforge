using System.Runtime.CompilerServices;
using System.Text.Json;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests.Fakes;

/// <summary>
/// Replays a recorded <see cref="Cassette"/>, returning interactions in order.
/// Supports three timing modes for streaming: Instant, Accelerated, and RealTime.
/// </summary>
public sealed class ReplayCompletionService : ICompletionService
{
    private readonly Queue<CassetteInteraction> _interactions;
    private readonly ReplayTiming _timing;

    public ReplayCompletionService(Cassette cassette, ReplayTiming timing = ReplayTiming.Instant)
    {
        _interactions = new Queue<CassetteInteraction>(cassette.Interactions);
        _timing = timing;
    }

    public List<CompletionRequest> ReceivedRequests { get; } = [];
    public int InteractionsRemaining => _interactions.Count;

    public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
    {
        ReceivedRequests.Add(request);

        if (_interactions.Count == 0)
            throw new InvalidOperationException("Cassette exhausted: no more recorded interactions.");

        var interaction = _interactions.Dequeue();
        if (interaction is not CompleteCassetteInteraction complete)
            throw new InvalidOperationException(
                $"Expected a Complete interaction but cassette has {interaction.GetType().Name}. " +
                "Ensure the caller uses the same Complete/Stream pattern as the recorded session.");

        return Task.FromResult(RehydrateResponse(complete.Response));
    }

    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        CompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        ReceivedRequests.Add(request);

        if (_interactions.Count == 0)
            throw new InvalidOperationException("Cassette exhausted: no more recorded interactions.");

        var interaction = _interactions.Dequeue();
        if (interaction is not StreamCassetteInteraction stream)
            throw new InvalidOperationException(
                $"Expected a Stream interaction but cassette has {interaction.GetType().Name}.");

        long lastOffset = 0;
        foreach (var recorded in stream.Events)
        {
            if (_timing != ReplayTiming.Instant && recorded.OffsetMs > lastOffset)
            {
                var delay = recorded.OffsetMs - lastOffset;
                if (_timing == ReplayTiming.Accelerated) delay /= 10;
                await Task.Delay((int)delay, ct);
            }
            lastOffset = recorded.OffsetMs;

            yield return RehydrateStreamEvent(recorded);
        }
    }

    public static ReplayCompletionService FromJson(string json, ReplayTiming timing = ReplayTiming.Instant)
    {
        var cassette = JsonSerializer.Deserialize<Cassette>(json, CassetteSerializer.Options)
            ?? throw new InvalidOperationException("Failed to deserialize cassette JSON.");
        return new ReplayCompletionService(cassette, timing);
    }

    private static CompletionResponse RehydrateResponse(CassetteResponse recorded)
    {
        var blocks = recorded.Content.Select<CassetteContentBlock, ContentBlock>(b => b switch
        {
            CassetteTextBlock text => new TextBlock(text.Text),
            CassetteToolUseBlock tool => new ToolUseBlock(
                tool.Id,
                tool.Name,
                new ToolInput(JsonDocument.Parse(tool.InputJson).RootElement)),
            _ => new TextBlock(""),
        }).ToList();

        if (blocks.Count == 0)
            blocks.Add(new TextBlock(""));

        return new CompletionResponse
        {
            Content = new MessageContent(blocks),
            StopReason = StopReasonExtensions.ParseStopReason(recorded.StopReason),
            Usage = new TokenUsage(recorded.InputTokens, recorded.OutputTokens),
            Reasoning = recorded.Reasoning,
        };
    }

    private static StreamEvent RehydrateStreamEvent(CassetteStreamEvent recorded) => recorded switch
    {
        CassetteTextDeltaEvent text => new TextDeltaEvent(text.Text),
        CassetteReasoningDeltaEvent reasoning => new ReasoningDeltaEvent(reasoning.Text),
        CassetteToolCallDeltaEvent tool => new ToolCallDeltaReceivedEvent(
            tool.ToolName,
            tool.ToolId,
            JsonDocument.Parse(tool.InputJson).RootElement,
            tool.ParseError),
        CassetteDoneEvent done => new DoneEvent(
            StopReasonExtensions.ParseStopReason(done.StopReason),
            new TokenUsage(done.InputTokens, done.OutputTokens)),
        CassetteDiagnosticEvent diag => new DiagnosticEvent(
            Enum.TryParse<DiagnosticCategory>(diag.Category, ignoreCase: true, out var cat)
                ? cat : DiagnosticCategory.Warning,
            diag.Message,
            Enum.TryParse<DiagnosticLevel>(diag.Level, ignoreCase: true, out var lvl)
                ? lvl : DiagnosticLevel.Info),
        _ => new TextDeltaEvent(""),
    };
}

public enum ReplayTiming
{
    /// <summary>All events returned immediately with no delay.</summary>
    Instant,

    /// <summary>Original inter-event delays divided by 10.</summary>
    Accelerated,

    /// <summary>Original inter-event delays preserved.</summary>
    RealTime,
}
