using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface IGamePromptTemplateStore
{
    Task<IReadOnlyList<GameUserPromptTemplateInfo>> ListAsync(string moduleId, CancellationToken ct = default);

    Task<GameUserPromptTemplateDocument?> TryLoadAsync(string moduleId, string promptName, CancellationToken ct = default);

    Task SaveAsync(string moduleId, string promptName, string content, CancellationToken ct = default);

    Task<GameUserPromptTemplateDocument> CreateUniqueAsync(
        string moduleId,
        string basePromptName,
        string content,
        CancellationToken ct = default);
}
