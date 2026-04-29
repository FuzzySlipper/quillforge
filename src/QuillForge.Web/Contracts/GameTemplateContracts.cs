using QuillForge.Core.Models;

namespace QuillForge.Web.Contracts;

public sealed record GameTemplateListResponse
{
    public required IReadOnlyList<GameTemplateSummary> Templates { get; init; }
}

public sealed record GameTemplateCatalogResponse
{
    public required IReadOnlyList<GameTemplateModuleOption> Modules { get; init; }

    public required IReadOnlyList<GameTemplateProviderOption> Providers { get; init; }
}

public sealed record GameTemplateModuleOption
{
    public required string ModuleId { get; init; }

    public required string ModuleVersion { get; init; }

    public required string DisplayName { get; init; }

    public required string MinimumTemplateVersion { get; init; }

    public required string MaximumTemplateVersion { get; init; }

    public required int MinimumPlayers { get; init; }

    public required int MaximumPlayers { get; init; }

    public required IReadOnlyList<GameTemplateSetupFieldOption> SetupFields { get; init; }

    public required IReadOnlyList<GameTemplateStageHookOption> Stages { get; init; }

    public required IReadOnlyList<GameTemplateActionFormOption> ActionForms { get; init; }

    public required IReadOnlyList<GameTemplatePromptAssetOption> PromptAssets { get; init; }

    public required GameTemplateCommunicationCapabilitiesOption CommunicationCapabilities { get; init; }

    public required GameTemplateMemoryExpectationsOption MemoryExpectations { get; init; }

    public required GameTemplateParticipantRequirementsOption ParticipantRequirements { get; init; }

    public required GameTemplateProjectionCapabilitiesOption ProjectionCapabilities { get; init; }
}

public sealed record GameTemplateSetupFieldOption
{
    public required string Name { get; init; }

    public required string ValueKind { get; init; }

    public required bool IsRequired { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }
}

public sealed record GameTemplateStageHookOption
{
    public required string StageId { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required int Sequence { get; init; }

    public required bool AllowsPublicMessages { get; init; }

    public required bool AllowsDirectMessages { get; init; }
}

public sealed record GameTemplateActionFormOption
{
    public required string IntentName { get; init; }

    public required string StageId { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }

    public required string Layout { get; init; }

    public required IReadOnlyList<GameTemplateActionFieldOption> Fields { get; init; }
}

public sealed record GameTemplateActionFieldOption
{
    public required string Name { get; init; }

    public required string ValueKind { get; init; }

    public required bool IsRequired { get; init; }

    public required string DisplayName { get; init; }

    public required string Description { get; init; }
}

public sealed record GameTemplatePromptAssetOption
{
    public required string AssetId { get; init; }

    public required string Kind { get; init; }

    public required bool IsRequired { get; init; }
}

public sealed record GameTemplateCommunicationCapabilitiesOption
{
    public required bool AllowsPublicChannelMessages { get; init; }

    public required bool AllowsDirectMessages { get; init; }
}

public sealed record GameTemplateMemoryExpectationsOption
{
    public required bool UsesRoundSummaries { get; init; }

    public required int SuggestedSummaryTokenBudget { get; init; }

    public required int MaximumRetainedRoundSummaries { get; init; }
}

public sealed record GameTemplateParticipantRequirementsOption
{
    public required bool AllowsHumanParticipants { get; init; }

    public required bool AllowsAgentParticipants { get; init; }

    public required bool AllowsSystemParticipants { get; init; }

    public required int MinimumHumanParticipants { get; init; }

    public required int MinimumAgentParticipants { get; init; }
}

public sealed record GameTemplateProjectionCapabilitiesOption
{
    public required bool SupportsPublicEventProjection { get; init; }

    public required bool SupportsParticipantPrivateProjection { get; init; }

    public required bool SupportsHostInspectorProjection { get; init; }
}

public sealed record GameTemplateProviderOption
{
    public required string Alias { get; init; }

    public required string Type { get; init; }

    public string? Model { get; init; }

    public string? DefaultModel { get; init; }

    public int? ContextLimit { get; init; }
}

public sealed record GameTemplateResponse
{
    public required GameTemplate Template { get; init; }

    public required GameTemplateValidationResult Validation { get; init; }
}

public sealed record SaveGameTemplateRequest
{
    public required GameTemplate Template { get; init; }
}

public sealed record CloneGameTemplateRequest
{
    public string TargetTemplateId { get; init; } = string.Empty;

    public string? DisplayName { get; init; }
}

public sealed record ValidateGameTemplateRequest
{
    public required GameTemplate Template { get; init; }
}

public sealed record ValidateGameTemplateResponse
{
    public required GameTemplateValidationResult Validation { get; init; }
}

public sealed record DeleteGameTemplateResponse
{
    public string Status { get; init; } = "ok";

    public required string TemplateId { get; init; }
}

public sealed record GamePromptTemplateListResponse
{
    public required string ModuleId { get; init; }

    public required IReadOnlyList<GamePromptTemplateOption> Prompts { get; init; }
}

public sealed record GamePromptTemplateOption
{
    public required string Value { get; init; }

    public required string DisplayName { get; init; }

    public required GamePromptTemplateSource Source { get; init; }

    public string? UserPromptName { get; init; }

    public required bool IsDefault { get; init; }

    public required int Tokens { get; init; }

    public long? Size { get; init; }

    public string? RelativePath { get; init; }
}

public sealed record OpenGamePromptTemplateRequest
{
    public GamePromptTemplateSelection Selection { get; init; } = GamePromptTemplateSelection.Default;
}

public sealed record GamePromptTemplateDocumentResponse
{
    public required string ModuleId { get; init; }

    public required string Name { get; init; }

    public required string DisplayName { get; init; }

    public required string RelativePath { get; init; }

    public required GamePromptTemplateSelection Selection { get; init; }

    public required string Content { get; init; }

    public required int Tokens { get; init; }

    public required bool CreatedCopy { get; init; }
}

public sealed record WriteGamePromptTemplateRequest
{
    public string Content { get; init; } = string.Empty;
}

public sealed record WriteGamePromptTemplateResponse
{
    public string Status { get; init; } = "ok";

    public required string ModuleId { get; init; }

    public required string Name { get; init; }
}
