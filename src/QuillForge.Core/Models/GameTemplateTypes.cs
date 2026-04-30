using System.Text.Json.Serialization;

namespace QuillForge.Core.Models;

/// <summary>
/// Durable reusable setup for starting a social game. Game templates are
/// QuillForge host-owned configuration; module rule authority remains in the
/// registered rules-engine module selected by <see cref="Module"/>.
/// </summary>
public sealed record GameTemplate
{
    public required string TemplateId { get; init; }

    public required string DisplayName { get; init; }

    public string? Description { get; init; }

    public required GameTemplateModuleSelection Module { get; init; }

    public string TemplateVersion { get; init; } = "1.0.0";

    public GameTemplateRulesOptions RulesOptions { get; init; } = new();

    public GameTemplateRosterSettings Roster { get; init; } = new();

    public GameTemplateMemorySettings Memory { get; init; } = new();

    public GameTemplateCommunicationSettings Communication { get; init; } = new();

    public GameTemplateNamingSettings Naming { get; init; } = new();
}

public sealed record GameTemplateSummary
{
    public required string TemplateId { get; init; }

    public required string DisplayName { get; init; }

    public required string ModuleId { get; init; }

    public required string MinimumModuleVersion { get; init; }

    public required string MaximumModuleVersion { get; init; }
}

public sealed record GameTemplateModuleSelection
{
    public required string ModuleId { get; init; }

    public required string MinimumVersion { get; init; }

    public required string MaximumVersion { get; init; }
}

public sealed record GameTemplateRulesOptions
{
    public IReadOnlyList<GameTemplateRuleOptionValue> Values { get; init; } = [];
}

public sealed record GameTemplateRuleOptionValue
{
    public required string Name { get; init; }

    public GameTemplateRuleOptionValueKind Kind { get; init; }

    public string? StringValue { get; init; }

    public int? IntValue { get; init; }

    public bool? BoolValue { get; init; }

    public string? ParticipantIdValue { get; init; }

    public IReadOnlyList<string> ParticipantSetValue { get; init; } = [];
}

[JsonConverter(typeof(JsonStringEnumConverter<GameTemplateRuleOptionValueKind>))]
public enum GameTemplateRuleOptionValueKind
{
    String,
    Int,
    Bool,
    ParticipantId,
    ParticipantSet
}

public sealed record GameTemplateRosterSettings
{
    public int RosterSize { get; init; } = 4;

    public string? UserSeatParticipantId { get; init; }

    public IReadOnlyList<GameTemplateAgentPlayerConfig> AgentPlayers { get; init; } = [];
}

public sealed record GameTemplateAgentPlayerConfig
{
    public required string ParticipantId { get; init; }

    public required string ProviderAlias { get; init; }

    public string? ModelOverride { get; init; }

    public string? CharacterPrompt { get; init; }

    public string? Personality { get; init; }

    public GamePersonaPromptSelection PersonaPrompt { get; init; } = GamePersonaPromptSelection.None;

    public string? FixedName { get; init; }

    public GamePromptTemplateSelection SystemPromptTemplate { get; init; } = GamePromptTemplateSelection.Default;

    public GameTemplateRandomNameBehavior RandomNameBehavior { get; init; } = GameTemplateRandomNameBehavior.UseFixedNameWhenProvided;
}

[JsonConverter(typeof(JsonStringEnumConverter<GameTemplateRandomNameBehavior>))]
public enum GameTemplateRandomNameBehavior
{
    UseFixedNameWhenProvided,
    AlwaysRandomize,
    NeverRandomize
}

public sealed record GameTemplateMemorySettings
{
    public int TokenBudget { get; init; } = 1024;
}

public sealed record GameTemplateCommunicationSettings
{
    public bool PublicChannelEnabled { get; init; } = true;

    public bool DirectMessagesEnabled { get; init; } = true;

    public bool HostMessagesEnabled { get; init; } = true;
}

public sealed record GameTemplateNamingSettings
{
    public bool RandomizeAgentNames { get; init; } = true;

    public string? RandomNameSet { get; init; }

    public int? RandomSeed { get; init; }
}

public sealed record GameTemplateValidationResult
{
    public IReadOnlyList<GameTemplateValidationIssue> Issues { get; init; } = [];

    public bool IsValid => Issues.Count == 0;

    public static GameTemplateValidationResult Valid { get; } = new();

    public static GameTemplateValidationResult FromIssues(IReadOnlyList<GameTemplateValidationIssue> issues) =>
        new() { Issues = issues.ToArray() };
}

public sealed record GameTemplateValidationIssue
{
    public required string Code { get; init; }

    public required string Message { get; init; }

    public string? Field { get; init; }

    public string Source { get; init; } = GameTemplateValidationSources.Template;
}

public static class GameTemplateValidationSources
{
    public const string Template = "template";
    public const string Provider = "provider";
    public const string Module = "module";
}

public sealed record GameTemplateValidationEnvelope
{
    public required GameTemplate Template { get; init; }

    public required GameTemplateValidationResult Validation { get; init; }
}
