using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface IGamePersonaPromptStore
{
    Task<IReadOnlyList<GameUserPersonaPromptInfo>> ListAsync(CancellationToken ct = default);

    Task<GameUserPersonaPromptDocument?> TryLoadAsync(string promptName, CancellationToken ct = default);

    Task SaveAsync(string promptName, string content, CancellationToken ct = default);

    Task<GameUserPersonaPromptDocument> CreateUniqueAsync(
        string basePromptName,
        string content,
        CancellationToken ct = default);
}
