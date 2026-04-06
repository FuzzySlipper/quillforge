using System.Runtime.CompilerServices;
using System.Text.Json;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests.Fakes;

/// <summary>
/// Wraps an ICompletionService and injects configurable faults during streaming.
/// Use <see cref="FaultProfile"/> to select which faults to apply.
/// Multiple faults can be composed (e.g., corrupt args + truncate).
/// </summary>
public sealed class FaultInjectingCompletionService : ICompletionService
{
    private readonly ICompletionService _inner;
    private readonly FaultProfile _faults;

    public FaultInjectingCompletionService(ICompletionService inner, FaultProfile faults)
    {
        _inner = inner;
        _faults = faults;
    }

    public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
    {
        // Faults only apply to streaming for now
        return _inner.CompleteAsync(request, ct);
    }

    public async IAsyncEnumerable<StreamEvent> StreamAsync(
        CompletionRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        int eventIndex = 0;

        await foreach (var evt in _inner.StreamAsync(request, ct))
        {
            // Truncate: stop yielding after N events
            if (_faults.TruncateAfterEvents is { } limit && eventIndex >= limit)
                yield break;

            // Throw mid-stream
            if (_faults.ThrowAfterEvents is { } throwAt && eventIndex == throwAt)
                throw _faults.ExceptionToThrow ?? new HttpRequestException("Simulated stream failure");

            var emitted = evt;

            // Corrupt tool arguments: keep valid JSON envelope but mark as parse-error
            if (_faults.CorruptToolArguments && emitted is ToolCallDeltaReceivedEvent tool)
            {
                emitted = new ToolCallDeltaReceivedEvent(
                    tool.ToolName,
                    tool.ToolId,
                    JsonDocument.Parse("{}").RootElement,
                    "Fault-injected: malformed arguments");
            }

            // Drop tool calls (skip the event entirely)
            if (_faults.DropToolCalls && emitted is ToolCallDeltaReceivedEvent)
            {
                eventIndex++;
                continue;
            }

            // Empty text deltas
            if (_faults.EmptyTextDeltas && emitted is TextDeltaEvent)
            {
                emitted = new TextDeltaEvent("");
            }

            // Delay between events
            if (_faults.DelayPerEventMs is { } delayMs && delayMs > 0)
            {
                await Task.Delay(delayMs, ct);
            }

            // Duplicate events
            if (_faults.DuplicateEvents)
            {
                yield return emitted;
            }

            yield return emitted;
            eventIndex++;
        }
    }
}

/// <summary>
/// Configures which faults to inject. Only non-null fields are active.
/// </summary>
public sealed record FaultProfile
{
    /// <summary>Stop yielding after this many events (simulates network disconnect).</summary>
    public int? TruncateAfterEvents { get; init; }

    /// <summary>Replace tool call argument JSON with malformed content.</summary>
    public bool CorruptToolArguments { get; init; }

    /// <summary>Drop all ToolCallDeltaReceivedEvents (tool_use stop but no payloads).</summary>
    public bool DropToolCalls { get; init; }

    /// <summary>Inject a delay (ms) between each streamed event.</summary>
    public int? DelayPerEventMs { get; init; }

    /// <summary>Yield each event twice (simulates SSE retry).</summary>
    public bool DuplicateEvents { get; init; }

    /// <summary>Replace all text deltas with empty strings.</summary>
    public bool EmptyTextDeltas { get; init; }

    /// <summary>Throw an exception after this many events.</summary>
    public int? ThrowAfterEvents { get; init; }

    /// <summary>Exception type to throw (defaults to HttpRequestException).</summary>
    public Exception? ExceptionToThrow { get; init; }
}
