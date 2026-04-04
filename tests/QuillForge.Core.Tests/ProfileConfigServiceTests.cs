using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public sealed class ProfileConfigServiceTests
{
    [Fact]
    public async Task ListAsync_MaterializesCompatibilityDefaultProfile_WhenNoProfilesExist()
    {
        var appConfigStore = new InMemoryAppConfigStore(new AppConfig
        {
            Persona = new PersonaConfig { Active = "narrator" },
            Lore = new LoreConfig { Active = "fantasy" },
            NarrativeRules = new NarrativeRulesConfig { Active = "strict" },
            WritingStyle = new WritingStyleConfig { Active = "literary" },
        });
        var profileStore = new InMemoryProfileConfigStore();
        var runtimeStore = new InMemoryProfileUsageRuntimeStore();
        var service = new ProfileConfigService(profileStore, appConfigStore, runtimeStore, NullLogger<ProfileConfigService>.Instance);

        var profiles = await service.ListAsync();
        var resolved = await service.LoadResolvedAsync();

        Assert.Equal(["default"], profiles);
        Assert.Equal("default", resolved.ProfileId);
        Assert.Equal("narrator", resolved.Config.Conductor);
        Assert.Equal("fantasy", resolved.Config.LoreSet);
        Assert.Equal("strict", resolved.Config.NarrativeRules);
        Assert.Equal("literary", resolved.Config.WritingStyle);
    }

    [Fact]
    public async Task SelectAsync_UpdatesAppConfigDefaultAndCompatibilityLegacyActiveFields()
    {
        var appConfigStore = new InMemoryAppConfigStore(new AppConfig());
        var profileStore = new InMemoryProfileConfigStore();
        var runtimeStore = new InMemoryProfileUsageRuntimeStore();
        await profileStore.SaveAsync("research", new ProfileConfig
        {
            Conductor = "analyst",
            LoreSet = "science",
            NarrativeRules = "clean",
            WritingStyle = "concise",
        });

        var service = new ProfileConfigService(profileStore, appConfigStore, runtimeStore, NullLogger<ProfileConfigService>.Instance);
        var selection = await service.SelectAsync("research");

        Assert.Equal("research", selection.ProfileId);
        Assert.Equal("research", selection.UpdatedAppConfig.Profiles.Default);
        Assert.Equal("analyst", selection.UpdatedAppConfig.Persona.Active);
        Assert.Equal("science", selection.UpdatedAppConfig.Lore.Active);
        Assert.Equal("clean", selection.UpdatedAppConfig.NarrativeRules.Active);
        Assert.Equal("concise", selection.UpdatedAppConfig.WritingStyle.Active);
    }

    [Fact]
    public async Task BuildSessionProfileStateAsync_StoresOnlyResolvedProfileId()
    {
        var appConfigStore = new InMemoryAppConfigStore(new AppConfig());
        var profileStore = new InMemoryProfileConfigStore();
        var runtimeStore = new InMemoryProfileUsageRuntimeStore();
        await profileStore.SaveAsync("builder", new ProfileConfig
        {
            Conductor = "worldsmith",
            LoreSet = "builder",
            NarrativeRules = "cinematic",
            WritingStyle = "lush",
        });

        var service = new ProfileConfigService(profileStore, appConfigStore, runtimeStore, NullLogger<ProfileConfigService>.Instance);
        var state = await service.BuildSessionProfileStateAsync("builder");

        Assert.Equal("builder", state.ProfileId);
        Assert.Null(state.ActiveConductor);
        Assert.Null(state.ActiveLoreSet);
        Assert.Null(state.ActiveNarrativeRules);
        Assert.Null(state.ActiveWritingStyle);
    }

    [Fact]
    public async Task CloneAsync_CopiesProfileToNewId()
    {
        var appConfigStore = new InMemoryAppConfigStore(new AppConfig());
        var profileStore = new InMemoryProfileConfigStore();
        var runtimeStore = new InMemoryProfileUsageRuntimeStore();
        await profileStore.SaveAsync("research", new ProfileConfig
        {
            Conductor = "analyst",
            LoreSet = "science",
            NarrativeRules = "clean",
            WritingStyle = "concise",
        });

        var service = new ProfileConfigService(profileStore, appConfigStore, runtimeStore, NullLogger<ProfileConfigService>.Instance);
        var cloned = await service.CloneAsync("research", "research-copy");

        Assert.Equal("research-copy", cloned.ProfileId);
        Assert.Equal("analyst", cloned.Config.Conductor);
        var loaded = await profileStore.LoadAsync("research-copy");
        Assert.Equal("science", loaded.LoreSet);
    }

    [Fact]
    public async Task DeleteAsync_RejectsDefaultProfile()
    {
        var appConfigStore = new InMemoryAppConfigStore(new AppConfig
        {
            Profiles = new ProfilesConfig { Default = "default" },
        });
        var profileStore = new InMemoryProfileConfigStore();
        var runtimeStore = new InMemoryProfileUsageRuntimeStore();
        await profileStore.SaveAsync("default", new ProfileConfig());
        var service = new ProfileConfigService(profileStore, appConfigStore, runtimeStore, NullLogger<ProfileConfigService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync("default"));

        Assert.Contains("Cannot delete default profile", ex.Message);
    }

    [Fact]
    public async Task DeleteAsync_RejectsInUseProfile()
    {
        var appConfigStore = new InMemoryAppConfigStore(new AppConfig
        {
            Profiles = new ProfilesConfig { Default = "default" },
        });
        var profileStore = new InMemoryProfileConfigStore();
        var runtimeStore = new InMemoryProfileUsageRuntimeStore();
        await profileStore.SaveAsync("default", new ProfileConfig());
        await profileStore.SaveAsync("grim", new ProfileConfig());
        runtimeStore.SetProfileUsage("grim", [Guid.CreateVersion7()]);
        var service = new ProfileConfigService(profileStore, appConfigStore, runtimeStore, NullLogger<ProfileConfigService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync("grim"));

        Assert.Contains("referenced by 1 persisted session", ex.Message);
    }

    [Fact]
    public async Task DeleteAsync_RemovesUnusedNonDefaultProfile()
    {
        var appConfigStore = new InMemoryAppConfigStore(new AppConfig
        {
            Profiles = new ProfilesConfig { Default = "default" },
        });
        var profileStore = new InMemoryProfileConfigStore();
        var runtimeStore = new InMemoryProfileUsageRuntimeStore();
        await profileStore.SaveAsync("default", new ProfileConfig());
        await profileStore.SaveAsync("grim", new ProfileConfig());
        var service = new ProfileConfigService(profileStore, appConfigStore, runtimeStore, NullLogger<ProfileConfigService>.Instance);

        await service.DeleteAsync("grim");

        Assert.False(await profileStore.ExistsAsync("grim"));
    }
}

