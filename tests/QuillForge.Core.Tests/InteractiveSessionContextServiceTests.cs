using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

public sealed class InteractiveSessionContextServiceTests
{
    [Fact]
    public async Task BuildAsync_CollectsCharacterStoryAndFileContext()
    {
        var runtimeService = new FakeRuntimeViewService();
        var cardStore = new FakeCharacterCardStoreForContext();
        var storyState = new StoryStateServiceWithData(new Dictionary<string, object>
        {
            ["tension"] = "high",
            ["location"] = "keep",
        });
        var sessionStore = new InMemoryInteractiveSessionStore();
        var sessionId = Guid.CreateVersion7();
        var tree = new ConversationTree(sessionId, "Roleplay Session", NullLogger<ConversationTree>.Instance);
        tree.Append(tree.ActiveLeafId, "user", new MessageContent("Captain Rowe says contraband is moving through the tide tunnels."));
        tree.Append(tree.ActiveLeafId, "assistant", new MessageContent("Sir Rowan closes his hand over the brass compass and glances toward the trapdoor."));
        await sessionStore.SaveAsync(tree);
        var files = new FakeContentFileService();
        files.SeedFile("story/novel/chapter1.md", new string('a', 520));
        var plots = new FakePlotStore();
        plots.Set("gate-arc", "# Gate Arc\n\n- Beat one");

        var service = new InteractiveSessionContextService(
            runtimeService,
            sessionStore,
            cardStore,
            storyState,
            files,
            plots,
            NullLogger<InteractiveSessionContextService>.Instance);

        var context = await service.BuildAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Roleplay,
                ProjectName = "novel",
                CurrentFile = "chapter1.md",
                Character = "hero",
            },
            Writer = new WriterRuntimeState
            {
                PendingContent = "Pending scene text",
                State = WriterState.PendingReview,
            },
            Narrative = new NarrativeRuntimeState
            {
                DirectorNotes = "Captain is wavering.",
                StickySessionCanon = "- Captain Rowe suspects contraband in the tide tunnels.",
                ActivePlotFile = "gate-arc",
                PlotProgress = new PlotProgressState
                {
                    CurrentBeat = "gate-confrontation",
                    CompletedBeats = ["arrival"],
                    Deviations = ["The captain recognized the smuggler's crest."],
                },
            },
        });

        Assert.Equal(Mode.Roleplay, context.ActiveMode);
        Assert.Equal("novel", context.ProjectName);
        Assert.Equal("novel/.state.yaml", context.StoryStatePath);
        Assert.Equal("Character: Sir Rowan", context.CharacterSection);
        Assert.Contains("tension", context.StoryStateSummary);
        Assert.NotNull(context.FileContext);
        Assert.StartsWith("...\n", context.FileContext, StringComparison.Ordinal);
        Assert.Equal("Pending scene text", context.WriterPendingContent);
        Assert.Contains("Captain Rowe suspects contraband", context.StickySessionCanon);
        Assert.Contains("User: Captain Rowe says contraband is moving through the tide tunnels.", context.RecentConversationSummary);
        Assert.Contains("Assistant: Sir Rowan closes his hand over the brass compass", context.RecentConversationSummary);
        Assert.Equal("gate-arc", context.ActivePlotFile);
        Assert.Contains("Beat one", context.ActivePlotContent);
        Assert.Contains("Current beat: gate-confrontation", context.PlotProgressSummary);
    }

    [Fact]
    public async Task LoadAsync_UsesRuntimeServiceState()
    {
        var runtimeService = new FakeRuntimeViewService
        {
            State = new SessionState
            {
                SessionId = Guid.CreateVersion7(),
                Mode = new ModeSelectionState
                {
                    ActiveMode = Mode.Writer,
                    ProjectName = "novel",
                },
            },
        };

        var service = new InteractiveSessionContextService(
            runtimeService,
            new InMemoryInteractiveSessionStore(),
            new FakeCharacterCardStoreForContext(),
            new StoryStateServiceWithData(new Dictionary<string, object>()),
            new FakeContentFileService(),
            new FakePlotStore(),
            NullLogger<InteractiveSessionContextService>.Instance);

        var context = await service.LoadAsync(runtimeService.State.SessionId);

        Assert.Equal(Mode.Writer, context.ActiveMode);
        Assert.Equal("novel", context.ProjectName);
        Assert.Equal("novel/.state.yaml", context.StoryStatePath);
    }

    [Fact]
    public async Task BuildAsync_SubstitutesRoleplayShortcodesInCharacterSection()
    {
        var runtimeService = new FakeRuntimeViewService();
        var cardStore = new FakeCharacterCardStoreWithShortcodes();
        var storyState = new StoryStateServiceWithData(new Dictionary<string, object>());
        var sessionStore = new InMemoryInteractiveSessionStore();
        var files = new FakeContentFileService();
        var plots = new FakePlotStore();

        var service = new InteractiveSessionContextService(
            runtimeService,
            sessionStore,
            cardStore,
            storyState,
            files,
            plots,
            NullLogger<InteractiveSessionContextService>.Instance);

        var context = await service.BuildAsync(new SessionState
        {
            SessionId = Guid.CreateVersion7(),
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Roleplay,
                ProjectName = "novel",
                Character = "aurora",
            },
            Roleplay = new RoleplayRuntimeState
            {
                ActiveAiCharacter = "aurora",
                ActiveUserCharacter = "Zayne",
            },
        });

        Assert.Contains("Name: Aurora", context.CharacterSection);
        Assert.Contains("Aurora is a brilliant mage", context.CharacterSection);
        Assert.Contains("Zayne gave her sapphires", context.CharacterSection);
        Assert.DoesNotContain("{{char}}", context.CharacterSection);
        Assert.DoesNotContain("{{user}}", context.CharacterSection);
    }

    [Fact]
    public async Task BuildAsync_LeavesUnresolvedUserShortcode_WhenNoUserCharacter()
    {
        var runtimeService = new FakeRuntimeViewService();
        var cardStore = new FakeCharacterCardStoreWithShortcodes();
        var storyState = new StoryStateServiceWithData(new Dictionary<string, object>());
        var sessionStore = new InMemoryInteractiveSessionStore();
        var files = new FakeContentFileService();
        var plots = new FakePlotStore();

        var service = new InteractiveSessionContextService(
            runtimeService,
            sessionStore,
            cardStore,
            storyState,
            files,
            plots,
            NullLogger<InteractiveSessionContextService>.Instance);

        var context = await service.BuildAsync(new SessionState
        {
            SessionId = Guid.CreateVersion7(),
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Roleplay,
                ProjectName = "novel",
                Character = "aurora",
            },
            Roleplay = new RoleplayRuntimeState
            {
                ActiveAiCharacter = "aurora",
                ActiveUserCharacter = null,
            },
        });

        Assert.Contains("Aurora is a brilliant mage", context.CharacterSection);
        Assert.Contains("{{user}} gave her sapphires", context.CharacterSection);
        Assert.DoesNotContain("{{char}}", context.CharacterSection);
    }

    [Fact]
    public async Task BuildAsync_LeavesAllShortcodesUnresolved_WhenNoCharacterCard()
    {
        var runtimeService = new FakeRuntimeViewService();
        var cardStore = new FakeCharacterCardStoreForContext();
        var storyState = new StoryStateServiceWithData(new Dictionary<string, object>());
        var sessionStore = new InMemoryInteractiveSessionStore();
        var files = new FakeContentFileService();
        var plots = new FakePlotStore();

        var service = new InteractiveSessionContextService(
            runtimeService,
            sessionStore,
            cardStore,
            storyState,
            files,
            plots,
            NullLogger<InteractiveSessionContextService>.Instance);

        var context = await service.BuildAsync(new SessionState
        {
            SessionId = Guid.CreateVersion7(),
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Roleplay,
                ProjectName = "novel",
                Character = "hero",
            },
        });

        // FakeCharacterCardStoreForContext returns a card with no shortcodes,
        // so the existing behavior is preserved.
        Assert.Equal("Character: Sir Rowan", context.CharacterSection);
    }

    [Fact]
    public async Task BuildAsync_UsesExpandedRecentConversationWindow_AndWordBoundaryTrim()
    {
        var runtimeService = new FakeRuntimeViewService();
        var sessionStore = new InMemoryInteractiveSessionStore();
        var sessionId = Guid.CreateVersion7();
        var tree = new ConversationTree(sessionId, "Long Session", NullLogger<ConversationTree>.Instance);

        for (var i = 1; i <= 12; i++)
        {
            tree.Append(tree.ActiveLeafId, "user", new MessageContent($"Message {i:00}"));
        }

        var longMessage = string.Join(' ', Enumerable.Repeat("lanternlight", 40));
        tree.Append(tree.ActiveLeafId, "assistant", new MessageContent(longMessage));
        await sessionStore.SaveAsync(tree);

        var service = new InteractiveSessionContextService(
            runtimeService,
            sessionStore,
            new FakeCharacterCardStoreForContext(),
            new StoryStateServiceWithData(new Dictionary<string, object>()),
            new FakeContentFileService(),
            new FakePlotStore(),
            NullLogger<InteractiveSessionContextService>.Instance);

        var context = await service.BuildAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Roleplay,
                ProjectName = "novel",
                CurrentFile = "chapter1.md",
            },
        });

        Assert.DoesNotContain("Message 01", context.RecentConversationSummary);
        Assert.Contains("Message 02", context.RecentConversationSummary);
        Assert.Contains("Message 12", context.RecentConversationSummary);
        Assert.DoesNotContain("lantern...", context.RecentConversationSummary, StringComparison.Ordinal);
        Assert.Contains("lanternlight...", context.RecentConversationSummary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildAsync_IncludesUserCharacterSection_WhenUserCardSelected()
    {
        var runtimeService = new FakeRuntimeViewService();
        var cardStore = new FakeCharacterCardStoreWithUserCard();
        var storyState = new StoryStateServiceWithData(new Dictionary<string, object>());
        var sessionStore = new InMemoryInteractiveSessionStore();
        var files = new FakeContentFileService();
        var plots = new FakePlotStore();

        var service = new InteractiveSessionContextService(
            runtimeService,
            sessionStore,
            cardStore,
            storyState,
            files,
            plots,
            NullLogger<InteractiveSessionContextService>.Instance);

        var context = await service.BuildAsync(new SessionState
        {
            SessionId = Guid.CreateVersion7(),
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Roleplay,
                ProjectName = "novel",
                Character = "aurora",
            },
            Roleplay = new RoleplayRuntimeState
            {
                ActiveAiCharacter = "aurora",
                ActiveUserCharacter = "zayne",
            },
        });

        Assert.Equal("aurora", context.Character);
        Assert.Equal("zayne", context.UserCharacter);
        Assert.NotNull(context.CharacterSection);
        Assert.Contains("Aurora", context.CharacterSection);
        Assert.NotNull(context.UserCharacterSection);
        Assert.Contains("Zayne", context.UserCharacterSection);
        Assert.Contains("zayne-user-description", context.UserCharacterSection);
    }

    [Fact]
    public async Task BuildAsync_UserCharacterSection_IsNull_WhenNoUserCharacterSelected()
    {
        var runtimeService = new FakeRuntimeViewService();
        var cardStore = new FakeCharacterCardStoreWithUserCard();
        var storyState = new StoryStateServiceWithData(new Dictionary<string, object>());
        var sessionStore = new InMemoryInteractiveSessionStore();
        var files = new FakeContentFileService();
        var plots = new FakePlotStore();

        var service = new InteractiveSessionContextService(
            runtimeService,
            sessionStore,
            cardStore,
            storyState,
            files,
            plots,
            NullLogger<InteractiveSessionContextService>.Instance);

        var context = await service.BuildAsync(new SessionState
        {
            SessionId = Guid.CreateVersion7(),
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Roleplay,
                ProjectName = "novel",
                Character = "aurora",
            },
            Roleplay = new RoleplayRuntimeState
            {
                ActiveAiCharacter = "aurora",
                ActiveUserCharacter = null,
            },
        });

        Assert.Equal("aurora", context.Character);
        Assert.Null(context.UserCharacter);
        Assert.NotNull(context.CharacterSection);
        Assert.Null(context.UserCharacterSection);
    }

    [Fact]
    public async Task BuildAsync_UserCharacterSection_IsNull_WhenUserCardMissing()
    {
        var runtimeService = new FakeRuntimeViewService();
        var cardStore = new FakeCharacterCardStoreWithUserCard();
        var storyState = new StoryStateServiceWithData(new Dictionary<string, object>());
        var sessionStore = new InMemoryInteractiveSessionStore();
        var files = new FakeContentFileService();
        var plots = new FakePlotStore();

        var service = new InteractiveSessionContextService(
            runtimeService,
            sessionStore,
            cardStore,
            storyState,
            files,
            plots,
            NullLogger<InteractiveSessionContextService>.Instance);

        var context = await service.BuildAsync(new SessionState
        {
            SessionId = Guid.CreateVersion7(),
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Roleplay,
                ProjectName = "novel",
                Character = "aurora",
            },
            Roleplay = new RoleplayRuntimeState
            {
                ActiveAiCharacter = "aurora",
                ActiveUserCharacter = "unknown-user",
            },
        });

        Assert.Equal("aurora", context.Character);
        Assert.Equal("unknown-user", context.UserCharacter);
        Assert.NotNull(context.CharacterSection);
        Assert.Null(context.UserCharacterSection);
    }
}

