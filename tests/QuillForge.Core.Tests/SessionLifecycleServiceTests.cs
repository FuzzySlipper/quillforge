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
        sourceTree.Append(
            sourceTree.ActiveLeafId,
            "assistant",
            new MessageContent("Reply"),
            new MessageMetadata
            {
                StopReason = StopReason.EndTurn,
                Reasoning = "Preserve the explanation.",
                ProviderReplay = new ReasoningReplayEnvelope(
                    "Reply",
                    "Preserve the explanation.",
                    []),
            });
        await sessionStore.SaveAsync(sourceTree);

        await runtimeStore.SaveAsync(new SessionState
        {
            SessionId = sourceTree.SessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Writer,
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
                PendingProjectName = "novel",
                PendingFileName = "chapter-1.md",
                State = WriterState.PendingReview,
            },
            Narrative = new NarrativeRuntimeState
            {
                DirectorNotes = "Keep the pressure rising.",
                StickySessionCanon = "- The rival saw the map.\n- The captain already distrusts the envoy.",
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
        Assert.Equal("Preserve the explanation.", forkedTree.ToFlatThread()[1].Metadata?.Reasoning);
        var replay = Assert.IsType<ReasoningReplayEnvelope>(forkedTree.ToFlatThread()[1].Metadata?.ProviderReplay);
        Assert.Equal("Preserve the explanation.", replay.ReasoningContent);
        Assert.Equal(Mode.Writer, forkedRuntime.Mode.ActiveMode);
        Assert.Equal("novel", forkedRuntime.Mode.ProjectName);
        Assert.Equal("grim", forkedRuntime.Profile.ProfileId);
        Assert.Equal("custom-lore", forkedRuntime.Profile.ActiveLoreSet);
        Assert.Equal("captain", forkedRuntime.Roleplay.ActiveAiCharacter);
        Assert.Equal("envoy", forkedRuntime.Roleplay.ActiveUserCharacter);
        Assert.Null(forkedRuntime.Writer.PendingContent);
        Assert.Null(forkedRuntime.Writer.PendingProjectName);
        Assert.Null(forkedRuntime.Writer.PendingFileName);
        Assert.Equal(WriterState.Idle, forkedRuntime.Writer.State);
        Assert.Equal("Keep the pressure rising.", forkedRuntime.Narrative.DirectorNotes);
        Assert.Contains("The rival saw the map.", forkedRuntime.Narrative.StickySessionCanon);
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
            Mode = new ModeSelectionState { ActiveMode = Mode.Roleplay, Character = "captain" },
            Profile = new ProfileState { ProfileId = "grim" },
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
                StickySessionCanon = "- The envoy promised the signet ring by dawn.",
            },
        });

        var forkedTree = await service.ForkAsync(sourceTree.SessionId, assistant1.Id);
        var flatThread = forkedTree.ToFlatThread();
        var forkedRuntime = await runtimeStore.LoadAsync(forkedTree.SessionId);

        Assert.Equal(2, flatThread.Count);
        Assert.Equal("One", flatThread[0].Content.GetText());
        Assert.Equal("Two", flatThread[1].Content.GetText());
        Assert.Equal(Mode.Roleplay, forkedRuntime.Mode.ActiveMode);
        Assert.Equal("captain", forkedRuntime.Mode.Character);
        Assert.Equal("grim", forkedRuntime.Profile.ProfileId);
        Assert.Equal("captain", forkedRuntime.Roleplay.ActiveAiCharacter);
        Assert.Equal("envoy", forkedRuntime.Roleplay.ActiveUserCharacter);
        Assert.Equal("The captain already distrusts the envoy.", forkedRuntime.Narrative.DirectorNotes);
        Assert.Contains("The envoy promised the signet ring by dawn.", forkedRuntime.Narrative.StickySessionCanon);
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
            Mode = new ModeSelectionState { ActiveMode = Mode.Writer },
        });

        await service.DeleteAsync(sessionId);

        await Assert.ThrowsAsync<FileNotFoundException>(() => sessionStore.LoadAsync(sessionId));
        var runtime = await runtimeStore.LoadAsync(sessionId);
        Assert.Equal(sessionId, runtime.SessionId);
        Assert.Equal(Mode.Guide, runtime.Mode.ActiveMode);
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
                ActiveMode = state.Mode.ActiveMode,
                ProjectName = state.Mode.ProjectName,
                CurrentFile = state.Mode.CurrentFile,
                Character = state.Mode.Character,
            },
            Profile = new ProfileState
            {
                ProfileId = state.Profile.ProfileId,
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
                PendingProjectName = state.Writer.PendingProjectName,
                PendingFileName = state.Writer.PendingFileName,
                State = state.Writer.State,
            },
            Narrative = new NarrativeRuntimeState
            {
                DirectorNotes = state.Narrative.DirectorNotes,
                StickySessionCanon = state.Narrative.StickySessionCanon,
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