internal sealed class InMemoryProfileConfigStore : IProfileConfigStore
{
    private readonly Dictionary<string, ProfileConfig> _profiles = new(StringComparer.OrdinalIgnoreCase);

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        var profiles = _profiles.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
        return Task.FromResult<IReadOnlyList<string>>(profiles);
    }

    public Task<bool> ExistsAsync(string profileId, CancellationToken ct = default)
        => Task.FromResult(_profiles.ContainsKey(profileId));

    public Task<ProfileConfig> LoadAsync(string profileId, CancellationToken ct = default)
    {
        if (!_profiles.TryGetValue(profileId, out var config))
        {
            throw new FileNotFoundException($"Profile {profileId} not found");
        }

        return Task.FromResult(config with { });
    }

    public Task SaveAsync(string profileId, ProfileConfig config, CancellationToken ct = default)
    {
        _profiles[profileId] = config with { };
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string profileId, CancellationToken ct = default)
    {
        _profiles.Remove(profileId);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryAppConfigStore : IAppConfigStore
{
    private AppConfig _config;

    public InMemoryAppConfigStore(AppConfig config)
    {
        _config = config;
    }

    public Task<AppConfig> LoadAsync(CancellationToken ct = default)
        => Task.FromResult(Clone(_config));

    public Task SaveAsync(AppConfig config, CancellationToken ct = default)
    {
        _config = Clone(config);
        return Task.CompletedTask;
    }

    public Task<AppConfig> UpdateAsync(Func<AppConfig, AppConfig> update, CancellationToken ct = default)
    {
        _config = Clone(update(Clone(_config)));
        return Task.FromResult(Clone(_config));
    }

    private static AppConfig Clone(AppConfig config)
    {
        return config with
        {
            Profiles = config.Profiles with { },
            Models = config.Models with { },
            Persona = config.Persona with { },
            NarrativeRules = config.NarrativeRules with { },
            Lore = config.Lore with { },
            WritingStyle = config.WritingStyle with { },
            Layout = config.Layout with { },
            Roleplay = config.Roleplay with { },
            Forge = config.Forge with { },
            WebSearch = config.WebSearch with { },
            Email = config.Email with { },
            Diagnostics = config.Diagnostics with { },
            Agents = config.Agents with
            {
                Orchestrator = config.Agents.Orchestrator with { },
                NarrativeDirector = config.Agents.NarrativeDirector with { },
                Librarian = config.Agents.Librarian with { },
                ProseWriter = config.Agents.ProseWriter with { },
                ForgePlanner = config.Agents.ForgePlanner with { },
                ForgeWriter = config.Agents.ForgeWriter with { },
                ForgeReviewer = config.Agents.ForgeReviewer with { },
                DelegateTechnical = config.Agents.DelegateTechnical with { },
                Council = config.Agents.Council with { },
                Artifact = config.Agents.Artifact with { },
                Research = config.Agents.Research with { },
            },
            Timeouts = config.Timeouts with { },
            ImageGen = config.ImageGen with
            {
                ComfyUi = config.ImageGen.ComfyUi with { },
                OpenAi = config.ImageGen.OpenAi with { },
            },
            Tts = config.Tts with
            {
                ElevenLabs = config.Tts.ElevenLabs with { },
                OpenAi = config.Tts.OpenAi with { },
            },
        };
    }
}

internal sealed class InMemoryProfileUsageRuntimeStore : ISessionRuntimeStore
{
    private readonly Dictionary<string, List<Guid>> _usageByProfileId = new(StringComparer.OrdinalIgnoreCase);

    public void SetProfileUsage(string profileId, IReadOnlyList<Guid> sessionIds)
    {
        _usageByProfileId[profileId] = [.. sessionIds];
    }

    public Task<SessionRuntimeState> LoadAsync(Guid? sessionId, CancellationToken ct = default)
        => Task.FromResult(new SessionRuntimeState { SessionId = sessionId });

    public Task SaveAsync(SessionRuntimeState state, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<Guid>> FindSessionIdsByProfileIdAsync(string profileId, CancellationToken ct = default)
    {
        if (_usageByProfileId.TryGetValue(profileId, out var sessionIds))
        {
            return Task.FromResult<IReadOnlyList<Guid>>([.. sessionIds]);
        }

        return Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
