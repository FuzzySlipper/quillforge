using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents.Tools;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public sealed class StoryStateHandlerTests
{
    private static readonly Guid SessionA = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid SessionB = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Fact]
    public async Task GetStoryState_UsesSessionProject()
    {
        var storyState = new TrackingStoryStateService();
        var sessionContextService = new FakeInteractiveSessionContextService(new Dictionary<Guid, string>
        {
            [SessionA] = "alpha",
        });
        var handler = new GetStoryStateHandler(storyState, sessionContextService, NullLogger<GetStoryStateHandler>.Instance);
        var context = new AgentContext { SessionId = SessionA, ActiveMode = "writer" };

        await handler.HandleAsync(JsonDocument.Parse("{}").RootElement, context);

        Assert.Single(storyState.LoadedPaths);
        Assert.Equal("alpha/.state.yaml", storyState.LoadedPaths[0]);
    }

    [Fact]
    public async Task GetStoryState_DifferentSession_UsesDifferentProject()
    {
        var storyState = new TrackingStoryStateService();
        var sessionContextService = new FakeInteractiveSessionContextService(new Dictionary<Guid, string>
        {
            [SessionA] = "alpha",
            [SessionB] = "beta",
        });
        var handler = new GetStoryStateHandler(storyState, sessionContextService, NullLogger<GetStoryStateHandler>.Instance);

        await handler.HandleAsync(JsonDocument.Parse("{}").RootElement,
            new AgentContext { SessionId = SessionA, ActiveMode = "writer" });
        await handler.HandleAsync(JsonDocument.Parse("{}").RootElement,
            new AgentContext { SessionId = SessionB, ActiveMode = "writer" });

        Assert.Equal(2, storyState.LoadedPaths.Count);
        Assert.Equal("alpha/.state.yaml", storyState.LoadedPaths[0]);
        Assert.Equal("beta/.state.yaml", storyState.LoadedPaths[1]);
    }

    [Fact]
    public async Task UpdateStoryState_UsesSessionProject()
    {
        var storyState = new TrackingStoryStateService();
        var sessionContextService = new FakeInteractiveSessionContextService(new Dictionary<Guid, string>
        {
            [SessionA] = "alpha",
        });
        var handler = new UpdateStoryStateHandler(storyState, sessionContextService, NullLogger<UpdateStoryStateHandler>.Instance);
        var context = new AgentContext { SessionId = SessionA, ActiveMode = "roleplay" };
        var input = JsonDocument.Parse("""{"updates": {"tension": "high"}}""").RootElement;

        await handler.HandleAsync(input, context);

        Assert.Single(storyState.MergedPaths);
        Assert.Equal("alpha/.state.yaml", storyState.MergedPaths[0]);
    }

    [Fact]
    public async Task WriteProseHandler_ResolvesStoryContextFromSession()
    {
        // WriteProseHandler should load story state from the session's project path.
        // We can't easily test the full prose generation (ProseWriterAgent has heavy deps),
        // but we can verify the story state is loaded from the correct session-scoped path.
        var storyState = new TrackingStoryStateService();
        var sessionContextService = new FakeInteractiveSessionContextService(new Dictionary<Guid, string>
        {
            [SessionA] = "my-novel",
        });
        // ProseWriterAgent is null — handler will fail at WriteAsync, but we verify story context resolution first
        var handler = new WriteProseHandler(null!, sessionContextService, storyState, NullLogger<WriteProseHandler>.Instance);
        var context = new AgentContext { SessionId = SessionA, ActiveMode = "writer" };
        var input = JsonDocument.Parse("""{"scene_description": "test scene"}""").RootElement;

        // The handler will throw NullReferenceException when calling _proseWriter.WriteAsync,
        // but story state resolution happens before that call
        await Assert.ThrowsAsync<NullReferenceException>(() => handler.HandleAsync(input, context));

        Assert.Single(storyState.LoadedPaths);
        Assert.Equal("my-novel/.state.yaml", storyState.LoadedPaths[0]);
    }

    [Fact]
    public async Task GetStoryState_NullProject_DefaultsToDefault()
    {
        var storyState = new TrackingStoryStateService();
        var sessionContextService = new FakeInteractiveSessionContextService(new Dictionary<Guid, string>());
        var handler = new GetStoryStateHandler(storyState, sessionContextService, NullLogger<GetStoryStateHandler>.Instance);
        var context = new AgentContext { SessionId = SessionA, ActiveMode = "writer" };

        await handler.HandleAsync(JsonDocument.Parse("{}").RootElement, context);

        Assert.Equal("default/.state.yaml", storyState.LoadedPaths[0]);
    }
}

internal sealed class TrackingStoryStateService : IStoryStateService
{
    public List<string> LoadedPaths { get; } = [];
    public List<string> MergedPaths { get; } = [];

    public Task<IReadOnlyDictionary<string, object>> LoadAsync(string stateFilePath, CancellationToken ct = default)
    {
        LoadedPaths.Add(stateFilePath);
        return Task.FromResult<IReadOnlyDictionary<string, object>>(new Dictionary<string, object>());
    }

    public Task SaveAsync(string stateFilePath, IReadOnlyDictionary<string, object> state, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyDictionary<string, object>> MergeAsync(string stateFilePath, IReadOnlyDictionary<string, object> updates, CancellationToken ct = default)
    {
        MergedPaths.Add(stateFilePath);
        return Task.FromResult(updates);
    }

    public Task IncrementCounterAsync(string stateFilePath, string counterKey, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RemoveKeyAsync(string stateFilePath, string key, CancellationToken ct = default)
        => Task.CompletedTask;
}

internal sealed class FakeInteractiveSessionContextService : IInteractiveSessionContextService
{
    private readonly Dictionary<Guid, string> _projectsBySession;

    public FakeInteractiveSessionContextService(Dictionary<Guid, string>? projectsBySession = null)
    {
        _projectsBySession = projectsBySession ?? [];
    }

    public Task<InteractiveSessionContext> BuildAsync(SessionRuntimeState state, CancellationToken ct = default)
    {
        var projectName = state.Mode.ProjectName ?? "default";
        return Task.FromResult(new InteractiveSessionContext
        {
            ActiveModeName = state.Mode.ActiveModeName,
            ProjectName = projectName,
            CurrentFile = state.Mode.CurrentFile,
            Character = state.Mode.Character,
            StoryStatePath = $"{projectName}/.state.yaml",
            WriterPendingContent = state.Writer.PendingContent,
        });
    }

    public Task<InteractiveSessionContext> LoadAsync(Guid? sessionId, CancellationToken ct = default)
    {
        string? projectName = null;
        if (sessionId.HasValue && _projectsBySession.TryGetValue(sessionId.Value, out var name))
        {
            projectName = name;
        }

        return BuildAsync(new SessionRuntimeState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState { ActiveModeName = "writer", ProjectName = projectName },
        }, ct);
    }
}
