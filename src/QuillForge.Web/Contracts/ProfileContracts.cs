namespace QuillForge.Web.Contracts;

public sealed record ProfileSwitchRequest
{
    public Guid? SessionId { get; init; }
    public string? ProfileId { get; init; }
    public string? Persona { get; init; }
    public string? Lore { get; init; }
    public string? NarrativeRules { get; init; }
    public string? WritingStyle { get; init; }
}

public sealed record ProfilesResponse
{
    public required IReadOnlyList<string> ProfileIds { get; init; }
    public required string DefaultProfileId { get; init; }
    public required string ActiveProfileId { get; init; }
    public required IReadOnlyList<string> Personas { get; init; }
    public required IReadOnlyList<string> LoreSets { get; init; }
    public required IReadOnlyList<string> NarrativeRules { get; init; }
    public required IReadOnlyList<string> WritingStyles { get; init; }
    public required string ActivePersona { get; init; }
    public required string ActiveLore { get; init; }
    public required string ActiveNarrativeRules { get; init; }
    public required string ActiveWritingStyle { get; init; }
}

public sealed record ProfileSwitchResponse
{
    public string Status { get; init; } = "ok";
    public required string ActiveProfileId { get; init; }
    public required string ActivePersona { get; init; }
    public required string ActiveLore { get; init; }
    public required string ActiveNarrativeRules { get; init; }
    public required string ActiveWritingStyle { get; init; }
    public required int LoreFiles { get; init; }
}

public sealed record ProfileConfigListResponse
{
    public required IReadOnlyList<string> Profiles { get; init; }
    public required string DefaultProfileId { get; init; }
}

public sealed record ProfileConfigResponse
{
    public required string ProfileId { get; init; }
    public required string Conductor { get; init; }
    public required string LoreSet { get; init; }
    public required string NarrativeRules { get; init; }
    public required string WritingStyle { get; init; }
}

public sealed record AgentAssignmentsResponse
{
    public required AgentModelAssignments Assignments { get; init; }
}

public sealed record AgentModelAssignments
{
    public required string Orchestrator { get; init; }
    public required string ProseWriter { get; init; }
    public required string Librarian { get; init; }
    public required string ForgeWriter { get; init; }
    public required string ForgePlanner { get; init; }
    public required string ForgeReviewer { get; init; }
    public required string Research { get; init; }
}

public sealed record AgentAssignmentsUpdateResponse
{
    public string Status { get; init; } = "ok";
    public required AgentModelAssignments Assignments { get; init; }
}
