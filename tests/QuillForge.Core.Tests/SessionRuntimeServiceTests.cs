using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public sealed class SessionRuntimeServiceTests
{
    private static readonly IMode[] Modes =
    [
        new GeneralMode(),
        new WriterMode(),
        new RoleplayMode(),
        new ForgeMode(),
        new CouncilMode(),
    ];

    [Fact]
    public async Task SetModeAsync_UpdatesModeAndContext()
    {
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);
        var sessionId = Guid.CreateVersion7();

        var result = await service.SetModeAsync(
            sessionId,
            new SetSessionModeCommand("writer", "novel", "chapter1.md", "hero"));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal("writer", result.Value.Mode.ActiveModeName);
        Assert.Equal("novel", result.Value.Mode.ProjectName);
        Assert.Equal("chapter1.md", result.Value.Mode.CurrentFile);
        Assert.Equal("hero", result.Value.Mode.Character);
        Assert.Equal("default", result.Value.Profile.ProfileId);
    }

    [Fact]
    public async Task SetModeAsync_InvalidMode_ReturnsInvalid()
    {
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);

        var result = await service.SetModeAsync(
            Guid.CreateVersion7(),
            new SetSessionModeCommand("missing", null, null, null));

        Assert.Equal(SessionMutationStatus.Invalid, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task SetModeAsync_LeavingWriter_ResetsPendingContent()
    {
        var store = new InMemorySessionRuntimeStore();
        var sessionId = Guid.CreateVersion7();
        await store.SaveAsync(new SessionRuntimeState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState { ActiveModeName = "writer", ProjectName = "novel" },
            Writer = new WriterRuntimeState
            {
                PendingContent = "Pending chapter text",
                State = WriterState.PendingReview,
            },
        });

        var service = CreateService(store);
        var result = await service.SetModeAsync(
            sessionId,
            new SetSessionModeCommand("general", null, null, null));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal(WriterState.Idle, result.Value.Writer.State);
        Assert.Null(result.Value.Writer.PendingContent);
    }

    [Fact]
    public async Task CaptureWriterPendingAsync_CapturesLongWriterResponse()
    {
        var store = new InMemorySessionRuntimeStore();
        var sessionId = Guid.CreateVersion7();
        await store.SaveAsync(new SessionRuntimeState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState { ActiveModeName = "writer" },
        });

        var service = CreateService(store);
        var result = await service.CaptureWriterPendingAsync(
            sessionId,
            new CaptureWriterPendingCommand(new string('x', 300), "writer"));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal(WriterState.PendingReview, result.Value.Writer.State);
        Assert.NotNull(result.Value.Writer.PendingContent);
        Assert.Equal("default", result.Value.Profile.ProfileId);
    }

    [Fact]
    public async Task CaptureWriterPendingAsync_SkipsOutsideWriterMode()
    {
        var store = new InMemorySessionRuntimeStore();
        var sessionId = Guid.CreateVersion7();
        await store.SaveAsync(new SessionRuntimeState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState { ActiveModeName = "general" },
        });

        var service = CreateService(store);
        var result = await service.CaptureWriterPendingAsync(
            sessionId,
            new CaptureWriterPendingCommand(new string('x', 300), "writer"));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal(WriterState.Idle, result.Value.Writer.State);
        Assert.Null(result.Value.Writer.PendingContent);
    }

    [Fact]
    public async Task AcceptWriterPendingAsync_ReturnsContent_AndResetsWriterState()
    {
        var store = new InMemorySessionRuntimeStore();
        var sessionId = Guid.CreateVersion7();
        await store.SaveAsync(new SessionRuntimeState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState { ActiveModeName = "writer" },
            Writer = new WriterRuntimeState
            {
                PendingContent = "Accepted text",
                State = WriterState.PendingReview,
            },
        });

        var service = CreateService(store);
        var result = await service.AcceptWriterPendingAsync(sessionId);

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal("Accepted text", result.Value.AcceptedContent);

        var saved = await store.LoadAsync(sessionId);
        Assert.Equal(WriterState.Idle, saved.Writer.State);
        Assert.Null(saved.Writer.PendingContent);
    }

    [Fact]
    public async Task RejectWriterPendingAsync_ResetsWriterState()
    {
        var store = new InMemorySessionRuntimeStore();
        var sessionId = Guid.CreateVersion7();
        await store.SaveAsync(new SessionRuntimeState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState { ActiveModeName = "writer" },
            Writer = new WriterRuntimeState
            {
                PendingContent = "Rejected text",
                State = WriterState.PendingReview,
            },
        });

        var service = CreateService(store);
        var result = await service.RejectWriterPendingAsync(sessionId);

        Assert.Equal(SessionMutationStatus.Success, result.Status);

        var saved = await store.LoadAsync(sessionId);
        Assert.Equal(WriterState.Idle, saved.Writer.State);
        Assert.Null(saved.Writer.PendingContent);
    }

    [Fact]
    public async Task UpdateNarrativeStateAsync_PersistsDirectorNotes()
    {
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);
        var sessionId = Guid.CreateVersion7();

        var result = await service.UpdateNarrativeStateAsync(
            sessionId,
            new UpdateNarrativeStateCommand("The captain is suspicious but wavering."));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal("The captain is suspicious but wavering.", result.Value.Narrative.DirectorNotes);

        var saved = await store.LoadAsync(sessionId);
        Assert.Equal("The captain is suspicious but wavering.", saved.Narrative.DirectorNotes);
    }

    [Fact]
    public async Task UpdateNarrativeStateAsync_PersistsPlotProgress()
    {
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);
        var sessionId = Guid.CreateVersion7();

        var result = await service.UpdateNarrativeStateAsync(
            sessionId,
            new UpdateNarrativeStateCommand(
                "The party committed to the heist.",
                "heist-arc",
                new PlotProgressUpdate(
                    "vault-entry",
                    ["setup"],
                    ["The guard captain joined the crew."])));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal("vault-entry", result.Value.Narrative.PlotProgress.CurrentBeat);
        Assert.Contains("setup", result.Value.Narrative.PlotProgress.CompletedBeats);
        Assert.Contains("The guard captain joined the crew.", result.Value.Narrative.PlotProgress.Deviations);
    }

    [Fact]
    public async Task LoadViewAsync_HydratesDefaultProfileWithoutPersistingOverrides()
    {
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);
        var sessionId = Guid.CreateVersion7();

        var view = await service.LoadViewAsync(sessionId);

        Assert.Equal("default", view.Profile.ProfileId);
        Assert.Equal("default-conductor", view.Profile.ActiveConductor);
        Assert.Equal("default-lore", view.Profile.ActiveLoreSet);
        Assert.Equal("default-rules", view.Profile.ActiveNarrativeRules);
        Assert.Equal("default-style", view.Profile.ActiveWritingStyle);

        var raw = await store.LoadAsync(sessionId);
        Assert.Null(raw.Profile.ProfileId);
        Assert.Null(raw.Profile.ActiveConductor);
        Assert.Null(raw.Profile.ActiveLoreSet);
        Assert.Null(raw.Profile.ActiveNarrativeRules);
        Assert.Null(raw.Profile.ActiveWritingStyle);
    }

    [Fact]
    public async Task SetProfileAsync_SwitchesBaseProfileAndStoresSparseOverrides()
    {
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);
        var sessionId = Guid.CreateVersion7();

        var result = await service.SetProfileAsync(
            sessionId,
            new SetSessionProfileCommand("grim", null, null, null, null));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal("grim", result.Value.Profile.ProfileId);
        Assert.Equal("grim-conductor", result.Value.Profile.ActiveConductor);
        Assert.Equal("grim-lore", result.Value.Profile.ActiveLoreSet);
        Assert.Equal("grim-rules", result.Value.Profile.ActiveNarrativeRules);
        Assert.Equal("grim-style", result.Value.Profile.ActiveWritingStyle);

        var raw = await store.LoadAsync(sessionId);
        Assert.Equal("grim", raw.Profile.ProfileId);
        Assert.Null(raw.Profile.ActiveConductor);
        Assert.Null(raw.Profile.ActiveLoreSet);
        Assert.Null(raw.Profile.ActiveNarrativeRules);
        Assert.Null(raw.Profile.ActiveWritingStyle);
    }

    [Fact]
    public async Task SetProfileAsync_PreservesSessionOverridesWhenProfileIsUnchanged()
    {
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);
        var sessionId = Guid.CreateVersion7();

        await service.SetProfileAsync(
            sessionId,
            new SetSessionProfileCommand("grim", null, null, null, null));

        var result = await service.SetProfileAsync(
            sessionId,
            new SetSessionProfileCommand(null, null, "custom-lore", null, null));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal("grim", result.Value.Profile.ProfileId);
        Assert.Equal("grim-conductor", result.Value.Profile.ActiveConductor);
        Assert.Equal("custom-lore", result.Value.Profile.ActiveLoreSet);
        Assert.Equal("grim-rules", result.Value.Profile.ActiveNarrativeRules);
        Assert.Equal("grim-style", result.Value.Profile.ActiveWritingStyle);

        var raw = await store.LoadAsync(sessionId);
        Assert.Equal("grim", raw.Profile.ProfileId);
        Assert.Null(raw.Profile.ActiveConductor);
        Assert.Equal("custom-lore", raw.Profile.ActiveLoreSet);
        Assert.Null(raw.Profile.ActiveNarrativeRules);
        Assert.Null(raw.Profile.ActiveWritingStyle);
    }

    [Fact]
    public async Task SetProfileAsync_KeepsDifferentSessionsIndependent()
    {
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);
        var sessionA = Guid.CreateVersion7();
        var sessionB = Guid.CreateVersion7();

        await service.SetProfileAsync(
            sessionA,
            new SetSessionProfileCommand("grim", null, null, null, null));
        await service.SetProfileAsync(
            sessionB,
            new SetSessionProfileCommand(null, "session-b-conductor", null, null, null));

        var viewA = await service.LoadViewAsync(sessionA);
        var viewB = await service.LoadViewAsync(sessionB);

        Assert.Equal("grim", viewA.Profile.ProfileId);
        Assert.Equal("grim-conductor", viewA.Profile.ActiveConductor);
        Assert.Equal("default", viewB.Profile.ProfileId);
        Assert.Equal("session-b-conductor", viewB.Profile.ActiveConductor);
        Assert.Equal("default-lore", viewB.Profile.ActiveLoreSet);
    }

    [Fact]
    public async Task LoadViewAsync_NormalizesLegacyHydratedDefaultsForUntouchedSession()
    {
        var store = new InMemorySessionRuntimeStore();
        var profiles = new FakeProfileConfigService();
        var service = CreateService(store, profiles);
        var sessionId = Guid.CreateVersion7();

        await store.SaveAsync(new SessionRuntimeState
        {
            SessionId = sessionId,
            Profile = new ProfileState
            {
                ProfileId = "grim",
                ActiveConductor = "grim-conductor",
                ActiveLoreSet = "grim-lore",
                ActiveNarrativeRules = "grim-rules",
                ActiveWritingStyle = "grim-style",
            },
        });

        profiles.SetProfile("grim", new ProfileConfig
        {
            Conductor = "grim-conductor-v2",
            LoreSet = "grim-lore-v2",
            NarrativeRules = "grim-rules-v2",
            WritingStyle = "grim-style-v2",
        });

        var view = await service.LoadViewAsync(sessionId);

        Assert.Equal("grim", view.Profile.ProfileId);
        Assert.Equal("grim-conductor-v2", view.Profile.ActiveConductor);
        Assert.Equal("grim-lore-v2", view.Profile.ActiveLoreSet);
        Assert.Equal("grim-rules-v2", view.Profile.ActiveNarrativeRules);
        Assert.Equal("grim-style-v2", view.Profile.ActiveWritingStyle);

        var raw = await store.LoadAsync(sessionId);
        Assert.Equal("grim", raw.Profile.ProfileId);
        Assert.Null(raw.Profile.ActiveConductor);
        Assert.Null(raw.Profile.ActiveLoreSet);
        Assert.Null(raw.Profile.ActiveNarrativeRules);
        Assert.Null(raw.Profile.ActiveWritingStyle);
    }

    [Fact]
    public async Task LoadViewAsync_ProfileEditsFlowThroughSparseSessionsWhileExplicitOverridesRemainSticky()
    {
        var store = new InMemorySessionRuntimeStore();
        var profiles = new FakeProfileConfigService();
        var service = CreateService(store, profiles);
        var sessionId = Guid.CreateVersion7();

        await service.SetProfileAsync(
            sessionId,
            new SetSessionProfileCommand("grim", null, null, null, null));
        await service.SetProfileAsync(
            sessionId,
            new SetSessionProfileCommand(null, null, "custom-lore", null, null));

        profiles.SetProfile("grim", new ProfileConfig
        {
            Conductor = "grim-conductor-v2",
            LoreSet = "grim-lore-v2",
            NarrativeRules = "grim-rules-v2",
            WritingStyle = "grim-style-v2",
        });

        var view = await service.LoadViewAsync(sessionId);

        Assert.Equal("grim", view.Profile.ProfileId);
        Assert.Equal("grim-conductor-v2", view.Profile.ActiveConductor);
        Assert.Equal("custom-lore", view.Profile.ActiveLoreSet);
        Assert.Equal("grim-rules-v2", view.Profile.ActiveNarrativeRules);
        Assert.Equal("grim-style-v2", view.Profile.ActiveWritingStyle);

        var raw = await store.LoadAsync(sessionId);
        Assert.Equal("grim", raw.Profile.ProfileId);
        Assert.Null(raw.Profile.ActiveConductor);
        Assert.Equal("custom-lore", raw.Profile.ActiveLoreSet);
        Assert.Null(raw.Profile.ActiveNarrativeRules);
        Assert.Null(raw.Profile.ActiveWritingStyle);
    }

    [Fact]
    public async Task LoadViewAsync_DoesNotCollapseExplicitFullOverridesForNonDefaultRuntimeState()
    {
        var store = new InMemorySessionRuntimeStore();
        var profiles = new FakeProfileConfigService();
        var service = CreateService(store, profiles);
        var sessionId = Guid.CreateVersion7();

        await store.SaveAsync(new SessionRuntimeState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState { ActiveModeName = "writer" },
            Profile = new ProfileState
            {
                ProfileId = "grim",
                ActiveConductor = "custom-conductor",
                ActiveLoreSet = "custom-lore",
                ActiveNarrativeRules = "custom-rules",
                ActiveWritingStyle = "custom-style",
            },
        });

        profiles.SetProfile("grim", new ProfileConfig
        {
            Conductor = "grim-conductor-v2",
            LoreSet = "grim-lore-v2",
            NarrativeRules = "grim-rules-v2",
            WritingStyle = "grim-style-v2",
        });

        var view = await service.LoadViewAsync(sessionId);

        Assert.Equal("custom-conductor", view.Profile.ActiveConductor);
        Assert.Equal("custom-lore", view.Profile.ActiveLoreSet);
        Assert.Equal("custom-rules", view.Profile.ActiveNarrativeRules);
        Assert.Equal("custom-style", view.Profile.ActiveWritingStyle);

        var raw = await store.LoadAsync(sessionId);
        Assert.Equal("custom-conductor", raw.Profile.ActiveConductor);
        Assert.Equal("custom-lore", raw.Profile.ActiveLoreSet);
        Assert.Equal("custom-rules", raw.Profile.ActiveNarrativeRules);
        Assert.Equal("custom-style", raw.Profile.ActiveWritingStyle);
    }

    [Fact]
    public async Task UpdateNarrativeStateAsync_RejectsEmptyNotes()
    {
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);

        var result = await service.UpdateNarrativeStateAsync(
            Guid.CreateVersion7(),
            new UpdateNarrativeStateCommand(""));

        Assert.Equal(SessionMutationStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task MutationGate_RejectsOverlappingSameSessionMutation()
    {
        var gate = new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance);
        var sessionId = Guid.CreateVersion7();

        await using var lease = await gate.TryAcquireAsync(sessionId, "test");
        Assert.NotNull(lease);

        var secondLease = await gate.TryAcquireAsync(sessionId, "test");
        Assert.Null(secondLease);
    }

    [Fact]
    public async Task MutationGate_AllowsDifferentSessionsInParallel()
    {
        var gate = new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance);

        await using var leaseA = await gate.TryAcquireAsync(Guid.CreateVersion7(), "test");
        var leaseB = await gate.TryAcquireAsync(Guid.CreateVersion7(), "test");

        Assert.NotNull(leaseA);
        Assert.NotNull(leaseB);
        if (leaseB is not null)
        {
            await leaseB.DisposeAsync();
        }
    }

    [Fact]
    public async Task MutationGate_ReleasesLeaseAfterDispose()
    {
        var gate = new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance);
        var sessionId = Guid.CreateVersion7();

        var firstLease = await gate.TryAcquireAsync(sessionId, "test");
        Assert.NotNull(firstLease);
        if (firstLease is not null)
        {
            await firstLease.DisposeAsync();
        }

        var secondLease = await gate.TryAcquireAsync(sessionId, "test");
        Assert.NotNull(secondLease);
        if (secondLease is not null)
        {
            await secondLease.DisposeAsync();
        }
    }

    [Fact]
    public async Task SetActivePlotAsync_LoadsPlotIntoSessionAndResetsProgress()
    {
        var store = new InMemorySessionRuntimeStore();
        var sessionId = Guid.CreateVersion7();
        await store.SaveAsync(new SessionRuntimeState
        {
            SessionId = sessionId,
            Narrative = new NarrativeRuntimeState
            {
                DirectorNotes = "Old notes",
                ActivePlotFile = "old-arc",
                PlotProgress = new PlotProgressState
                {
                    CurrentBeat = "old-beat",
                    CompletedBeats = ["old-step"],
                    Deviations = ["old deviation"],
                },
            },
        });

        var service = CreateService(store);
        var result = await service.SetActivePlotAsync(sessionId, new SetActivePlotCommand("new-arc"));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal("new-arc", result.Value.Narrative.ActivePlotFile);
        Assert.Null(result.Value.Narrative.PlotProgress.CurrentBeat);
        Assert.Empty(result.Value.Narrative.PlotProgress.CompletedBeats);
        Assert.Empty(result.Value.Narrative.PlotProgress.Deviations);
    }

    [Fact]
    public async Task ClearActivePlotAsync_ClearsPlotAndProgress()
    {
        var store = new InMemorySessionRuntimeStore();
        var sessionId = Guid.CreateVersion7();
        await store.SaveAsync(new SessionRuntimeState
        {
            SessionId = sessionId,
            Narrative = new NarrativeRuntimeState
            {
                ActivePlotFile = "new-arc",
                PlotProgress = new PlotProgressState
                {
                    CurrentBeat = "midpoint",
                    CompletedBeats = ["opening"],
                    Deviations = ["Skipped the duel."],
                },
            },
        });

        var service = CreateService(store);
        var result = await service.ClearActivePlotAsync(sessionId);

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Null(result.Value.Narrative.ActivePlotFile);
        Assert.Null(result.Value.Narrative.PlotProgress.CurrentBeat);
        Assert.Empty(result.Value.Narrative.PlotProgress.CompletedBeats);
        Assert.Empty(result.Value.Narrative.PlotProgress.Deviations);
    }

    private static SessionRuntimeService CreateService(
        InMemorySessionRuntimeStore store,
        FakeProfileConfigService? profileService = null)
    {
        return new SessionRuntimeService(
            store,
            new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance),
            profileService ?? new FakeProfileConfigService(),
            Modes,
            NullLogger<SessionRuntimeService>.Instance);
    }
}

