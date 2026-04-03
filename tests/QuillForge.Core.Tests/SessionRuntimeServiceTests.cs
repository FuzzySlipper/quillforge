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

    private static SessionRuntimeService CreateService(InMemorySessionRuntimeStore store)
    {
        return new SessionRuntimeService(
            store,
            new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance),
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
                ActivePersona = state.Profile.ActivePersona,
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
