using Den.Persistence;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Storage.Configuration;

/// <summary>
/// Persists AppConfig to config.yaml using the Den.Persistence boundary.
/// Thin wrapper over <see cref="YamlPersistedDocumentStore{T}"/> that implements
/// the domain-shaped <see cref="IAppConfigStore"/> interface.
/// </summary>
public sealed class AppConfigStore : IAppConfigStore
{
    private readonly IPersistedDocumentStore<AppConfig> _inner;

    public AppConfigStore(string contentRoot, AtomicFileWriter writer, ILogger<AppConfigStore> logger)
    {
        _inner = new YamlPersistedDocumentStore<AppConfig>(
            new AppConfigDocument(),
            contentRoot,
            writer,
            logger);
    }

    /// <inheritdoc />
    public Task<AppConfig> LoadAsync(CancellationToken ct = default)
        => _inner.LoadAsync(ct);

    /// <inheritdoc />
    public Task SaveAsync(AppConfig config, CancellationToken ct = default)
        => _inner.SaveAsync(config, ct);

    /// <inheritdoc />
    public Task<AppConfig> UpdateAsync(Func<AppConfig, AppConfig> update, CancellationToken ct = default)
        => _inner.UpdateAsync(update, ct);
}
