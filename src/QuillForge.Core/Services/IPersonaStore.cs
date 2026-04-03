namespace QuillForge.Core.Services;

/// <summary>
/// Access to conductor prompt profiles, with legacy persona-path compatibility.
/// </summary>
public interface IConductorStore
{
    Task<string> LoadAsync(string conductorName, int? maxTokens = null, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);
}

/// <summary>
/// Legacy alias for conductor prompt storage.
/// </summary>
public interface IPersonaStore : IConductorStore
{
}
