using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class ProfileConfigService : IProfileConfigService
{
    private const string CompatibilityDefaultProfileId = "default";

    private readonly IProfileConfigStore _store;
    private readonly IAppConfigStore _appConfigStore;
    private readonly ILogger<ProfileConfigService> _logger;

    public ProfileConfigService(
        IProfileConfigStore store,
        IAppConfigStore appConfigStore,
        ILogger<ProfileConfigService> logger)
    {
        _store = store;
        _appConfigStore = appConfigStore;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        await EnsureCompatibilityDefaultProfileAsync(ct);

        var profiles = await _store.ListAsync(ct);
        _logger.LogInformation("Listed {Count} durable profiles", profiles.Count);
        return profiles;
    }

    public async Task<string> GetDefaultProfileIdAsync(CancellationToken ct = default)
    {
        var appConfig = await _appConfigStore.LoadAsync(ct);
        var defaultProfileId = NormalizeProfileId(appConfig.Profiles.Default);
        _logger.LogInformation("Resolved default profile id {ProfileId}", defaultProfileId);
        return defaultProfileId;
    }

    public async Task<ResolvedProfileConfig> LoadResolvedAsync(string? profileId = null, CancellationToken ct = default)
    {
        await EnsureCompatibilityDefaultProfileAsync(ct);

        var explicitProfileRequested = !string.IsNullOrWhiteSpace(profileId);
        var resolvedProfileId = await ResolveProfileIdAsync(profileId, ct);
        var exists = await _store.ExistsAsync(resolvedProfileId, ct);
        if (!exists && explicitProfileRequested)
        {
            throw new FileNotFoundException($"Profile config {resolvedProfileId} not found");
        }

        if (!exists && !string.Equals(resolvedProfileId, CompatibilityDefaultProfileId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Requested profile {ProfileId} does not exist; falling back to compatibility default profile",
                resolvedProfileId);
            resolvedProfileId = CompatibilityDefaultProfileId;
        }

        var config = await _store.LoadAsync(resolvedProfileId, ct);

        _logger.LogInformation(
            "Loaded resolved profile config {ProfileId}: conductor={Conductor} lore={LoreSet} narrativeRules={NarrativeRules} writingStyle={WritingStyle}",
            resolvedProfileId,
            config.Conductor,
            config.LoreSet,
            config.NarrativeRules,
            config.WritingStyle);

        return new ResolvedProfileConfig
        {
            ProfileId = resolvedProfileId,
            Config = config,
            Persisted = true,
        };
    }

    public async Task<ResolvedProfileConfig> SaveAsync(string profileId, ProfileConfig config, CancellationToken ct = default)
    {
        var resolvedProfileId = NormalizeProfileId(profileId);
        var normalizedConfig = NormalizeProfile(config);

        await _store.SaveAsync(resolvedProfileId, normalizedConfig, ct);

        _logger.LogInformation(
            "Saved durable profile config {ProfileId}: conductor={Conductor} lore={LoreSet} narrativeRules={NarrativeRules} writingStyle={WritingStyle}",
            resolvedProfileId,
            normalizedConfig.Conductor,
            normalizedConfig.LoreSet,
            normalizedConfig.NarrativeRules,
            normalizedConfig.WritingStyle);

        return new ResolvedProfileConfig
        {
            ProfileId = resolvedProfileId,
            Config = normalizedConfig,
            Persisted = true,
        };
    }

    public async Task<ProfileSelectionResult> SelectAsync(string profileId, CancellationToken ct = default)
    {
        await EnsureCompatibilityDefaultProfileAsync(ct);

        var resolvedProfileId = NormalizeProfileId(profileId);
        var config = NormalizeProfile(await _store.LoadAsync(resolvedProfileId, ct));
        var updatedAppConfig = await _appConfigStore.UpdateAsync(current =>
            ApplySelectedProfileToAppConfig(current, resolvedProfileId, config), ct);

        _logger.LogInformation(
            "Selected default profile {ProfileId}: conductor={Conductor} lore={LoreSet} narrativeRules={NarrativeRules} writingStyle={WritingStyle}",
            resolvedProfileId,
            config.Conductor,
            config.LoreSet,
            config.NarrativeRules,
            config.WritingStyle);

        return new ProfileSelectionResult
        {
            ProfileId = resolvedProfileId,
            Config = config,
            UpdatedAppConfig = updatedAppConfig,
        };
    }

    public async Task<ProfileSelectionResult> SaveAndSelectAsync(string profileId, ProfileConfig config, CancellationToken ct = default)
    {
        var saved = await SaveAsync(profileId, config, ct);
        return await SelectAsync(saved.ProfileId, ct);
    }

    public async Task<ProfileState> BuildSessionProfileStateAsync(string? profileId = null, CancellationToken ct = default)
    {
        var resolved = await LoadResolvedAsync(profileId, ct);

        return new ProfileState
        {
            ProfileId = resolved.ProfileId,
            ActivePersona = resolved.Config.Conductor,
            ActiveLoreSet = resolved.Config.LoreSet,
            ActiveNarrativeRules = resolved.Config.NarrativeRules,
            ActiveWritingStyle = resolved.Config.WritingStyle,
        };
    }

    private async Task EnsureCompatibilityDefaultProfileAsync(CancellationToken ct)
    {
        if (await _store.ExistsAsync(CompatibilityDefaultProfileId, ct))
        {
            return;
        }

        var appConfig = await _appConfigStore.LoadAsync(ct);
        var compatibilityProfile = CreateCompatibilityProfile(appConfig);
        await _store.SaveAsync(CompatibilityDefaultProfileId, compatibilityProfile, ct);

        _logger.LogInformation(
            "Created compatibility default profile from AppConfig: conductor={Conductor} lore={LoreSet} narrativeRules={NarrativeRules} writingStyle={WritingStyle}",
            compatibilityProfile.Conductor,
            compatibilityProfile.LoreSet,
            compatibilityProfile.NarrativeRules,
            compatibilityProfile.WritingStyle);
    }

    private async Task<string> ResolveProfileIdAsync(string? profileId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(profileId))
        {
            return NormalizeProfileId(profileId);
        }

        return await GetDefaultProfileIdAsync(ct);
    }

    private static string NormalizeProfileId(string? profileId)
    {
        var normalized = profileId?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return CompatibilityDefaultProfileId;
        }

        return normalized;
    }

    private static ProfileConfig NormalizeProfile(ProfileConfig config)
    {
        return config with
        {
            Conductor = NormalizeChoice(config.Conductor),
            LoreSet = NormalizeChoice(config.LoreSet),
            NarrativeRules = NormalizeChoice(config.NarrativeRules),
            WritingStyle = NormalizeChoice(config.WritingStyle),
        };
    }

    private static string NormalizeChoice(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? CompatibilityDefaultProfileId : value.Trim();
    }

    private static ProfileConfig CreateCompatibilityProfile(AppConfig appConfig)
    {
        return NormalizeProfile(new ProfileConfig
        {
            Conductor = appConfig.Persona.Active,
            LoreSet = appConfig.Lore.Active,
            NarrativeRules = appConfig.NarrativeRules.Active,
            WritingStyle = appConfig.WritingStyle.Active,
        });
    }

    private static AppConfig ApplySelectedProfileToAppConfig(
        AppConfig current,
        string profileId,
        ProfileConfig config)
    {
        return current with
        {
            Profiles = current.Profiles with
            {
                Default = profileId,
            },
            Persona = current.Persona with
            {
                Active = config.Conductor,
            },
            Lore = current.Lore with
            {
                Active = config.LoreSet,
            },
            NarrativeRules = current.NarrativeRules with
            {
                Active = config.NarrativeRules,
            },
            WritingStyle = current.WritingStyle with
            {
                Active = config.WritingStyle,
            },
        };
    }
}
