using Den.Persistence;
using Microsoft.Extensions.Logging;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Persists lightweight runtime state (last mode, last session, etc.)
/// to a small JSON file. Delegates to <see cref="JsonPersistedDocumentStore{T}"/>
/// for the full load/save lifecycle.
/// </summary>
public sealed class RuntimeStateStore
{
    private readonly IPersistedDocumentStore<RuntimeState> _inner;

    public RuntimeStateStore(string contentRoot, AtomicFileWriter writer, ILogger<RuntimeStateStore> logger)
    {
        _inner = new JsonPersistedDocumentStore<RuntimeState>(
            new RuntimeStateDocument(),
            contentRoot,
            writer,
            logger);
    }

    public RuntimeState Load()
        => _inner.LoadAsync().GetAwaiter().GetResult();

    public Task SaveAsync(RuntimeState state, CancellationToken ct = default)
        => _inner.SaveAsync(state, ct);

    public Task<RuntimeState> UpdateAsync(Func<RuntimeState, RuntimeState> update, CancellationToken ct = default)
        => _inner.UpdateAsync(update, ct);
}
