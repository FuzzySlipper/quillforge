namespace QuillForge.Core.Services;

/// <summary>
/// Loads named assistant style/personality prompt layers from the file system.
/// These are always subordinate to the app-owned Assistant base prompt.
/// </summary>
public interface IAssistantPromptStore
{
    Task<string> LoadAsync(string promptName, CancellationToken ct = default);
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);
}
