namespace QuillForge.Core.Services;

/// <summary>
/// Access to conductor prompt profiles, with legacy persona-path compatibility.
/// </summary>
public interface IPersonaStore
{
    Task<string> LoadAsync(string personaName, int? maxTokens = null, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);
}
