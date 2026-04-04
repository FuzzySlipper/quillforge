using System.Text.Json.Serialization;

namespace QuillForge.Web.Contracts;

public sealed record ChatStreamRequest
{
    public Guid? SessionId { get; init; }
    public string Message { get; init; } = "";
    public string? Model { get; init; }
    public string? Conductor { get; set; }
    [JsonPropertyName("persona")]
    public string? LegacyPersona
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(Conductor))
            {
                Conductor = value;
            }
        }
    }
    public int? MaxTokens { get; init; }
    public Guid? ParentId { get; init; }
}

public sealed record ChatTextDeltaDto
{
    public string Type { get; init; } = "text_delta";
    public required string Text { get; init; }
}

public sealed record ChatToolDto
{
    public string Type { get; init; } = "tool";
    public required string Name { get; init; }
    public required string Id { get; init; }
}

public sealed record ChatDoneDto
{
    public string Type { get; init; } = "done";
    public required Guid SessionId { get; init; }
    public required Guid ParentId { get; init; }
    public required string Content { get; init; }
    public required string StopReason { get; init; }
    public required string ResponseType { get; init; }
    public required ChatUsageDto Usage { get; init; }
    public string? Portrait { get; init; }
    public string? UserPortrait { get; init; }
}

public sealed record ChatUsageDto
{
    public required int Input { get; init; }
    public required int Output { get; init; }
}

public sealed record ChatReasoningDeltaDto
{
    public string Type { get; init; } = "reasoning_delta";
    public required string Text { get; init; }
}

public sealed record ChatDiagnosticDto
{
    public string Type { get; init; } = "diagnostic";
    public required string Category { get; init; }
    public required string Message { get; init; }
    public required string Level { get; init; }
}

public sealed record ChatPersistedDto
{
    public string Type { get; init; } = "persisted";
    public Guid? NodeId { get; init; }
    public Guid? UserNodeId { get; init; }
}
