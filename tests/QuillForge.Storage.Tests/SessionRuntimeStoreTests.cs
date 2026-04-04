using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core;
using QuillForge.Core.Models;
using QuillForge.Storage.FileSystem;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.Tests;

public class SessionRuntimeStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemSessionRuntimeStore _store;

    public SessionRuntimeStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"quillforge-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        var writer = new AtomicFileWriter(NullLoggerFactory.Instance.CreateLogger<AtomicFileWriter>());
        _store = new FileSystemSessionRuntimeStore(
            _tempDir, writer, NullLoggerFactory.Instance.CreateLogger<FileSystemSessionRuntimeStore>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Load_NoExistingState_ReturnsDefaults()
    {
        var state = await _store.LoadAsync(Guid.NewGuid());

        Assert.Equal("general", state.Mode.ActiveModeName);
        Assert.Null(state.Mode.ProjectName);
        Assert.Equal(WriterState.Idle, state.Writer.State);
        Assert.Null(state.Profile.ProfileId);
        Assert.Null(state.Profile.ActiveConductor);
        Assert.False(state.Roleplay.HasExplicitAiCharacterSelection);
        Assert.Null(state.Roleplay.ActiveAiCharacter);
    }

    [Fact]
    public async Task Load_NullSessionId_ReturnsTransientDefaults()
    {
        var state = await _store.LoadAsync(null);

        Assert.Null(state.SessionId);
        Assert.Equal("general", state.Mode.ActiveModeName);
        Assert.Null(state.Profile.ProfileId);
    }

    [Fact]
    public async Task Load_MissingSessionState_DoesNotInheritLegacyDefaultJson()
    {
        var legacyDefaultPath = Path.Combine(_tempDir, ContentPaths.DataSessionState, "default.json");
        await File.WriteAllTextAsync(
            legacyDefaultPath,
            """
            {
              "mode": {
                "activeModeName": "writer",
                "projectName": "legacy-project"
              }
            }
            """);

        var loaded = await _store.LoadAsync(Guid.NewGuid());

        Assert.Equal("general", loaded.Mode.ActiveModeName);
        Assert.Null(loaded.Mode.ProjectName);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsState()
    {
        var sessionId = Guid.NewGuid();
        var state = new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveModeName = "writer",
                ProjectName = "test-project",
                CurrentFile = "chapter1.md",
                Character = "hero",
            },
            Profile = new ProfileState
            {
                ProfileId = "grim",
                ActiveConductor = "narrator",
                ActiveLoreSet = "fantasy",
                ActiveNarrativeRules = "default",
                ActiveWritingStyle = "literary",
            },
            Roleplay = new RoleplayRuntimeState
            {
                HasExplicitAiCharacterSelection = true,
                ActiveAiCharacter = "guide",
                HasExplicitUserCharacterSelection = true,
                ActiveUserCharacter = "author",
            },
            Writer = new WriterRuntimeState
            {
                PendingContent = "Once upon a time...",
                State = WriterState.PendingReview,
            },
            Narrative = new NarrativeRuntimeState
            {
                DirectorNotes = "The hero has entered the ruins.",
                ActivePlotFile = "ruins-arc.md",
                PlotProgress = new PlotProgressState
                {
                    CurrentBeat = "ruins-entry",
                    CompletedBeats = ["call-to-adventure"],
                    Deviations = ["The rival arrived early."],
                },
            },
        };

        await _store.SaveAsync(state);
        var loaded = await _store.LoadAsync(sessionId);

        Assert.Equal(sessionId, loaded.SessionId);
        Assert.Equal("writer", loaded.Mode.ActiveModeName);
        Assert.Equal("test-project", loaded.Mode.ProjectName);
        Assert.Equal("chapter1.md", loaded.Mode.CurrentFile);
        Assert.Equal("hero", loaded.Mode.Character);
        Assert.Equal("grim", loaded.Profile.ProfileId);
        Assert.Equal("narrator", loaded.Profile.ActiveConductor);
        Assert.Equal("fantasy", loaded.Profile.ActiveLoreSet);
        Assert.Equal("default", loaded.Profile.ActiveNarrativeRules);
        Assert.Equal("literary", loaded.Profile.ActiveWritingStyle);
        Assert.True(loaded.Roleplay.HasExplicitAiCharacterSelection);
        Assert.Equal("guide", loaded.Roleplay.ActiveAiCharacter);
        Assert.True(loaded.Roleplay.HasExplicitUserCharacterSelection);
        Assert.Equal("author", loaded.Roleplay.ActiveUserCharacter);
        Assert.Equal("Once upon a time...", loaded.Writer.PendingContent);
        Assert.Equal(WriterState.PendingReview, loaded.Writer.State);
        Assert.Equal("The hero has entered the ruins.", loaded.Narrative.DirectorNotes);
        Assert.Equal("ruins-arc.md", loaded.Narrative.ActivePlotFile);
        Assert.Equal("ruins-entry", loaded.Narrative.PlotProgress.CurrentBeat);
        Assert.Contains("call-to-adventure", loaded.Narrative.PlotProgress.CompletedBeats);
        Assert.Contains("The rival arrived early.", loaded.Narrative.PlotProgress.Deviations);
    }

    [Fact]
    public async Task Save_NullSessionId_Throws()
    {
        var state = new SessionState();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _store.SaveAsync(state));
    }

    [Fact]
    public async Task TwoSessions_IndependentState()
    {
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        await _store.SaveAsync(new SessionState
        {
            SessionId = id1,
            Mode = new ModeSelectionState { ActiveModeName = "writer", ProjectName = "novel-a" },
        });
        await _store.SaveAsync(new SessionState
        {
            SessionId = id2,
            Mode = new ModeSelectionState { ActiveModeName = "roleplay", Character = "villain" },
        });

        var state1 = await _store.LoadAsync(id1);
        var state2 = await _store.LoadAsync(id2);

        Assert.Equal("writer", state1.Mode.ActiveModeName);
        Assert.Equal("novel-a", state1.Mode.ProjectName);
        Assert.Equal("roleplay", state2.Mode.ActiveModeName);
        Assert.Equal("villain", state2.Mode.Character);
    }

    [Fact]
    public async Task Delete_RemovesPersistedState()
    {
        var sessionId = Guid.NewGuid();
        await _store.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState { ActiveModeName = "writer" },
        });

        await _store.DeleteAsync(sessionId);
        var loaded = await _store.LoadAsync(sessionId);

        // Should get fresh defaults after delete
        Assert.Equal("general", loaded.Mode.ActiveModeName);
    }

    [Fact]
    public async Task Save_UpdatesLastModified()
    {
        var sessionId = Guid.NewGuid();
        var state = new SessionState { SessionId = sessionId };
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        await _store.SaveAsync(state);
        var loaded = await _store.LoadAsync(sessionId);

        Assert.True(loaded.LastModified > before);
    }

    [Fact]
    public async Task FindSessionIdsByProfileIdAsync_ReturnsMatchingPersistedSessions()
    {
        var firstSessionId = Guid.NewGuid();
        var secondSessionId = Guid.NewGuid();

        await _store.SaveAsync(new SessionState
        {
            SessionId = firstSessionId,
            Profile = new ProfileState
            {
                ProfileId = "grim",
            },
        });
        await _store.SaveAsync(new SessionState
        {
            SessionId = secondSessionId,
            Profile = new ProfileState
            {
                ProfileId = "default",
            },
        });

        var matches = await _store.FindSessionIdsByProfileIdAsync("grim");

        Assert.Equal([firstSessionId], matches);
    }
}
