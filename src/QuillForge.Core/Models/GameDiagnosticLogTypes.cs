using System.Text.Json.Serialization;

namespace QuillForge.Core.Models;

/// <summary>
/// Host-level diagnostic projection for local game debugging. Unlike the
/// participant table view, this surface intentionally includes private prompt
/// previews and hidden/system facts so a developer can diagnose stalls.
/// </summary>
public sealed record GameDiagnosticLogProjection
{
    public required Guid SessionId { get; init; }

    public bool HasGame { get; init; }

    public string? GameInstanceId { get; init; }

    public string? RequestedGameInstanceId { get; init; }

    public bool ScopeMatchesActiveGame { get; init; } = true;

    public string? TemplateId { get; init; }

    public string? ModuleId { get; init; }

    public string? RuntimeStatus { get; init; }

    public required string PrivacyNotice { get; init; }

    public int? Limit { get; init; }

    public long? BeforeSequence { get; init; }

    public required IReadOnlyList<GameDiagnosticLogCategory> Categories { get; init; }

    public required int TotalEventCount { get; init; }

    public required int FilteredEventCount { get; init; }

    public required int ReturnedEventCount { get; init; }

    public bool HasMore { get; init; }

    public long? NextBeforeSequence { get; init; }

    public required IReadOnlyList<GameDiagnosticLogEvent> Events { get; init; }
}

public sealed record GameDiagnosticLogQuery
{
    public const int DefaultPromptPreviewCharacters = 1200;

    public const int MaxLimit = 500;

    public int PromptPreviewCharacters { get; init; } = DefaultPromptPreviewCharacters;

    public int? Limit { get; init; }

    public long? BeforeSequence { get; init; }

    public IReadOnlyList<GameDiagnosticLogCategory> Categories { get; init; } = [];

    public string? RequestedGameInstanceId { get; init; }
}

public sealed record GameDiagnosticLogEvent
{
    public required long Sequence { get; init; }

    public required DateTimeOffset Timestamp { get; init; }

    public required GameDiagnosticLogLevel Level { get; init; }

    public required GameDiagnosticLogCategory Category { get; init; }

    public required string Source { get; init; }

    public required string Operation { get; init; }

    public required string Summary { get; init; }

    public string? ReasonCode { get; init; }

    public string? ParticipantId { get; init; }

    public string? ProviderAlias { get; init; }

    public string? Model { get; init; }

    public int? PromptTokens { get; init; }

    public int? ResponseTokens { get; init; }

    public string? PromptPreview { get; init; }

    public string? ResponsePreview { get; init; }

    public IReadOnlyDictionary<string, string?> Details { get; init; } = new Dictionary<string, string?>();
}

[JsonConverter(typeof(JsonStringEnumConverter<GameDiagnosticLogLevel>))]
public enum GameDiagnosticLogLevel
{
    Info,
    Warning,
    Error
}

[JsonConverter(typeof(JsonStringEnumConverter<GameDiagnosticLogCategory>))]
public enum GameDiagnosticLogCategory
{
    Endpoint,
    Service,
    RuntimeMutation,
    RulesEngine,
    Communication,
    LlmProvider,
    AgentPrompt,
    TokenUsage,
    Persistence,
    Rejection,
    Error
}
