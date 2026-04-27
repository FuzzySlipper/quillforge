using QuillForge.Core.Models;

namespace QuillForge.Web.Contracts;

public sealed record GameTemplateListResponse
{
    public required IReadOnlyList<GameTemplateSummary> Templates { get; init; }
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
