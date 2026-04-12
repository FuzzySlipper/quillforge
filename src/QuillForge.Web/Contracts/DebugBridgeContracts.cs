namespace QuillForge.Web.Contracts;

public sealed record DebugBridgeChatResponse
{
    public required Guid SessionId { get; init; }
    public required string ResponseText { get; init; }
    public required string StopReason { get; init; }
    public required int ToolRoundsUsed { get; init; }
    public required DebugBridgeUsageDto Usage { get; init; }
    public required string Mode { get; init; }
    public required int MessageCount { get; init; }
    public string? Reasoning { get; init; }
    public IReadOnlyList<ReasoningArtifactDto> ReasoningArtifacts { get; init; } = [];
}

public sealed record DebugBridgeUsageDto
{
    public required int InputTokens { get; init; }
    public required int OutputTokens { get; init; }
}

public sealed record DebugBridgeModeResponse
{
    public Guid? SessionId { get; init; }
    public required string Mode { get; init; }
    public string? Project { get; init; }
    public string? File { get; init; }
}

public sealed record DebugBridgeSessionResponse
{
    public required Guid SessionId { get; init; }
    public required string Name { get; init; }
    public required int MessageCount { get; init; }
    public required IEnumerable<DebugBridgeMessageDto> Messages { get; init; }
}

public sealed record DebugBridgeMessageDto
{
    public required Guid Id { get; init; }
    public required string Role { get; init; }
    public required string Content { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public string? Reasoning { get; init; }
    public IReadOnlyList<ReasoningArtifactDto> ReasoningArtifacts { get; init; } = [];
}

public sealed record DebugBridgeStateResponse
{
    public required string Mode { get; init; }
    public string? Project { get; init; }
    public string? File { get; init; }
}

/// <summary>
/// Response from POST /api/debug/bridge/chat/stream.
/// Returns the full streaming event sequence as a collected JSON array
/// so tests and scenario runners can assert without parsing SSE.
/// </summary>
public sealed record DebugBridgeStreamResponse
{
    public required Guid SessionId { get; init; }
    public required IReadOnlyList<DebugBridgeStreamEventDto> Events { get; init; }
    public required string FinalContent { get; init; }
    public string? FinalReasoning { get; init; }
    public IReadOnlyList<ReasoningArtifactDto> FinalReasoningArtifacts { get; init; } = [];
    public required DebugBridgeNodeIds NodeIds { get; init; }
    public required string Mode { get; init; }
    public required int MessageCount { get; init; }
    public required int ToolRounds { get; init; }
    public required string StopReason { get; init; }
    public required DebugBridgeUsageDto Usage { get; init; }
    public string? WriterState { get; init; }
}

public sealed record DebugBridgeNodeIds
{
    public Guid? User { get; init; }
    public Guid? Assistant { get; init; }
}

public sealed record DebugBridgeStreamEventDto
{
    public required string Type { get; init; }
    public string? Text { get; init; }
    public string? ToolName { get; init; }
    public string? ToolId { get; init; }
    public string? Category { get; init; }
    public string? Message { get; init; }
    public string? Level { get; init; }
    public string? StopReason { get; init; }
    public DebugBridgeUsageDto? Usage { get; init; }
}