internal sealed class InMemorySessionRuntimeStore : ISessionRuntimeStore
{
    private readonly Dictionary<string, SessionRuntimeState> _states = [];

    public Task<SessionRuntimeState> LoadAsync(Guid? sessionId, CancellationToken ct = default)
    {
        if (_states.TryGetValue(GetKey(sessionId), out var state))
        {
            return Task.FromResult(Clone(state, sessionId));
        }

        return Task.FromResult(new SessionRuntimeState { SessionId = sessionId });
    }

    public Task SaveAsync(SessionRuntimeState state, CancellationToken ct = default)
    {
        _states[GetKey(state.SessionId)] = Clone(state, state.SessionId);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
    {
        _states.Remove(GetKey(sessionId));
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> FindSessionIdsByProfileIdAsync(string profileId, CancellationToken ct = default)
    {
        var sessionIds = _states
            .Where(pair =>
            {
                var state = pair.Value;
                return state.SessionId.HasValue
                    && string.Equals(state.Profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase);
            })
            .Select(pair => pair.Value.SessionId!.Value)
            .ToList();
        return Task.FromResult<IReadOnlyList<Guid>>(sessionIds);
    }

    private static string GetKey(Guid? sessionId)
    {
        return sessionId?.ToString() ?? "default";
    }

    private static SessionRuntimeState Clone(SessionRuntimeState state, Guid? sessionId)
    {
        return new SessionRuntimeState
        {
            SessionId = sessionId,
            LastModified = state.LastModified,
            Mode = new ModeSelectionState
            {
                ActiveModeName = state.Mode.ActiveModeName,
                ProjectName = state.Mode.ProjectName,
                CurrentFile = state.Mode.CurrentFile,
                Character = state.Mode.Character,
            },
            Profile = new ProfileState
            {
                ProfileId = state.Profile.ProfileId,
                ActiveConductor = state.Profile.ActiveConductor,
                ActiveLoreSet = state.Profile.ActiveLoreSet,
                ActiveNarrativeRules = state.Profile.ActiveNarrativeRules,
                ActiveWritingStyle = state.Profile.ActiveWritingStyle,
            },
            Writer = new WriterRuntimeState
            {
                PendingContent = state.Writer.PendingContent,
                State = state.Writer.State,
            },
            Narrative = new NarrativeRuntimeState
            {
                DirectorNotes = state.Narrative.DirectorNotes,
                ActivePlotFile = state.Narrative.ActivePlotFile,
                PlotProgress = new PlotProgressState
                {
                    CurrentBeat = state.Narrative.PlotProgress.CurrentBeat,
                    CompletedBeats = [.. state.Narrative.PlotProgress.CompletedBeats],
                    Deviations = [.. state.Narrative.PlotProgress.Deviations],
                },
            },
        };
    }
}

internal sealed class FakeProfileConfigService : IProfileConfigService
{
    private readonly Dictionary<string, ProfileConfig> _profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = new()
        {
            Conductor = "default-conductor",
            LoreSet = "default-lore",
            NarrativeRules = "default-rules",
            WritingStyle = "default-style",
        },
        ["grim"] = new()
        {
            Conductor = "grim-conductor",
            LoreSet = "grim-lore",
            NarrativeRules = "grim-rules",
            WritingStyle = "grim-style",
        },
    };

    public string DefaultProfileId { get; set; } = "default";

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(_profiles.Keys.OrderBy(k => k).ToList());

    public Task<string> GetDefaultProfileIdAsync(CancellationToken ct = default)
        => Task.FromResult(DefaultProfileId);

    public Task<ResolvedProfileConfig> LoadResolvedAsync(string? profileId = null, CancellationToken ct = default)
    {
        var resolvedProfileId = string.IsNullOrWhiteSpace(profileId) ? DefaultProfileId : profileId.Trim();
        if (!_profiles.TryGetValue(resolvedProfileId, out var config))
        {
            throw new FileNotFoundException($"Profile config {resolvedProfileId} not found");
        }

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

    public void SetProfile(string profileId, ProfileConfig config)
    {
        _profiles[profileId] = config;
    }

    public async Task<ProfileState> BuildSessionProfileStateAsync(string? profileId = null, CancellationToken ct = default)
    {
        var resolved = await LoadResolvedAsync(profileId, ct);
        return new ProfileState
        {
            ProfileId = resolved.ProfileId,
        };
    }
}
