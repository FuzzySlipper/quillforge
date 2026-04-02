using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// Provides load, save, and atomic update operations for the application configuration.
/// All mutations are persisted to disk — callers never need to remember to call save.
/// </summary>
public interface IAppConfigStore
{
    /// <summary>
    /// Loads the current configuration from disk. Returns defaults if the file
    /// does not exist or cannot be parsed.
    /// </summary>
    Task<AppConfig> LoadAsync(CancellationToken ct = default);

    /// <summary>
    /// Persists the given configuration to disk atomically.
    /// </summary>
    Task SaveAsync(AppConfig config, CancellationToken ct = default);

    /// <summary>
    /// Atomically loads, applies the update function, and persists the result.
    /// This is the preferred mutation path for config changes.
    /// </summary>
    Task<AppConfig> UpdateAsync(Func<AppConfig, AppConfig> update, CancellationToken ct = default);
}
