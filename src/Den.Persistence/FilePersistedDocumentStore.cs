using Microsoft.Extensions.Logging;

namespace Den.Persistence;

/// <summary>
/// Base class for file-backed persisted document stores. Centralizes the full
/// load/default/normalize/validate/save lifecycle with thread-safe locking
/// and atomic writes. Subclasses provide serialization/deserialization only.
/// </summary>
/// <typeparam name="T">The document model type.</typeparam>
public abstract class FilePersistedDocumentStore<T> : IPersistedDocumentStore<T>
{
    private readonly IPersistedDocument<T> _document;
    private readonly AtomicFileWriter _writer;
    private readonly ILogger _logger;
    private readonly string _fullPath;
    private readonly SemaphoreSlim _lock = new(1, 1);

    protected FilePersistedDocumentStore(
        IPersistedDocument<T> document,
        string contentRoot,
        AtomicFileWriter writer,
        ILogger logger)
    {
        _document = document;
        _writer = writer;
        _logger = logger;
        _fullPath = Path.Combine(contentRoot, document.RelativePath);
    }

    /// <summary>
    /// The resolved absolute path to the persisted document file.
    /// </summary>
    protected string FullPath => _fullPath;

    /// <inheritdoc />
    public async Task<T> LoadAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            return await LoadInternalAsync(ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(T value, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await SaveInternalAsync(value, ct);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<T> UpdateAsync(Func<T, T> update, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            var current = await LoadInternalAsync(ct);
            var updated = update(current);
            await SaveInternalAsync(updated, ct);
            return updated;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Deserializes the raw file content into the document model.
    /// Return <c>null</c> if the content cannot be deserialized.
    /// </summary>
    protected abstract T? Deserialize(string content);

    /// <summary>
    /// Serializes the document model to a string for writing to disk.
    /// </summary>
    protected abstract string Serialize(T value);

    private async Task<T> LoadInternalAsync(CancellationToken ct)
    {
        T value;

        if (File.Exists(_fullPath))
        {
            try
            {
                var content = await File.ReadAllTextAsync(_fullPath, ct);
                var deserialized = Deserialize(content);
                value = deserialized ?? _document.CreateDefault();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load persisted document from {Path}, using defaults", _fullPath);
                value = _document.CreateDefault();
            }
        }
        else
        {
            _logger.LogDebug("Persisted document not found at {Path}, using defaults", _fullPath);
            value = _document.CreateDefault();
        }

        value = _document.Normalize(value);
        _document.ThrowIfInvalid(value);
        return value;
    }

    private async Task SaveInternalAsync(T value, CancellationToken ct)
    {
        value = _document.Normalize(value);
        _document.ThrowIfInvalid(value);

        var content = Serialize(value);
        await _writer.WriteAsync(_fullPath, content, ct);
        _logger.LogDebug("Persisted document saved to {Path}", _fullPath);
    }
}
