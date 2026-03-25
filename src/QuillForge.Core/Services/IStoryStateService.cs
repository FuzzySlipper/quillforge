namespace QuillForge.Core.Services;

/// <summary>
/// Manages .state.yaml companion files that track plot threads, character conditions,
/// tension levels, and event counters across sessions.
/// </summary>
public interface IStoryStateService
{
    Task<IReadOnlyDictionary<string, object>> LoadAsync(string stateFilePath, CancellationToken ct = default);
    Task SaveAsync(string stateFilePath, IReadOnlyDictionary<string, object> state, CancellationToken ct = default);
    Task<IReadOnlyDictionary<string, object>> MergeAsync(
        string stateFilePath,
        IReadOnlyDictionary<string, object> updates,
        CancellationToken ct = default);
    Task IncrementCounterAsync(string stateFilePath, string counterKey, CancellationToken ct = default);
    Task RemoveKeyAsync(string stateFilePath, string key, CancellationToken ct = default);
}