internal sealed class InMemoryInteractiveSessionStore : ISessionStore
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

internal sealed class FakeRuntimeViewService : ISessionStateService
{
    public SessionState State { get; set; } = new();

    public Task<SessionState> LoadViewAsync(Guid? sessionId, CancellationToken ct = default)
    {
        State.SessionId = sessionId;
        return Task.FromResult(State);
    }

    public Task<SessionMutationResult<SessionState>> SetProfileAsync(Guid? sessionId, SetSessionProfileCommand command, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<SessionMutationResult<SessionState>> SetRoleplayAsync(Guid? sessionId, SetSessionRoleplayCommand command, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<SessionMutationResult<SessionState>> SetModeAsync(Guid? sessionId, SetSessionModeCommand command, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<SessionMutationResult<WriterPendingCaptureEvent>> CaptureWriterPendingAsync(Guid? sessionId, CaptureWriterPendingCommand command, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<SessionMutationResult<WriterPendingContentAcceptedEvent>> AcceptWriterPendingAsync(Guid? sessionId, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<SessionMutationResult<WriterPendingContentRejectedEvent>> RejectWriterPendingAsync(Guid? sessionId, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<SessionMutationResult<SessionState>> UpdateNarrativeStateAsync(Guid? sessionId, UpdateNarrativeStateCommand command, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<SessionMutationResult<SessionState>> SetActivePlotAsync(Guid? sessionId, SetActivePlotCommand command, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<SessionMutationResult<SessionState>> ClearActivePlotAsync(Guid? sessionId, CancellationToken ct = default)
        => throw new NotSupportedException();
}

internal sealed class FakeCharacterCardStoreForContext : ICharacterCardStore
{
    public Task<CharacterCard?> LoadAsync(string fileName, CancellationToken ct = default)
        => Task.FromResult<CharacterCard?>(new CharacterCard { Name = "Sir Rowan" });

    public Task SaveAsync(string fileName, CharacterCard card, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<bool> DeleteAsync(string fileName, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<IReadOnlyList<CharacterCard>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CharacterCard>>([]);

    public string CardToPrompt(CharacterCard card) => $"Character: {card.Name}";

    public CharacterCard NewTemplate(string name = "New Character")
        => new() { Name = name };

    public Task<CharacterCard> ImportTavernCardAsync(string pngPath, CancellationToken ct = default)
        => Task.FromResult(new CharacterCard { Name = "Imported" });
}

internal sealed class FakeCharacterCardStoreWithShortcodes : ICharacterCardStore
{
    public Task<CharacterCard?> LoadAsync(string fileName, CancellationToken ct = default)
    {
        if (fileName == "aurora")
        {
            return Task.FromResult<CharacterCard?>(new CharacterCard
            {
                Name = "Aurora",
                Description = "{{char}} is a brilliant mage.",
                Personality = "{{user}} gave her sapphires.",
            });
        }

        return Task.FromResult<CharacterCard?>(null);
    }

    public Task SaveAsync(string fileName, CharacterCard card, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<bool> DeleteAsync(string fileName, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<IReadOnlyList<CharacterCard>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CharacterCard>>([]);

    public string CardToPrompt(CharacterCard card)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Name: {card.Name}");
        if (!string.IsNullOrWhiteSpace(card.Description))
            sb.AppendLine($"Description: {card.Description}");
        if (!string.IsNullOrWhiteSpace(card.Personality))
            sb.AppendLine($"Personality: {card.Personality}");
        return sb.ToString().TrimEnd();
    }

    public CharacterCard NewTemplate(string name = "New Character")
        => new() { Name = name };

    public Task<CharacterCard> ImportTavernCardAsync(string pngPath, CancellationToken ct = default)
        => Task.FromResult(new CharacterCard { Name = "Imported" });
}

internal sealed class FakeCharacterCardStoreWithUserCard : ICharacterCardStore
{
    public Task<CharacterCard?> LoadAsync(string fileName, CancellationToken ct = default)
    {
        if (fileName == "aurora")
        {
            return Task.FromResult<CharacterCard?>(new CharacterCard
            {
                Name = "Aurora",
                Description = "{{char}} is a brilliant mage.",
                Personality = "{{user}} gave her sapphires.",
            });
        }

        if (fileName == "zayne")
        {
            return Task.FromResult<CharacterCard?>(new CharacterCard
            {
                Name = "Zayne",
                Description = "zayne-user-description",
                Personality = "{{char}} is a rogue.",
            });
        }

        return Task.FromResult<CharacterCard?>(null);
    }

    public Task SaveAsync(string fileName, CharacterCard card, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<bool> DeleteAsync(string fileName, CancellationToken ct = default)
        => Task.FromResult(false);

    public Task<IReadOnlyList<CharacterCard>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CharacterCard>>([]);

    public string CardToPrompt(CharacterCard card)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Name: {card.Name}");
        if (!string.IsNullOrWhiteSpace(card.Description))
            sb.AppendLine($"Description: {card.Description}");
        if (!string.IsNullOrWhiteSpace(card.Personality))
            sb.AppendLine($"Personality: {card.Personality}");
        return sb.ToString().TrimEnd();
    }

    public CharacterCard NewTemplate(string name = "New Character")
        => new() { Name = name };

    public Task<CharacterCard> ImportTavernCardAsync(string pngPath, CancellationToken ct = default)
        => Task.FromResult(new CharacterCard { Name = "Imported" });
}

internal sealed class StoryStateServiceWithData : IStoryStateService
{
    private readonly IReadOnlyDictionary<string, object> _data;

    public StoryStateServiceWithData(IReadOnlyDictionary<string, object> data)
    {
        _data = data;
    }

    public Task<IReadOnlyDictionary<string, object>> LoadAsync(string stateFilePath, CancellationToken ct = default)
        => Task.FromResult(_data);

    public Task SaveAsync(string stateFilePath, IReadOnlyDictionary<string, object> state, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyDictionary<string, object>> MergeAsync(string stateFilePath, IReadOnlyDictionary<string, object> updates, CancellationToken ct = default)
        => Task.FromResult(updates);

    public Task IncrementCounterAsync(string stateFilePath, string counterKey, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RemoveKeyAsync(string stateFilePath, string key, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class FakePlotStore : IPlotStore
{
    private readonly Dictionary<string, string> _plots = [];

    public void Set(string name, string content)
    {
        _plots[name] = content;
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<string>>(_plots.Keys.OrderBy(k => k).ToList());

    public Task<string> LoadAsync(string plotName, CancellationToken ct = default)
        => Task.FromResult(_plots.TryGetValue(plotName, out var content) ? content : "");

    public Task SaveAsync(string plotName, string content, CancellationToken ct = default)
    {
        _plots[plotName] = content;
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string plotName, CancellationToken ct = default)
        => Task.FromResult(_plots.ContainsKey(plotName));
}
