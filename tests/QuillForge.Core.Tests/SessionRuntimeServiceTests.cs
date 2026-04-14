using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public sealed class SessionRuntimeServiceTests
{
    private static readonly IMode[] Modes =
    [
        new GuideMode(),
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
        Assert.Equal(Mode.Writer, result.Value.Mode.ActiveMode);
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
        await store.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState { ActiveMode = Mode.Writer, ProjectName = "novel" },
            Writer = new WriterRuntimeState
            {
                PendingContent = "Pending chapter text",
                PendingProjectName = "novel",
                PendingFileName = "chapter1.md",
                State = WriterState.PendingReview,
            },
        });

        var service = CreateService(store);
        var result = await service.SetModeAsync(
            sessionId,
            new SetSessionModeCommand("general", null, null, null));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal(Mode.Guide, result.Value.Mode.ActiveMode);
        Assert.Equal(WriterState.Idle, result.Value.Writer.State);
        Assert.Null(result.Value.Writer.PendingContent);
        Assert.Null(result.Value.Writer.PendingProjectName);
        Assert.Null(result.Value.Writer.PendingFileName);
    }

    [Fact]
    public async Task SetModeAsync_RoleplayWithoutExplicitTarget_DefaultsProjectAndFile()
    {
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);
        var sessionId = Guid.CreateVersion7();

        var result = await service.SetModeAsync(
            sessionId,
            new SetSessionModeCommand("roleplay", null, null, null));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal(Mode.Roleplay, result.Value.Mode.ActiveMode);
        Assert.Equal($"roleplay-{sessionId:N}"[..21], result.Value.Mode.ProjectName);
        Assert.Equal("scene-01.md", result.Value.Mode.CurrentFile);
        Assert.Equal("default-guide", result.Value.Mode.Character);

        var saved = await store.LoadAsync(sessionId);
        Assert.Equal(Mode.Roleplay, saved.Mode.ActiveMode);
        Assert.Equal($"roleplay-{sessionId:N}"[..21], saved.Mode.ProjectName);
        Assert.Equal("scene-01.md", saved.Mode.CurrentFile);
    }

    [Fact]
    public async Task SetModeAsync_RoleplayWithoutNewTarget_PreservesExistingProjectAndFile()
    {
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);
        var sessionId = Guid.CreateVersion7();

        await service.SetModeAsync(
            sessionId,
            new SetSessionModeCommand("roleplay", "campaign-alpha", "scene-07.md", "Archivist"));

        var result = await service.SetModeAsync(
            sessionId,
            new SetSessionModeCommand("roleplay", null, null, null));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal(Mode.Roleplay, result.Value.Mode.ActiveMode);
        Assert.Equal("campaign-alpha", result.Value.Mode.ProjectName);
        Assert.Equal("scene-07.md", result.Value.Mode.CurrentFile);
        Assert.Equal("Archivist", result.Value.Mode.Character);

        var saved = await store.LoadAsync(sessionId);
        Assert.Equal("campaign-alpha", saved.Mode.ProjectName);
        Assert.Equal("scene-07.md", saved.Mode.CurrentFile);
        Assert.Equal("Archivist", saved.Mode.Character);
    }

    [Fact]
    public async Task CaptureWriterPendingAsync_CapturesLongWriterResponse()
    {
        var store = new InMemorySessionRuntimeStore();
        var sessionId = Guid.CreateVersion7();
        await store.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Writer,
                ProjectName = "novel",
                CurrentFile = "chapter1.md",
            },
        });

        var service = CreateService(store);
        var result = await service.CaptureWriterPendingAsync(
            sessionId,
            new CaptureWriterPendingCommand(new string('x', 300), Mode.Writer));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        var captured = Assert.IsType<WriterPendingContentCapturedEvent>(result.Value);
        Assert.Equal(WriterState.PendingReview, captured.SessionView.Writer.State);
        Assert.NotNull(captured.SessionView.Writer.PendingContent);
        Assert.Equal("novel", captured.SessionView.Writer.PendingProjectName);
        Assert.Equal("chapter1.md", captured.SessionView.Writer.PendingFileName);
        Assert.Equal("default", captured.SessionView.Profile.ProfileId);
        Assert.Equal(Mode.Writer, captured.SourceMode);
    }

    [Fact]
    public async Task CaptureWriterPendingAsync_ReplacesExistingPendingReviewContent()
    {
        var store = new InMemorySessionRuntimeStore();
        var sessionId = Guid.CreateVersion7();
        var initialDraft = new string('a', 300);
        var revisedDraft = new string('b', 320);
        await store.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Writer,
                ProjectName = "novel",
                CurrentFile = "chapter1.md",
            },
            Writer = new WriterRuntimeState
            {
                PendingContent = initialDraft,
                PendingProjectName = "old-project",
                PendingFileName = "old-file.md",
                State = WriterState.PendingReview,
            },
        });

        var service = CreateService(store);
        var result = await service.CaptureWriterPendingAsync(
            sessionId,
            new CaptureWriterPendingCommand(revisedDraft, Mode.Writer));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        var captured = Assert.IsType<WriterPendingContentCapturedEvent>(result.Value);
        Assert.Equal(revisedDraft, captured.SessionView.Writer.PendingContent);
        Assert.Equal("novel", captured.SessionView.Writer.PendingProjectName);
        Assert.Equal("chapter1.md", captured.SessionView.Writer.PendingFileName);
        Assert.Equal(WriterState.PendingReview, captured.SessionView.Writer.State);

        var saved = await store.LoadAsync(sessionId);
        Assert.Equal(revisedDraft, saved.Writer.PendingContent);
        Assert.Equal("novel", saved.Writer.PendingProjectName);
        Assert.Equal("chapter1.md", saved.Writer.PendingFileName);
        Assert.Equal(WriterState.PendingReview, saved.Writer.State);
    }

    [Fact]
    public async Task CaptureWriterPendingAsync_SkipsOutsideWriterMode()
    {
        var store = new InMemorySessionRuntimeStore();
        var sessionId = Guid.CreateVersion7();
        await store.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState { ActiveMode = Mode.Guide },
        });

        var service = CreateService(store);
        var result = await service.CaptureWriterPendingAsync(
            sessionId,
            new CaptureWriterPendingCommand(new string('x', 300), Mode.Writer));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        var skipped = Assert.IsType<WriterPendingCaptureSkippedEvent>(result.Value);
        Assert.Equal("mode_mismatch", skipped.ReasonCode);
        Assert.Equal(WriterState.Idle, skipped.SessionView.Writer.State);
        Assert.Null(skipped.SessionView.Writer.PendingContent);
    }

    [Fact]
    public async Task AcceptWriterPendingAsync_ReturnsContent_AndResetsWriterState()
    {
        var store = new InMemorySessionRuntimeStore();
        var storyStore = new InMemoryStoryStore();
        var sessionId = Guid.CreateVersion7();
        await store.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Writer,
                ProjectName = "novel",
                CurrentFile = "chapter1.md",
            },
            Writer = new WriterRuntimeState
            {
                PendingContent = "Accepted text",
                PendingProjectName = "novel",
                PendingFileName = "chapter1.md",
                State = WriterState.PendingReview,
            },
        });

        var service = CreateService(store, storyStore: storyStore);
        var result = await service.AcceptWriterPendingAsync(sessionId);

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal("Accepted text", result.Value.AcceptedContent);
        Assert.Equal(sessionId, result.Value.SessionId);
        Assert.Equal("story/novel/chapter1.md", result.Value.SavedPath);

        var saved = await store.LoadAsync(sessionId);
        Assert.Equal(WriterState.Idle, saved.Writer.State);
        Assert.Null(saved.Writer.PendingContent);
        Assert.Null(saved.Writer.PendingProjectName);
        Assert.Null(saved.Writer.PendingFileName);
        Assert.Equal("Accepted text", await storyStore.ReadAsync("novel", "chapter1.md"));
    }

    [Fact]
    public async Task AcceptWriterPendingAsync_LegacyPendingWithoutCapturedTarget_UsesCurrentWriterTarget()
    {
        var store = new InMemorySessionRuntimeStore();
        var storyStore = new InMemoryStoryStore();
        var sessionId = Guid.CreateVersion7();
        await store.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Writer,
                ProjectName = "novel",
                CurrentFile = "chapter1.md",
            },
            Writer = new WriterRuntimeState
            {
                PendingContent = "Accepted text",
                State = WriterState.PendingReview,
            },
        });

        var service = CreateService(store, storyStore: storyStore);
        var result = await service.AcceptWriterPendingAsync(sessionId);

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.Equal("story/novel/chapter1.md", result.Value!.SavedPath);
        Assert.Equal("Accepted text", await storyStore.ReadAsync("novel", "chapter1.md"));

        var saved = await store.LoadAsync(sessionId);
        Assert.Equal(WriterState.Idle, saved.Writer.State);
        Assert.Null(saved.Writer.PendingContent);
        Assert.Null(saved.Writer.PendingProjectName);
        Assert.Null(saved.Writer.PendingFileName);
    }

    [Fact]
    public async Task AcceptWriterPendingAsync_AfterRevision_WritesRevisedContent()
    {
        var store = new InMemorySessionRuntimeStore();
        var storyStore = new InMemoryStoryStore();
        var service = CreateService(store, storyStore: storyStore);
        var sessionId = Guid.CreateVersion7();
        var initialDraft = new string('a', 300);
        var revisedDraft = new string('b', 320);

        await service.SetModeAsync(
            sessionId,
            new SetSessionModeCommand("writer", "novel", "chapter1.md", null));
        await service.CaptureWriterPendingAsync(
            sessionId,
            new CaptureWriterPendingCommand(initialDraft, Mode.Writer));
        await service.CaptureWriterPendingAsync(
            sessionId,
            new CaptureWriterPendingCommand(revisedDraft, Mode.Writer));

        var result = await service.AcceptWriterPendingAsync(sessionId);

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.Equal(revisedDraft, result.Value!.AcceptedContent);
        Assert.Equal("story/novel/chapter1.md", result.Value.SavedPath);
        Assert.Equal(revisedDraft, await storyStore.ReadAsync("novel", "chapter1.md"));

        var saved = await store.LoadAsync(sessionId);
        Assert.Equal(WriterState.Idle, saved.Writer.State);
        Assert.Null(saved.Writer.PendingContent);
        Assert.Null(saved.Writer.PendingProjectName);
        Assert.Null(saved.Writer.PendingFileName);
    }

    [Fact]
    public async Task AcceptWriterPendingAsync_UsesCapturedTarget_WhenActiveWriterTargetChanges()
    {
        var store = new InMemorySessionRuntimeStore();
        var storyStore = new InMemoryStoryStore();
        var service = CreateService(store, storyStore: storyStore);
        var sessionId = Guid.CreateVersion7();
        var draft = new string('b', 320);

        await service.SetModeAsync(
            sessionId,
            new SetSessionModeCommand("writer", "project-a", "chapter-a.md", null));
        await service.CaptureWriterPendingAsync(
            sessionId,
            new CaptureWriterPendingCommand(draft, Mode.Writer));
        await service.SetModeAsync(
            sessionId,
            new SetSessionModeCommand("writer", "project-b", "chapter-b.md", null));

        var result = await service.AcceptWriterPendingAsync(sessionId);

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.Equal("story/project-a/chapter-a.md", result.Value!.SavedPath);
        Assert.Equal(draft, await storyStore.ReadAsync("project-a", "chapter-a.md"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => storyStore.ReadAsync("project-b", "chapter-b.md"));

        var saved = await store.LoadAsync(sessionId);
        Assert.Equal(Mode.Writer, saved.Mode.ActiveMode);
        Assert.Equal("project-b", saved.Mode.ProjectName);
        Assert.Equal("chapter-b.md", saved.Mode.CurrentFile);
        Assert.Equal(WriterState.Idle, saved.Writer.State);
        Assert.Null(saved.Writer.PendingContent);
        Assert.Null(saved.Writer.PendingProjectName);
        Assert.Null(saved.Writer.PendingFileName);
    }

    [Fact]
    public async Task AcceptWriterPendingAsync_AfterTargetChangeAndRecapture_UsesLatestCapturedTarget()
    {
        var store = new InMemorySessionRuntimeStore();
        var storyStore = new InMemoryStoryStore();
        var service = CreateService(store, storyStore: storyStore);
        var sessionId = Guid.CreateVersion7();
        var initialDraft = new string('a', 300);
        var revisedDraft = new string('b', 320);

        await service.SetModeAsync(
            sessionId,
            new SetSessionModeCommand("writer", "project-a", "chapter-a.md", null));
        await service.CaptureWriterPendingAsync(
            sessionId,
            new CaptureWriterPendingCommand(initialDraft, Mode.Writer));
        await service.SetModeAsync(
            sessionId,
            new SetSessionModeCommand("writer", "project-b", "chapter-b.md", null));
        await service.CaptureWriterPendingAsync(
            sessionId,
            new CaptureWriterPendingCommand(revisedDraft, Mode.Writer));

        var pendingState = await store.LoadAsync(sessionId);
        Assert.Equal("project-b", pendingState.Writer.PendingProjectName);
        Assert.Equal("chapter-b.md", pendingState.Writer.PendingFileName);

        var result = await service.AcceptWriterPendingAsync(sessionId);

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.Equal("story/project-b/chapter-b.md", result.Value!.SavedPath);
        Assert.Equal(revisedDraft, await storyStore.ReadAsync("project-b", "chapter-b.md"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => storyStore.ReadAsync("project-a", "chapter-a.md"));
    }

    [Fact]
    public async Task AcceptWriterPendingAsync_WithoutProjectOrFile_ReturnsInvalid_AndPreservesPendingState()
    {
        var store = new InMemorySessionRuntimeStore();
        var storyStore = new InMemoryStoryStore();
        var sessionId = Guid.CreateVersion7();
        await store.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Writer,
            },
            Writer = new WriterRuntimeState
            {
                PendingContent = "Accepted text",
                State = WriterState.PendingReview,
            },
        });

        var service = CreateService(store, storyStore: storyStore);
        var result = await service.AcceptWriterPendingAsync(sessionId);

        Assert.Equal(SessionMutationStatus.Invalid, result.Status);
        Assert.Equal(
            "Writer pending content requires an active project and file before it can be accepted.",
            result.Error);
        await Assert.ThrowsAsync<FileNotFoundException>(() => storyStore.ReadAsync("novel", "chapter1.md"));

        var saved = await store.LoadAsync(sessionId);
        Assert.Equal(WriterState.PendingReview, saved.Writer.State);
        Assert.Equal("Accepted text", saved.Writer.PendingContent);
    }

    [Fact]
    public async Task AcceptWriterPendingAsync_PartialCapturedTarget_ReturnsInvalid_AndPreservesPendingState()
    {
        var store = new InMemorySessionRuntimeStore();
        var storyStore = new InMemoryStoryStore();
        var sessionId = Guid.CreateVersion7();
        await store.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Writer,
                ProjectName = "novel",
                CurrentFile = "chapter1.md",
            },
            Writer = new WriterRuntimeState
            {
                PendingContent = "Accepted text",
                PendingProjectName = "novel",
                State = WriterState.PendingReview,
            },
        });

        var service = CreateService(store, storyStore: storyStore);
        var result = await service.AcceptWriterPendingAsync(sessionId);

        Assert.Equal(SessionMutationStatus.Invalid, result.Status);
        Assert.Equal(
            "Writer pending content has an incomplete saved target and cannot be accepted safely.",
            result.Error);
        await Assert.ThrowsAsync<FileNotFoundException>(() => storyStore.ReadAsync("novel", "chapter1.md"));

        var saved = await store.LoadAsync(sessionId);
        Assert.Equal(WriterState.PendingReview, saved.Writer.State);
        Assert.Equal("Accepted text", saved.Writer.PendingContent);
        Assert.Equal("novel", saved.Writer.PendingProjectName);
        Assert.Null(saved.Writer.PendingFileName);
    }

    [Fact]
    public async Task AcceptWriterPendingAsync_PathTraversalTarget_ReturnsInvalid_AndPreservesPendingState()
    {
        var store = new InMemorySessionRuntimeStore();
        var storyStore = new InMemoryStoryStore();
        var sessionId = Guid.CreateVersion7();
        await store.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Writer,
                ProjectName = "novel",
                CurrentFile = "chapter1.md",
            },
            Writer = new WriterRuntimeState
            {
                PendingContent = "Accepted text",
                PendingProjectName = "../escape",
                PendingFileName = "chapter1.md",
                State = WriterState.PendingReview,
            },
        });

        var service = CreateService(store, storyStore: storyStore);
        var result = await service.AcceptWriterPendingAsync(sessionId);

        Assert.Equal(SessionMutationStatus.Invalid, result.Status);
        Assert.Equal(
            "Writer pending content requires an active project and file before it can be accepted.",
            result.Error);
        await Assert.ThrowsAsync<FileNotFoundException>(() => storyStore.ReadAsync("../escape", "chapter1.md"));

        var saved = await store.LoadAsync(sessionId);
        Assert.Equal(WriterState.PendingReview, saved.Writer.State);
        Assert.Equal("Accepted text", saved.Writer.PendingContent);
        Assert.Equal("../escape", saved.Writer.PendingProjectName);
        Assert.Equal("chapter1.md", saved.Writer.PendingFileName);
    }

    [Fact]
    public async Task RejectWriterPendingAsync_ResetsWriterState()
    {
        var store = new InMemorySessionRuntimeStore();
        var sessionId = Guid.CreateVersion7();
        await store.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState { ActiveMode = Mode.Writer },
            Writer = new WriterRuntimeState
            {
                PendingContent = "Rejected text",
                PendingProjectName = "novel",
                PendingFileName = "chapter1.md",
                State = WriterState.PendingReview,
            },
        });

        var service = CreateService(store);
        var result = await service.RejectWriterPendingAsync(sessionId);

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal(WriterState.Idle, result.Value.SessionView.Writer.State);
        Assert.Null(result.Value.SessionView.Writer.PendingContent);
        Assert.Null(result.Value.SessionView.Writer.PendingProjectName);
        Assert.Null(result.Value.SessionView.Writer.PendingFileName);

        var saved = await store.LoadAsync(sessionId);
        Assert.Equal(WriterState.Idle, saved.Writer.State);
        Assert.Null(saved.Writer.PendingContent);
        Assert.Null(saved.Writer.PendingProjectName);
        Assert.Null(saved.Writer.PendingFileName);
    }

    [Fact]
    public async Task RejectWriterPendingAsync_AfterRevision_ClearsRevisedPendingState()
    {
        var store = new InMemorySessionRuntimeStore();
        var storyStore = new InMemoryStoryStore();
        var service = CreateService(store, storyStore: storyStore);
        var sessionId = Guid.CreateVersion7();
        var initialDraft = new string('a', 300);
        var revisedDraft = new string('b', 320);

        await service.SetModeAsync(
            sessionId,
            new SetSessionModeCommand("writer", "novel", "chapter1.md", null));
        await service.CaptureWriterPendingAsync(
            sessionId,
            new CaptureWriterPendingCommand(initialDraft, Mode.Writer));
        await service.CaptureWriterPendingAsync(
            sessionId,
            new CaptureWriterPendingCommand(revisedDraft, Mode.Writer));

        var result = await service.RejectWriterPendingAsync(sessionId);

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.Equal(WriterState.Idle, result.Value!.SessionView.Writer.State);
        Assert.Null(result.Value.SessionView.Writer.PendingContent);
        await Assert.ThrowsAsync<FileNotFoundException>(() => storyStore.ReadAsync("novel", "chapter1.md"));

        var saved = await store.LoadAsync(sessionId);
        Assert.Equal(WriterState.Idle, saved.Writer.State);
        Assert.Null(saved.Writer.PendingContent);
        Assert.Null(saved.Writer.PendingProjectName);
        Assert.Null(saved.Writer.PendingFileName);
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
                null,
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
    public async Task UpdateNarrativeStateAsync_PersistsStickySessionCanon()
    {
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);
        var sessionId = Guid.CreateVersion7();

        var result = await service.UpdateNarrativeStateAsync(
            sessionId,
            new UpdateNarrativeStateCommand(
                "Rowan is masking his fear behind dry humor.",
                "- Captain Rowe suspects contraband in the tide tunnels.\n- Rowan still carries the lighthouse keeper's ring."));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Contains("Captain Rowe suspects contraband", result.Value.Narrative.StickySessionCanon);

        var saved = await store.LoadAsync(sessionId);
        Assert.Contains("lighthouse keeper's ring", saved.Narrative.StickySessionCanon);
    }

    [Fact]
    public async Task UpdateNarrativeStateAsync_EmptyStickySessionCanon_DoesNotClearExistingCanon()
    {
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);
        var sessionId = Guid.CreateVersion7();

        await store.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Narrative = new NarrativeRuntimeState
            {
                StickySessionCanon = "- Rowan still carries the lighthouse keeper's ring.",
            },
        });

        var result = await service.UpdateNarrativeStateAsync(
            sessionId,
            new UpdateNarrativeStateCommand(
                "Rowan is still evasive.",
                "   "));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal(
            "- Rowan still carries the lighthouse keeper's ring.",
            result.Value.Narrative.StickySessionCanon);

        var saved = await store.LoadAsync(sessionId);
        Assert.Equal(
            "- Rowan still carries the lighthouse keeper's ring.",
            saved.Narrative.StickySessionCanon);
    }

    [Fact]
    public async Task LoadViewAsync_HydratesDefaultProfileWithoutPersistingOverrides()
    {
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);
        var sessionId = Guid.CreateVersion7();

        var view = await service.LoadViewAsync(sessionId);

        Assert.Equal("default", view.Profile.ProfileId);
        Assert.Equal("default-lore", view.Profile.ActiveLoreSet);
        Assert.Equal("default-rules", view.Profile.ActiveNarrativeRules);
        Assert.Equal("default-style", view.Profile.ActiveWritingStyle);
        Assert.Equal("default-guide", view.Roleplay.ActiveAiCharacter);
        Assert.Equal("default-author", view.Roleplay.ActiveUserCharacter);

        var raw = await store.LoadAsync(sessionId);
        Assert.Null(raw.Profile.ProfileId);
        Assert.Null(raw.Profile.ActiveLoreSet);
        Assert.Null(raw.Profile.ActiveNarrativeRules);
        Assert.Null(raw.Profile.ActiveWritingStyle);
        Assert.False(raw.Roleplay.HasExplicitAiCharacterSelection);
        Assert.False(raw.Roleplay.HasExplicitUserCharacterSelection);
        Assert.Null(raw.Roleplay.ActiveAiCharacter);
        Assert.Null(raw.Roleplay.ActiveUserCharacter);
    }

    [Fact]
    public async Task SetRoleplayAsync_StoresExplicitSelectionsPerSession()
    {
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);
        var sessionId = Guid.CreateVersion7();

        var result = await service.SetRoleplayAsync(
            sessionId,
            new SetSessionRoleplayCommand(true, "session-guide", true, "session-author"));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal("session-guide", result.Value.Roleplay.ActiveAiCharacter);
        Assert.Equal("session-author", result.Value.Roleplay.ActiveUserCharacter);

        var raw = await store.LoadAsync(sessionId);
        Assert.True(raw.Roleplay.HasExplicitAiCharacterSelection);
        Assert.Equal("session-guide", raw.Roleplay.ActiveAiCharacter);
        Assert.True(raw.Roleplay.HasExplicitUserCharacterSelection);
        Assert.Equal("session-author", raw.Roleplay.ActiveUserCharacter);
    }

    [Fact]
    public async Task SetRoleplayAsync_AllowsExplicitClearAgainstProfileDefaults()
    {
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);
        var sessionId = Guid.CreateVersion7();

        var result = await service.SetRoleplayAsync(
            sessionId,
            new SetSessionRoleplayCommand(true, null, false, null));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Null(result.Value.Roleplay.ActiveAiCharacter);
        Assert.Equal("default-author", result.Value.Roleplay.ActiveUserCharacter);

        var raw = await store.LoadAsync(sessionId);
        Assert.True(raw.Roleplay.HasExplicitAiCharacterSelection);
        Assert.Null(raw.Roleplay.ActiveAiCharacter);
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
        Assert.Equal("grim-lore", result.Value.Profile.ActiveLoreSet);
        Assert.Equal("grim-rules", result.Value.Profile.ActiveNarrativeRules);
        Assert.Equal("grim-style", result.Value.Profile.ActiveWritingStyle);

        var raw = await store.LoadAsync(sessionId);
        Assert.Equal("grim", raw.Profile.ProfileId);
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
            new SetSessionProfileCommand(null, "custom-lore", null, null, null));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal("grim", result.Value.Profile.ProfileId);
        Assert.Equal("custom-lore", result.Value.Profile.ActiveLoreSet);
        Assert.Equal("grim-rules", result.Value.Profile.ActiveNarrativeRules);
        Assert.Equal("grim-style", result.Value.Profile.ActiveWritingStyle);

        var raw = await store.LoadAsync(sessionId);
        Assert.Equal("grim", raw.Profile.ProfileId);
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
            new SetSessionProfileCommand(null, "session-b-lore", null, null, null));

        var viewA = await service.LoadViewAsync(sessionA);
        var viewB = await service.LoadViewAsync(sessionB);

        Assert.Equal("grim", viewA.Profile.ProfileId);
        Assert.Equal("default", viewB.Profile.ProfileId);
        Assert.Equal("session-b-lore", viewB.Profile.ActiveLoreSet);
        Assert.Equal("grim-guide", viewA.Roleplay.ActiveAiCharacter);
        Assert.Equal("default-guide", viewB.Roleplay.ActiveAiCharacter);
    }

    [Fact]
    public async Task SetProfileAsync_ProfileSwitchUpdatesImplicitRoleplayDefaultsButKeepsExplicitSelections()
    {
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);
        var sessionId = Guid.CreateVersion7();

        await service.SetProfileAsync(
            sessionId,
            new SetSessionProfileCommand("grim", null, null, null, null));
        await service.SetRoleplayAsync(
            sessionId,
            new SetSessionRoleplayCommand(false, null, true, "session-author"));

        var result = await service.SetProfileAsync(
            sessionId,
            new SetSessionProfileCommand("storm", null, null, null, null));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal("storm", result.Value.Profile.ProfileId);
        Assert.Equal("storm-guide", result.Value.Roleplay.ActiveAiCharacter);
        Assert.Equal("session-author", result.Value.Roleplay.ActiveUserCharacter);

        var raw = await store.LoadAsync(sessionId);
        Assert.False(raw.Roleplay.HasExplicitAiCharacterSelection);
        Assert.Equal("storm-guide", raw.Roleplay.ActiveAiCharacter);
        Assert.True(raw.Roleplay.HasExplicitUserCharacterSelection);
        Assert.Equal("session-author", raw.Roleplay.ActiveUserCharacter);
    }

    [Fact]
    public async Task LoadViewAsync_NormalizesLegacyHydratedDefaultsForUntouchedSession()
    {
        var store = new InMemorySessionRuntimeStore();
        var profiles = new FakeProfileConfigService();
        var service = CreateService(store, profiles);
        var sessionId = Guid.CreateVersion7();

        await store.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Profile = new ProfileState
            {
                ProfileId = "grim",
                ActiveLoreSet = "grim-lore",
                ActiveNarrativeRules = "grim-rules",
                ActiveWritingStyle = "grim-style",
            },
        });

        profiles.SetProfile("grim", new ProfileConfig
        {
            LoreSet = "grim-lore-v2",
            NarrativeRules = "grim-rules-v2",
            WritingStyle = "grim-style-v2",
        });

        var view = await service.LoadViewAsync(sessionId);

        Assert.Equal("grim", view.Profile.ProfileId);
        Assert.Equal("grim-lore-v2", view.Profile.ActiveLoreSet);
        Assert.Equal("grim-rules-v2", view.Profile.ActiveNarrativeRules);
        Assert.Equal("grim-style-v2", view.Profile.ActiveWritingStyle);

        var raw = await store.LoadAsync(sessionId);
        Assert.Equal("grim", raw.Profile.ProfileId);
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
            new SetSessionProfileCommand(null, "custom-lore", null, null, null));

        profiles.SetProfile("grim", new ProfileConfig
        {
            LoreSet = "grim-lore-v2",
            NarrativeRules = "grim-rules-v2",
            WritingStyle = "grim-style-v2",
        });

        var view = await service.LoadViewAsync(sessionId);

        Assert.Equal("grim", view.Profile.ProfileId);
        Assert.Equal("custom-lore", view.Profile.ActiveLoreSet);
        Assert.Equal("grim-rules-v2", view.Profile.ActiveNarrativeRules);
        Assert.Equal("grim-style-v2", view.Profile.ActiveWritingStyle);

        var raw = await store.LoadAsync(sessionId);
        Assert.Equal("grim", raw.Profile.ProfileId);
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

        await store.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState { ActiveMode = Mode.Writer },
            Profile = new ProfileState
            {
                ProfileId = "grim",
                ActiveLoreSet = "custom-lore",
                ActiveNarrativeRules = "custom-rules",
                ActiveWritingStyle = "custom-style",
            },
        });

        profiles.SetProfile("grim", new ProfileConfig
        {
            LoreSet = "grim-lore-v2",
            NarrativeRules = "grim-rules-v2",
            WritingStyle = "grim-style-v2",
        });

        var view = await service.LoadViewAsync(sessionId);

        Assert.Equal("custom-lore", view.Profile.ActiveLoreSet);
        Assert.Equal("custom-rules", view.Profile.ActiveNarrativeRules);
        Assert.Equal("custom-style", view.Profile.ActiveWritingStyle);

        var raw = await store.LoadAsync(sessionId);
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
        await store.SaveAsync(new SessionState
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
        await store.SaveAsync(new SessionState
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
        FakeProfileConfigService? profileService = null,
        InMemoryStoryStore? storyStore = null)
    {
        return new SessionRuntimeService(
            store,
            new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance),
            profileService ?? new FakeProfileConfigService(),
            storyStore ?? new InMemoryStoryStore(),
            Modes,
            NullLogger<SessionRuntimeService>.Instance);
    }
}

