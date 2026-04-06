using System.Text.Json;
using System.Text.Json.Serialization;

namespace QuillForge.Core.Tests.Fakes;

/// <summary>
/// A recorded sequence of ICompletionService interactions.
/// Cassettes are order-matched: the Nth call returns the Nth recorded interaction.
/// </summary>
public sealed class Cassette
{
    public int Version { get; set; } = 1;
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public List<CassetteInteraction> Interactions { get; set; } = [];
}

/// <summary>
/// A single recorded call to ICompletionService.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CompleteCassetteInteraction), "complete")]
[JsonDerivedType(typeof(StreamCassetteInteraction), "stream")]
public abstract class CassetteInteraction
{
    /// <summary>Request metadata (for documentation, not matching).</summary>
    public CassetteRequestInfo Request { get; set; } = new();
}

public sealed class CompleteCassetteInteraction : CassetteInteraction
{
    public CassetteResponse Response { get; set; } = new();
}

public sealed class StreamCassetteInteraction : CassetteInteraction
{
    public List<CassetteStreamEvent> Events { get; set; } = [];
}

/// <summary>
/// Recorded request metadata. Stored for debugging, not used for replay matching.
/// </summary>
public sealed class CassetteRequestInfo
{
    public string Model { get; set; } = "";
    public int MaxTokens { get; set; }
    public int MessageCount { get; set; }
    public int ToolCount { get; set; }
}

/// <summary>
/// A recorded CompletionResponse.
/// </summary>
public sealed class CassetteResponse
{
    public List<CassetteContentBlock> Content { get; set; } = [];
    public string StopReason { get; set; } = "end_turn";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public string? Reasoning { get; set; }
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CassetteTextBlock), "text")]
[JsonDerivedType(typeof(CassetteToolUseBlock), "tool_use")]
public abstract class CassetteContentBlock;

public sealed class CassetteTextBlock : CassetteContentBlock
{
    public string Text { get; set; } = "";
}

public sealed class CassetteToolUseBlock : CassetteContentBlock
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string InputJson { get; set; } = "{}";
}

/// <summary>
/// A recorded StreamEvent with relative timing.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CassetteTextDeltaEvent), "text_delta")]
[JsonDerivedType(typeof(CassetteReasoningDeltaEvent), "reasoning_delta")]
[JsonDerivedType(typeof(CassetteToolCallDeltaEvent), "tool_call_delta")]
[JsonDerivedType(typeof(CassetteDoneEvent), "done")]
[JsonDerivedType(typeof(CassetteDiagnosticEvent), "diagnostic")]
public abstract class CassetteStreamEvent
{
    /// <summary>Milliseconds since the start of this streaming interaction.</summary>
    public long OffsetMs { get; set; }
}

public sealed class CassetteTextDeltaEvent : CassetteStreamEvent
{
    public string Text { get; set; } = "";
}

public sealed class CassetteReasoningDeltaEvent : CassetteStreamEvent
{
    public string Text { get; set; } = "";
}

public sealed class CassetteToolCallDeltaEvent : CassetteStreamEvent
{
    public string ToolName { get; set; } = "";
    public string ToolId { get; set; } = "";
    public string InputJson { get; set; } = "{}";
    public string? ParseError { get; set; }
}

public sealed class CassetteDoneEvent : CassetteStreamEvent
{
    public string StopReason { get; set; } = "end_turn";
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
}

public sealed class CassetteDiagnosticEvent : CassetteStreamEvent
{
    public string Category { get; set; } = "";
    public string Message { get; set; } = "";
    public string Level { get; set; } = "info";
}
