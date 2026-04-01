namespace QuillForge.Web.Contracts;

public sealed record StatusResponse
{
    public string Status { get; init; } = "ready";
    public required string Version { get; init; }
    public required string Build { get; init; }
    public required string Mode { get; init; }
    public string? Project { get; init; }
    public string? File { get; init; }
    public required string LoreSet { get; init; }
    public required string Persona { get; init; }
    public required string WritingStyle { get; init; }
    public required string Model { get; init; }
    public required string Layout { get; init; }
    public required string AiCharacter { get; init; }
    public required string UserCharacter { get; init; }
    public int ConversationTurns { get; init; }
    public required int LoreFiles { get; init; }
    public int ContextLimit { get; init; }
    public required int LoreTokens { get; init; }
    public required int PersonaTokens { get; init; }
    public int HistoryTokens { get; init; }
    public required bool DiagnosticsLivePanel { get; init; }
    public UpdateInfoDto? Update { get; init; }
}

public sealed record UpdateInfoDto
{
    public bool Available { get; init; } = true;
    public string? Version { get; init; }
    public string? Url { get; init; }
}
