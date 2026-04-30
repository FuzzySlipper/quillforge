using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class DefaultOnlyGamePersonaPromptService : IGamePersonaPromptService
{
    public Task<IReadOnlyList<GameUserPersonaPromptInfo>> ListUserPromptsAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<GameUserPersonaPromptInfo>>([]);

    public Task<GameUserPersonaPromptDocument> CreateForEditAsync(string? basePromptName, string? seedContent, CancellationToken ct = default) =>
        throw new NotSupportedException("User-owned game persona prompts are not configured.");

    public Task<GameUserPersonaPromptDocument?> TryOpenUserPromptAsync(string promptName, CancellationToken ct = default) =>
        Task.FromResult<GameUserPersonaPromptDocument?>(null);

    public Task SaveUserPromptAsync(string promptName, string content, CancellationToken ct = default) =>
        throw new NotSupportedException("User-owned game persona prompts are not configured.");

    public Task<GameResolvedPersonaPrompt> ResolveAsync(GamePersonaPromptSelection? selection, CancellationToken ct = default) =>
        Task.FromResult(new GameResolvedPersonaPrompt
        {
            Content = null,
            Selection = GamePersonaPromptSelection.None,
            UsedFallback = selection is { Source: GamePersonaPromptSource.User },
            FallbackReason = selection is { Source: GamePersonaPromptSource.User } ? "persona_prompt_store_unavailable" : null,
        });
}
