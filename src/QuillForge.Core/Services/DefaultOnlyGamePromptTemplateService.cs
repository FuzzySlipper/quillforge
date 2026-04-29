using Den.RulesEngine;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class DefaultOnlyGamePromptTemplateService : IGamePromptTemplateService
{
    public Task<IReadOnlyList<GameUserPromptTemplateInfo>> ListUserPromptsAsync(string moduleId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<GameUserPromptTemplateInfo>>([]);

    public Task<GameUserPromptTemplateDocument> CopyDefaultForEditAsync(string moduleId, CancellationToken ct = default) =>
        throw new NotSupportedException("User-owned game prompt templates are not configured.");

    public Task<GameUserPromptTemplateDocument?> TryOpenUserPromptAsync(string moduleId, string promptName, CancellationToken ct = default) =>
        Task.FromResult<GameUserPromptTemplateDocument?>(null);

    public Task SaveUserPromptAsync(string moduleId, string promptName, string content, CancellationToken ct = default) =>
        throw new NotSupportedException("User-owned game prompt templates are not configured.");

    public Task<GameResolvedPromptTemplate> ResolveAsync(
        IGameModule module,
        GamePromptTemplateSelection? selection,
        CancellationToken ct = default) =>
        Task.FromResult(new GameResolvedPromptTemplate
        {
            Content = GamePromptTemplateService.BuildDefaultPrompt(module),
            Selection = GamePromptTemplateSelection.Default,
            UsedFallback = selection is { Source: GamePromptTemplateSource.User },
            FallbackReason = selection is { Source: GamePromptTemplateSource.User } ? "user_prompt_store_unavailable" : null,
        });
}
