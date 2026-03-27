namespace QuillForge.Core.Services;

/// <summary>
/// Access to persona/character definitions.
/// </summary>
public interface IPersonaStore
{
    Task<string> LoadAsync(string personaName, int? maxTokens = null, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);
}
