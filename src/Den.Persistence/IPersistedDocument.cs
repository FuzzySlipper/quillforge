namespace Den.Persistence;

/// <summary>
/// Defines the schema and lifecycle for a typed file-backed persisted document.
/// Implementations specify the file path, default state, normalization, and validation
/// for a single persisted document type.
/// </summary>
/// <typeparam name="T">The document model type.</typeparam>
public interface IPersistedDocument<T>
{
    /// <summary>
    /// Relative path (from the content root) where this document is stored on disk.
    /// </summary>
    string RelativePath { get; }

    /// <summary>
    /// Creates the default state for this document when the file does not exist on disk.
    /// </summary>
    T CreateDefault();

    /// <summary>
    /// Normalizes a loaded or updated value. Called after deserialization on load
    /// and before validation on save/update. Use this to apply defaults for missing
    /// fields, clamp ranges, or fix up legacy shapes.
    /// </summary>
    T Normalize(T value);

    /// <summary>
    /// Validates the document state. Throws if the value is invalid.
    /// Called before every save/persist operation.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when the value is invalid.</exception>
    void ThrowIfInvalid(T value);
}
