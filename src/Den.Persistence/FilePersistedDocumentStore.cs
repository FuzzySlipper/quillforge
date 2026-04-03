using Microsoft.Extensions.Logging;
using System.Text.Json.Nodes;

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

    /// <summary>
    /// Parses serialized content into a mutable root object so version metadata
    /// and migration transforms can be applied before typed deserialization.
    /// </summary>
    protected abstract JsonObject ParseRootObject(string content);

    /// <summary>
    /// Serializes a migrated root object back to the store format.
    /// </summary>
    protected abstract string SerializeRootObject(JsonObject root);

    private async Task<T> LoadInternalAsync(CancellationToken ct)
    {
        T value;

        if (File.Exists(_fullPath))
        {
            try
            {
                var content = await File.ReadAllTextAsync(_fullPath, ct);
                var migration = MigrateContentIfNeeded(content);
                content = migration.Content;
                if (migration.DidMigrate)
                {
                    await _writer.WriteAsync(_fullPath, content, ct);
                    _logger.LogInformation("Persisted migrated document to {Path}", _fullPath);
                }
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
        content = StampVersionIfNeeded(content);
        await _writer.WriteAsync(_fullPath, content, ct);
        _logger.LogDebug("Persisted document saved to {Path}", _fullPath);
    }

    private (string Content, bool DidMigrate) MigrateContentIfNeeded(string content)
    {
        if (_document is not IVersionedPersistedDocument<T> versioned)
        {
            return (content, false);
        }

        var root = ParseRootObject(content);
        var version = ReadVersion(root, versioned);
        var didMigrate = false;

        if (version > versioned.CurrentVersion)
        {
            throw new InvalidOperationException(
                $"Persisted document at '{_fullPath}' has schema version {version}, " +
                $"but this store only supports up to version {versioned.CurrentVersion}.");
        }

        while (version < versioned.CurrentVersion)
        {
            versioned.MigrateOneVersion(root, version);
            version++;
            didMigrate = true;
            root[versioned.VersionFieldName] = version;
            _logger.LogInformation(
                "Migrated persisted document {Path} to schema version {Version}",
                _fullPath,
                version);
        }

        return (SerializeRootObject(root), didMigrate);
    }

    private string StampVersionIfNeeded(string content)
    {
        if (_document is not IVersionedPersistedDocument<T> versioned)
        {
            return content;
        }

        var root = ParseRootObject(content);
        root[versioned.VersionFieldName] = versioned.CurrentVersion;
        return SerializeRootObject(root);
    }

    private static int ReadVersion(JsonObject root, IVersionedPersistedDocument<T> versioned)
    {
        if (root[versioned.VersionFieldName] is JsonValue value)
        {
            if (value.TryGetValue<int>(out var intVersion))
            {
                return intVersion;
            }

            if (value.TryGetValue<string>(out var stringVersion)
                && int.TryParse(stringVersion, out intVersion))
            {
                return intVersion;
            }

            throw new InvalidOperationException(
                $"Persisted document version field '{versioned.VersionFieldName}' must be an integer.");
        }

        return versioned.InitialVersion;
    }
}
