using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface IProfileConfigService
{
    Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default);

    Task<string> GetDefaultProfileIdAsync(CancellationToken ct = default);

    Task<ResolvedProfileConfig> LoadResolvedAsync(string? profileId = null, CancellationToken ct = default);

    Task<ResolvedProfileConfig> SaveAsync(string profileId, ProfileConfig config, CancellationToken ct = default);

    Task<ProfileSelectionResult> SelectAsync(string profileId, CancellationToken ct = default);

    Task<ProfileSelectionResult> SaveAndSelectAsync(string profileId, ProfileConfig config, CancellationToken ct = default);

    Task<ProfileState> BuildSessionProfileStateAsync(string? profileId = null, CancellationToken ct = default);
}

public sealed record ResolvedProfileConfig
{
    public required string ProfileId { get; init; }
    public required ProfileConfig Config { get; init; }
    public required bool Persisted { get; init; }
}

public sealed record ProfileSelectionResult
{
    public required string ProfileId { get; init; }
    public required ProfileConfig Config { get; init; }
    public required AppConfig UpdatedAppConfig { get; init; }
}
