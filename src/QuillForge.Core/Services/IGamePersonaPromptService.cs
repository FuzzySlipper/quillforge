using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface IGamePersonaPromptService
{
    Task<IReadOnlyList<GameUserPersonaPromptInfo>> ListUserPromptsAsync(CancellationToken ct = default);

    Task<GameUserPersonaPromptDocument> CreateForEditAsync(string? basePromptName, string? seedContent, CancellationToken ct = default);

    Task<GameUserPersonaPromptDocument?> TryOpenUserPromptAsync(string promptName, CancellationToken ct = default);

    Task SaveUserPromptAsync(string promptName, string content, CancellationToken ct = default);

    Task<GameResolvedPersonaPrompt> ResolveAsync(GamePersonaPromptSelection? selection, CancellationToken ct = default);
}
