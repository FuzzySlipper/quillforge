namespace QuillForge.Core.Services;

/// <summary>
/// Access to narrative-rules templates that guide scene direction.
/// </summary>
public interface INarrativeRulesStore
{
    Task<string> LoadAsync(string rulesName, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);
}
