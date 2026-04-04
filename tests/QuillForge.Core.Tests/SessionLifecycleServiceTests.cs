using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public sealed class SessionLifecycleServiceTests
{
    [Fact]
    public async Task ForkAsync_ClonesConversationAndRuntimeState_AndClearsWriterPending()
    {
        var sessionStore = new InMemorySessionStore();
        var runtimeStore = new InMemoryRuntimeStore();
        var service = CreateService(sessionStore, runtimeStore);

        var sourceTree = new ConversationTree(
            Guid.CreateVersion7(),
            "Original Session",
            NullLogger<ConversationTree>.Instance);
        sourceTree.Append(sourceTree.RootId, "user", new MessageContent("Prompt"));
        sourceTree.Append(sourceTree.ActiveLeafId, "assistant", new MessageContent("Reply"));
        await sessionStore.SaveAsync(sourceTree);

        await runtimeStore.SaveAsync(new SessionState
        {
            SessionId = sourceTree.SessionId,
            Mode = new ModeSelectionState
            {
                ActiveModeName = "writer",
                ProjectName = "novel",
                CurrentFile = "chapter-1.md",
            },
            Profile = new ProfileState
            {
                ProfileId = "grim",
                ActiveLoreSet = "custom-lore",
            },
            Roleplay = new RoleplayRuntimeState
            {
                HasExplicitAiCharacterSelection = true,
                ActiveAiCharacter = "captain",
                HasExplicitUserCharacterSelection = true,
                ActiveUserCharacter = "envoy",
            },
            Writer = new WriterRuntimeState
            {
                PendingContent = "Pending review text",
                State = WriterState.PendingReview,
            },
            Narrative = new NarrativeRuntimeState
            {
                DirectorNotes = "Keep the pressure rising.",
                ActivePlotFile = "gate-arc",
                PlotProgress = new PlotProgressState
                {
                    CurrentBeat = "midpoint",
                    CompletedBeats = ["opening"],
                    Deviations = ["The rival saw the map."],
                },
            },
        });

        var forkedTree = await service.ForkAsync(sourceTree.SessionId);
        var forkedRuntime = await runtimeStore.LoadAsync(forkedTree.SessionId);

        Assert.NotEqual(sourceTree.SessionId, forkedTree.SessionId);
        Assert.Equal("Fork of Original Session", forkedTree.Name);
        Assert.Equal(2, forkedTree.ToFlatThread().Count);
        Assert.Equal("writer", forkedRuntime.Mode.ActiveModeName);
        Assert.Equal("novel", forkedRuntime.Mode.ProjectName);
        Assert.Equal("grim", forkedRuntime.Profile.ProfileId);
        Assert.Equal("custom-lore", forkedRuntime.Profile.ActiveLoreSet);
        Assert.Equal("captain", forkedRuntime.Roleplay.ActiveAiCharacter);
        Assert.Equal("envoy", forkedRuntime.Roleplay.ActiveUserCharacter);
        Assert.Null(forkedRuntime.Writer.PendingContent);
        Assert.Equal(WriterState.Idle, forkedRuntime.Writer.State);
        Assert.Equal("Keep the pressure rising.", forkedRuntime.Narrative.DirectorNotes);
        Assert.Equal("gate-arc", forkedRuntime.Narrative.ActivePlotFile);
        Assert.Equal("midpoint", forkedRuntime.Narrative.PlotProgress.CurrentBeat);
        Assert.Contains("opening", forkedRuntime.Narrative.PlotProgress.CompletedBeats);
        Assert.Contains("The rival saw the map.", forkedRuntime.Narrative.PlotProgress.Deviations);
    }

    [Fact]
    public async Task ForkAsync_FromEarlierMessage_UsesThreadUpToMessage_ButClonesCurrentRuntimeShape()
    {
        var sessionStore = new InMemorySessionStore();
        var runtimeStore = new InMemoryRuntimeStore();
        var service = CreateService(sessionStore, runtimeStore);

        var sourceTree = new ConversationTree(
            Guid.CreateVersion7(),
            "Branch Source",
            NullLogger<ConversationTree>.Instance);
        var user1 = sourceTree.Append(sourceTree.RootId, "user", new MessageContent("One"));
        var assistant1 = sourceTree.Append(user1.Id, "assistant", new MessageContent("Two"));
        sourceTree.Append(assistant1.Id, "user", new MessageContent("Three"));
        await sessionStore.SaveAsync(sourceTree);

        await runtimeStore.SaveAsync(new SessionState
        {
            SessionId = sourceTree.SessionId,
            Mode = new ModeSelectionState { ActiveModeName = "roleplay", Character = "captain" },
            Profile = new ProfileState { ProfileId = "grim", ActiveConductor = "grim-captain" },
            Roleplay = new RoleplayRuntimeState
            {
                HasExplicitAiCharacterSelection = true,
                ActiveAiCharacter = "captain",
                HasExplicitUserCharacterSelection = true,
                ActiveUserCharacter = "envoy",
            },
            Narrative = new NarrativeRuntimeState
            {
                DirectorNotes = "The captain already distrusts the envoy.",
            },
        });

        var forkedTree = await service.ForkAsync(sourceTree.SessionId, assistant1.Id);
        var flatThread = forkedTree.ToFlatThread();
        var forkedRuntime = await runtimeStore.LoadAsync(forkedTree.SessionId);

        Assert.Equal(2, flatThread.Count);
        Assert.Equal("One", flatThread[0].Content.GetText());
        Assert.Equal("Two", flatThread[1].Content.GetText());
        Assert.Equal("roleplay", forkedRuntime.Mode.ActiveModeName);
        Assert.Equal("captain", forkedRuntime.Mode.Character);
        Assert.Equal("grim", forkedRuntime.Profile.ProfileId);
        Assert.Equal("grim-captain", forkedRuntime.Profile.ActiveConductor);
        Assert.Equal("captain", forkedRuntime.Roleplay.ActiveAiCharacter);
        Assert.Equal("envoy", forkedRuntime.Roleplay.ActiveUserCharacter);
        Assert.Equal("The captain already distrusts the envoy.", forkedRuntime.Narrative.DirectorNotes);
    }

    [Fact]
    public async Task DeleteAsync_RemovesConversationAndRuntimeTogether()
    {
        var sessionStore = new InMemorySessionStore();
        var runtimeStore = new InMemoryRuntimeStore();
        var service = CreateService(sessionStore, runtimeStore);

        var sessionId = Guid.CreateVersion7();
        await sessionStore.SaveAsync(new ConversationTree(sessionId, "Delete Me", NullLogger<ConversationTree>.Instance));
        await runtimeStore.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState { ActiveModeName = "writer" },
        });

        await service.DeleteAsync(sessionId);

        await Assert.ThrowsAsync<FileNotFoundException>(() => sessionStore.LoadAsync(sessionId));
        var runtime = await runtimeStore.LoadAsync(sessionId);
        Assert.Equal(sessionId, runtime.SessionId);
        Assert.Equal("general", runtime.Mode.ActiveModeName);
    }

    private static SessionLifecycleService CreateService(
        InMemorySessionStore sessionStore,
        InMemoryRuntimeStore runtimeStore)
    {
        return new SessionLifecycleService(
            sessionStore,
            runtimeStore,
            NullLoggerFactory.Instance,
            NullLogger<SessionLifecycleService>.Instance);
    }
}

