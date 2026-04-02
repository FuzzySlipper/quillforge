namespace Den.Persistence;

/// <summary>
/// Provides load, save, and atomic update operations for a typed persisted document.
/// All mutations are persisted to disk by default — callers never need to remember
/// to call save after modifying state.
/// </summary>
/// <typeparam name="T">The document model type.</typeparam>
public interface IPersistedDocumentStore<T>
{
    /// <summary>
    /// Loads the document from disk. If the file does not exist, creates and returns
    /// the default state. The returned value has been deserialized, normalized, and validated.
    /// </summary>
    Task<T> LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Validates and persists the given value to disk atomically.
    /// </summary>
    Task SaveAsync(T value, CancellationToken ct = default);

    /// <summary>
    /// Atomically loads the current state, applies the update function, validates,
    /// and persists the result. This is the preferred mutation path — it prevents
    /// lost updates from concurrent callers.
    /// </summary>
    /// <param name="update">
    /// A function that receives the current state and returns the new state.
    /// Prefer <c>with</c>-style record updates for immutable models.
    /// </param>
    /// <returns>The updated and persisted value.</returns>
    Task<T> UpdateAsync(Func<T, T> update, CancellationToken ct = default);
}
