using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface IGameTemplateService
{
    Task<IReadOnlyList<GameTemplateSummary>> ListAsync(CancellationToken ct = default);

    Task<GameTemplateValidationEnvelope> LoadAsync(string templateId, CancellationToken ct = default);

    Task<GameTemplateValidationEnvelope> SaveAsync(string templateId, GameTemplate template, CancellationToken ct = default);

    Task<GameTemplateValidationEnvelope> CloneAsync(string sourceTemplateId, string targetTemplateId, string? displayName, CancellationToken ct = default);

    Task DeleteAsync(string templateId, CancellationToken ct = default);

    Task<GameTemplateValidationResult> ValidateAsync(GameTemplate template, CancellationToken ct = default);
}