internal sealed class InMemorySessionStore : ISessionStore
{
    private readonly Dictionary<Guid, ConversationTree> _sessions = [];

    public Task<ConversationTree> LoadAsync(Guid sessionId, CancellationToken ct = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session))
        {
            throw new FileNotFoundException($"Session not found: {sessionId}");
        }

        return Task.FromResult(session);
    }

    public Task SaveAsync(ConversationTree session, CancellationToken ct = default)
    {
        _sessions[session.SessionId] = session;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<SessionSummary>>([]);

    public Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
    {
        _sessions.Remove(sessionId);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryRuntimeStore : ISessionStateStore
{
    private readonly Dictionary<Guid, SessionState> _states = [];

    public Task<SessionState> LoadAsync(Guid? sessionId, CancellationToken ct = default)
    {
        if (sessionId.HasValue && _states.TryGetValue(sessionId.Value, out var state))
        {
            return Task.FromResult(Clone(state, sessionId));
        }

        return Task.FromResult(new SessionState { SessionId = sessionId });
    }

    public Task SaveAsync(SessionState state, CancellationToken ct = default)
    {
        if (state.SessionId.HasValue)
        {
            _states[state.SessionId.Value] = Clone(state, state.SessionId);
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
    {
        _states.Remove(sessionId);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> FindSessionIdsByProfileIdAsync(string profileId, CancellationToken ct = default)
    {
        var sessionIds = _states
            .Where(pair => string.Equals(pair.Value.Profile.ProfileId, profileId, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .ToList();
        return Task.FromResult<IReadOnlyList<Guid>>(sessionIds);
    }

    private static SessionState Clone(SessionState state, Guid? sessionId)
    {
        return new SessionState
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
            Roleplay = new RoleplayRuntimeState
            {
                HasExplicitAiCharacterSelection = state.Roleplay.HasExplicitAiCharacterSelection,
                ActiveAiCharacter = state.Roleplay.ActiveAiCharacter,
                HasExplicitUserCharacterSelection = state.Roleplay.HasExplicitUserCharacterSelection,
                ActiveUserCharacter = state.Roleplay.ActiveUserCharacter,
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
