using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public sealed class SessionBootstrapServiceTests
{
    [Fact]
    public async Task CreateAsync_PersistsConversationAndSeededRuntimeState()
    {
        var sessionStore = new InMemorySessionStore();
        var runtimeStore = new InMemoryRuntimeStore();
        var profileService = new BootstrapProfileConfigService();
        var service = new SessionBootstrapService(
            sessionStore,
            runtimeStore,
            profileService,
            NullLoggerFactory.Instance,
            NullLogger<SessionBootstrapService>.Instance);

        var tree = await service.CreateAsync(new CreateSessionCommand
        {
            Name = "Bootstrap Session",
            ProfileId = "grim",
        });

        var persistedTree = await sessionStore.LoadAsync(tree.SessionId);
        var runtimeState = await runtimeStore.LoadAsync(tree.SessionId);

        Assert.Equal("Bootstrap Session", persistedTree.Name);
        Assert.Equal(tree.SessionId, runtimeState.SessionId);
        Assert.Equal("grim", runtimeState.Profile.ProfileId);
        Assert.Null(runtimeState.Profile.ActiveConductor);
        Assert.Equal("grim-guide", runtimeState.Roleplay.ActiveAiCharacter);
        Assert.Equal("grim-author", runtimeState.Roleplay.ActiveUserCharacter);
        Assert.Equal("general", runtimeState.Mode.ActiveModeName);
    }

    [Fact]
    public async Task CreateAsync_UsesRequestedSessionId_WhenProvided()
    {
        var sessionStore = new InMemorySessionStore();
        var runtimeStore = new InMemoryRuntimeStore();
        var service = new SessionBootstrapService(
            sessionStore,
            runtimeStore,
            new BootstrapProfileConfigService(),
            NullLoggerFactory.Instance,
            NullLogger<SessionBootstrapService>.Instance);
        var requestedId = Guid.CreateVersion7();

        var tree = await service.CreateAsync(new CreateSessionCommand
        {
            SessionId = requestedId,
            Name = "Debug Session",
        });

        Assert.Equal(requestedId, tree.SessionId);
        var runtimeState = await runtimeStore.LoadAsync(requestedId);
        Assert.Equal(requestedId, runtimeState.SessionId);
    }

    [Fact]
    public async Task CreateAsync_RuntimeSaveFailure_RollsBackPersistedSession()
    {
        var sessionStore = new InMemorySessionStore();
        var runtimeStore = new FailingRuntimeStore();
        var service = new SessionBootstrapService(
            sessionStore,
            runtimeStore,
            new BootstrapProfileConfigService(),
            NullLoggerFactory.Instance,
            NullLogger<SessionBootstrapService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CreateAsync(new CreateSessionCommand { Name = "Rollback Session" }));

        // No sessions should remain after the rollback
        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            sessionStore.LoadAsync(runtimeStore.AttemptedSessionId!.Value));
    }

    private sealed class BootstrapProfileConfigService : IProfileConfigService
    {
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["default", "grim"]);

        public Task<string> GetDefaultProfileIdAsync(CancellationToken ct = default)
            => Task.FromResult("default");

        public Task<ResolvedProfileConfig> LoadResolvedAsync(string? profileId = null, CancellationToken ct = default)
        {
            var resolvedProfileId = string.IsNullOrWhiteSpace(profileId) ? "default" : profileId;
            var config = resolvedProfileId == "grim"
                ? new ProfileConfig
                {
                    Conductor = "grim-conductor",
                    LoreSet = "grim-lore",
                    NarrativeRules = "grim-rules",
                    WritingStyle = "grim-style",
                    Roleplay = new RoleplayConfig
                    {
                        AiCharacter = "grim-guide",
                        UserCharacter = "grim-author",
                    },
                }
                : new ProfileConfig
                {
                    Conductor = "default-conductor",
                    LoreSet = "default-lore",
                    NarrativeRules = "default-rules",
                    WritingStyle = "default-style",
                    Roleplay = new RoleplayConfig
                    {
                        AiCharacter = "default-guide",
                        UserCharacter = "default-author",
                    },
                };

            return Task.FromResult(new ResolvedProfileConfig
            {
                ProfileId = resolvedProfileId,
                Config = config,
                Persisted = true,
            });
        }

        public Task<ResolvedProfileConfig> SaveAsync(string profileId, ProfileConfig config, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ResolvedProfileConfig> CloneAsync(string sourceProfileId, string targetProfileId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(string profileId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProfileSelectionResult> SelectAsync(string profileId, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProfileSelectionResult> SaveAndSelectAsync(string profileId, ProfileConfig config, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<ProfileState> BuildSessionProfileStateAsync(string? profileId = null, CancellationToken ct = default)
        {
            return Task.FromResult(new ProfileState
            {
                ProfileId = profileId ?? "default",
            });
        }
    }

    private sealed class FailingRuntimeStore : ISessionRuntimeStore
    {
        public Guid? AttemptedSessionId { get; private set; }

        public Task<SessionRuntimeState> LoadAsync(Guid? sessionId, CancellationToken ct = default)
            => Task.FromResult(new SessionRuntimeState { SessionId = sessionId });

        public Task SaveAsync(SessionRuntimeState state, CancellationToken ct = default)
        {
            AttemptedSessionId = state.SessionId;
            throw new InvalidOperationException("Simulated runtime store failure");
        }

        public Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<Guid>> FindSessionIdsByProfileIdAsync(string profileId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