internal sealed class InMemorySessionRuntimeStore : ISessionStateStore
{
    private readonly Dictionary<string, SessionState> _states = [];

    public Task<SessionState> LoadAsync(Guid? sessionId, CancellationToken ct = default)
    {
        if (_states.TryGetValue(GetKey(sessionId), out var state))
        {
            return Task.FromResult(Clone(state, sessionId));
        }

        return Task.FromResult(new SessionState { SessionId = sessionId });
    }

    public Task SaveAsync(SessionState state, CancellationToken ct = default)
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

internal sealed class InMemoryStoryStore : IStoryStore
{
    private readonly Dictionary<string, string> _files = [];

    public Task<string> ReadAsync(string projectName, string fileName, CancellationToken ct = default)
    {
        var key = GetKey(projectName, fileName);
        if (!_files.TryGetValue(key, out var content))
        {
            throw new FileNotFoundException($"Story file not found: {projectName}/{fileName}");
        }

        return Task.FromResult(content);
    }

    public Task AppendAsync(string projectName, string fileName, string content, CancellationToken ct = default)
    {
        var key = GetKey(projectName, fileName);
        _files[key] = _files.TryGetValue(key, out var existing)
            ? existing + content
            : content;
        return Task.CompletedTask;
    }

    public Task WriteAsync(string projectName, string fileName, string content, CancellationToken ct = default)
    {
        _files[GetKey(projectName, fileName)] = content;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<string>> ListProjectsAsync(CancellationToken ct = default)
    {
        var projects = _files.Keys
            .Select(key => key.Split('/', 2)[0])
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(projects);
    }

    public Task<IReadOnlyList<string>> ListFilesAsync(string projectName, CancellationToken ct = default)
    {
        var prefix = projectName + "/";
        var files = _files.Keys
            .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
            .Select(key => key[prefix.Length..])
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(files);
    }

    private static string GetKey(string projectName, string fileName)
    {
        return $"{projectName}/{fileName}";
    }
}

internal sealed class FakeProfileConfigService : IProfileConfigService
{
    private readonly Dictionary<string, ProfileConfig> _profiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["default"] = new()
        {
            LoreSet = "default-lore",
            NarrativeRules = "default-rules",
            WritingStyle = "default-style",
            Roleplay = new RoleplayConfig
            {
                AiCharacter = "default-guide",
                UserCharacter = "default-author",
            },
        },
        ["grim"] = new()
        {
            LoreSet = "grim-lore",
            NarrativeRules = "grim-rules",
            WritingStyle = "grim-style",
            Roleplay = new RoleplayConfig
            {
                AiCharacter = "grim-guide",
                UserCharacter = "grim-author",
            },
        },
        ["storm"] = new()
        {
            LoreSet = "storm-lore",
            NarrativeRules = "storm-rules",
            WritingStyle = "storm-style",
            Roleplay = new RoleplayConfig
            {
                AiCharacter = "storm-guide",
                UserCharacter = "storm-author",
            },
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
