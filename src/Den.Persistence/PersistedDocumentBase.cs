namespace Den.Persistence;

/// <summary>
/// Base class for persisted document definitions that provides sensible defaults.
/// Subclasses must implement <see cref="RelativePath"/> and <see cref="CreateDefault"/>.
/// Override <see cref="Normalize"/> and <see cref="ThrowIfInvalid"/> only when needed.
/// </summary>
/// <typeparam name="T">The document model type.</typeparam>
public abstract class PersistedDocumentBase<T> : IPersistedDocument<T>
{
    /// <inheritdoc />
    public abstract string RelativePath { get; }

    /// <inheritdoc />
    public abstract T CreateDefault();

    /// <inheritdoc />
    /// <remarks>Default implementation returns the value unchanged.</remarks>
    public virtual T Normalize(T value) => value;

    /// <inheritdoc />
    /// <remarks>Default implementation accepts all values.</remarks>
    public virtual void ThrowIfInvalid(T value) { }
}
