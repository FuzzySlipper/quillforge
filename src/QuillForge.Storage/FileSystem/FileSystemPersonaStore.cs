using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Services;

namespace QuillForge.Storage.FileSystem;

/// <summary>
/// Legacy compatibility wrapper for the renamed conductor store.
/// </summary>
public sealed class FileSystemPersonaStore : IPersonaStore
{
    private readonly FileSystemConductorStore _inner;

    public FileSystemPersonaStore(
        string conductorPath,
        string? legacyPersonaPath,
        ILogger<FileSystemPersonaStore> logger)
    {
        _inner = new FileSystemConductorStore(
            conductorPath,
            legacyPersonaPath,
            NullLogger<FileSystemConductorStore>.Instance);
    }

    public async Task<string> LoadAsync(string personaName, int? maxTokens = null, CancellationToken ct = default)
    {
        return await _inner.LoadAsync(personaName, maxTokens, ct);
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        return _inner.ListAsync(ct);
    }
}
