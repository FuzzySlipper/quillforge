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
        var runtimeStore = new FakeSessionRuntimeStore(new Dictionary<Guid, string>
        {
            [SessionA] = "alpha",
        });
        var handler = new GetStoryStateHandler(storyState, runtimeStore, NullLogger<GetStoryStateHandler>.Instance);
        var context = new AgentContext { SessionId = SessionA, ActiveMode = "writer" };

        await handler.HandleAsync(JsonDocument.Parse("{}").RootElement, context);

        Assert.Single(storyState.LoadedPaths);
        Assert.Equal("alpha/.state.yaml", storyState.LoadedPaths[0]);
    }

    [Fact]
    public async Task GetStoryState_DifferentSession_UsesDifferentProject()
    {
        var storyState = new TrackingStoryStateService();
        var runtimeStore = new FakeSessionRuntimeStore(new Dictionary<Guid, string>
        {
            [SessionA] = "alpha",
            [SessionB] = "beta",
        });
        var handler = new GetStoryStateHandler(storyState, runtimeStore, NullLogger<GetStoryStateHandler>.Instance);

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
        var runtimeStore = new FakeSessionRuntimeStore(new Dictionary<Guid, string>
        {
            [SessionA] = "alpha",
        });
        var handler = new UpdateStoryStateHandler(storyState, runtimeStore, NullLogger<UpdateStoryStateHandler>.Instance);
        var context = new AgentContext { SessionId = SessionA, ActiveMode = "roleplay" };
        var input = JsonDocument.Parse("""{"updates": {"tension": "high"}}""").RootElement;

        await handler.HandleAsync(input, context);

        Assert.Single(storyState.MergedPaths);
        Assert.Equal("alpha/.state.yaml", storyState.MergedPaths[0]);
    }

    [Fact]
    public async Task GetStoryState_NullProject_DefaultsToDefault()
    {
        var storyState = new TrackingStoryStateService();
        var runtimeStore = new FakeSessionRuntimeStore(new Dictionary<Guid, string>());
        var handler = new GetStoryStateHandler(storyState, runtimeStore, NullLogger<GetStoryStateHandler>.Instance);
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

internal sealed class FakeSessionRuntimeStore : ISessionRuntimeStore
{
    private readonly Dictionary<Guid, string> _projectsBySession;

    public FakeSessionRuntimeStore(Dictionary<Guid, string> projectsBySession)
    {
        _projectsBySession = projectsBySession;
    }

    public Task<SessionRuntimeState> LoadAsync(Guid? sessionId, CancellationToken ct = default)
    {
        string? projectName = null;
        if (sessionId.HasValue && _projectsBySession.TryGetValue(sessionId.Value, out var name))
        {
            projectName = name;
        }

        return Task.FromResult(new SessionRuntimeState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState { ActiveModeName = "writer", ProjectName = projectName },
        });
    }

    public Task SaveAsync(SessionRuntimeState state, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
        => Task.CompletedTask;
}
