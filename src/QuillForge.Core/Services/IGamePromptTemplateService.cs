using Den.RulesEngine;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface IGamePromptTemplateService
{
    Task<IReadOnlyList<GameUserPromptTemplateInfo>> ListUserPromptsAsync(string moduleId, CancellationToken ct = default);

    Task<GameUserPromptTemplateDocument> CopyDefaultForEditAsync(string moduleId, CancellationToken ct = default);

    Task<GameUserPromptTemplateDocument?> TryOpenUserPromptAsync(string moduleId, string promptName, CancellationToken ct = default);

    Task SaveUserPromptAsync(string moduleId, string promptName, string content, CancellationToken ct = default);

    Task<GameResolvedPromptTemplate> ResolveAsync(
        IGameModule module,
        GamePromptTemplateSelection? selection,
        CancellationToken ct = default);
}
