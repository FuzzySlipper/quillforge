using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// Persistence for durable reusable profile configuration files.
/// Profiles are keyed documents rather than a single global config file.
/// </summary>
public interface IProfileConfigStore
{
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);

    Task<bool> ExistsAsync(string profileId, CancellationToken ct = default);

    Task<ProfileConfig> LoadAsync(string profileId, CancellationToken ct = default);

    Task SaveAsync(string profileId, ProfileConfig config, CancellationToken ct = default);

    Task DeleteAsync(string profileId, CancellationToken ct = default);
}
