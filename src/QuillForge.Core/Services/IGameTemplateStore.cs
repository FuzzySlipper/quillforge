using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// Persistence for durable reusable game templates. Templates are keyed
/// documents under the QuillForge content root and are mutated only through the
/// game-template service boundary.
/// </summary>
public interface IGameTemplateStore
{
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);

    Task<bool> ExistsAsync(string templateId, CancellationToken ct = default);

    Task<GameTemplate> LoadAsync(string templateId, CancellationToken ct = default);

    Task SaveAsync(string templateId, GameTemplate template, CancellationToken ct = default);

    Task DeleteAsync(string templateId, CancellationToken ct = default);
}
