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
        var files = new FakeContentFileService();
        files.SeedFile("story/novel/chapter1.md", new string('a', 520));
        var plots = new FakePlotStore();
        plots.Set("gate-arc", "# Gate Arc\n\n- Beat one");

        var service = new InteractiveSessionContextService(
            runtimeService,
            cardStore,
            storyState,
            files,
            plots,
            NullLogger<InteractiveSessionContextService>.Instance);

        var context = await service.BuildAsync(new SessionRuntimeState
        {
            SessionId = Guid.CreateVersion7(),
            Mode = new ModeSelectionState
            {
                ActiveModeName = "roleplay",
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
                ActivePlotFile = "gate-arc",
                PlotProgress = new PlotProgressState
                {
                    CurrentBeat = "gate-confrontation",
                    CompletedBeats = ["arrival"],
                    Deviations = ["The captain recognized the smuggler's crest."],
                },
            },
        });

        Assert.Equal("roleplay", context.ActiveModeName);
        Assert.Equal("novel", context.ProjectName);
        Assert.Equal("novel/.state.yaml", context.StoryStatePath);
        Assert.Equal("Character: Sir Rowan", context.CharacterSection);
        Assert.Contains("tension", context.StoryStateSummary);
        Assert.NotNull(context.FileContext);
        Assert.StartsWith("...\n", context.FileContext, StringComparison.Ordinal);
        Assert.Equal("Pending scene text", context.WriterPendingContent);
        Assert.Equal("gate-arc", context.ActivePlotFile);
        Assert.Contains("Beat one", context.ActivePlotContent);
        Assert.Contains("Current beat: gate-confrontation", context.PlotProgressSummary);
    }

    [Fact]
    public async Task LoadAsync_UsesRuntimeServiceState()
    {
        var runtimeService = new FakeRuntimeViewService
        {
            State = new SessionRuntimeState
            {
                SessionId = Guid.CreateVersion7(),
                Mode = new ModeSelectionState
                {
                    ActiveModeName = "writer",
                    ProjectName = "novel",
                },
            },
        };

        var service = new InteractiveSessionContextService(
            runtimeService,
            new FakeCharacterCardStoreForContext(),
            new StoryStateServiceWithData(new Dictionary<string, object>()),
            new FakeContentFileService(),
            new FakePlotStore(),
            NullLogger<InteractiveSessionContextService>.Instance);

        var context = await service.LoadAsync(runtimeService.State.SessionId);

        Assert.Equal("writer", context.ActiveModeName);
        Assert.Equal("novel", context.ProjectName);
        Assert.Equal("novel/.state.yaml", context.StoryStatePath);
    }
}

internal sealed class FakeRuntimeViewService : ISessionRuntimeService
{
    public SessionRuntimeState State { get; set; } = new();

    public Task<SessionRuntimeState> LoadViewAsync(Guid? sessionId, CancellationToken ct = default)
    {
        State.SessionId = sessionId;
        return Task.FromResult(State);
    }

    public Task<SessionMutationResult<SessionRuntimeState>> SetProfileAsync(Guid? sessionId, SetSessionProfileCommand command, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<SessionMutationResult<SessionRuntimeState>> SetModeAsync(Guid? sessionId, SetSessionModeCommand command, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<SessionMutationResult<SessionRuntimeState>> CaptureWriterPendingAsync(Guid? sessionId, CaptureWriterPendingCommand command, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<SessionMutationResult<WriterPendingDecisionResult>> AcceptWriterPendingAsync(Guid? sessionId, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<SessionMutationResult<SessionRuntimeState>> RejectWriterPendingAsync(Guid? sessionId, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<SessionMutationResult<SessionRuntimeState>> UpdateNarrativeStateAsync(Guid? sessionId, UpdateNarrativeStateCommand command, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<SessionMutationResult<SessionRuntimeState>> SetActivePlotAsync(Guid? sessionId, SetActivePlotCommand command, CancellationToken ct = default)
        => throw new NotSupportedException();

    public Task<SessionMutationResult<SessionRuntimeState>> ClearActivePlotAsync(Guid? sessionId, CancellationToken ct = default)
        => throw new NotSupportedException();
}

internal sealed class FakeCharacterCardStoreForContext : ICharacterCardStore
{
    public Task<CharacterCard?> LoadAsync(string fileName, CancellationToken ct = default)
        => Task.FromResult<CharacterCard?>(new CharacterCard { Name = "Sir Rowan" });

    public Task SaveAsync(string fileName, CharacterCard card, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<CharacterCard>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CharacterCard>>([]);

    public string CardToPrompt(CharacterCard card) => $"Character: {card.Name}";

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
