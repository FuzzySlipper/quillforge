using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core;
using QuillForge.Core.Models;
using QuillForge.Storage.FileSystem;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.Tests;

public sealed class SessionRoundTripStressTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemSessionStore _sessionStore;
    private readonly FileSystemSessionRuntimeStore _runtimeStore;

    public SessionRoundTripStressTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"quillforge-roundtrip-stress-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var loggerFactory = NullLoggerFactory.Instance;
        var writer = new AtomicFileWriter(loggerFactory.CreateLogger<AtomicFileWriter>());

        _sessionStore = new FileSystemSessionStore(
            Path.Combine(_tempDir, ContentPaths.DataSessions),
            writer,
            loggerFactory.CreateLogger<FileSystemSessionStore>(),
            loggerFactory);
        _runtimeStore = new FileSystemSessionRuntimeStore(
            _tempDir,
            writer,
            loggerFactory.CreateLogger<FileSystemSessionRuntimeStore>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SessionAndRuntimeState_RoundTripAcrossTwentyFourTurns_PreserveIdentityAndContinuity()
    {
        const int turnCount = 24;

        var sessionId = Guid.CreateVersion7();
        var tree = new ConversationTree(
            sessionId,
            "Stress Session",
            NullLogger<ConversationTree>.Instance);
        var runtime = new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Writer,
                ProjectName = "novel-stress",
                CurrentFile = "chapter-01.md",
            },
            Profile = new ProfileState
            {
                ProfileId = "stress-profile",
                ActiveConductor = "stress-conductor",
                ActiveLoreSet = "stress-lore",
                ActiveNarrativeRules = "stress-rules",
                ActiveWritingStyle = "stress-style",
            },
            Writer = new WriterRuntimeState
            {
                State = WriterState.PendingReview,
                PendingContent = "Pending draft turn 0",
            },
            Roleplay = new RoleplayRuntimeState
            {
                HasExplicitAiCharacterSelection = true,
                ActiveAiCharacter = "guide-0",
            },
        };

        var expectedUserIds = new List<Guid>();
        var expectedAssistantIds = new List<Guid>();
        var expectedAssistantStopReasons = new List<StopReason>();

        for (var turn = 0; turn < turnCount; turn++)
        {
            var userNode = tree.Append(
                tree.ActiveLeafId,
                "user",
                new MessageContent($"User turn {turn}"));
            expectedUserIds.Add(userNode.Id);

            var stopReason = turn % 5 == 4 ? StopReason.MaxTokens : StopReason.EndTurn;
            var assistantNode = tree.Append(
                tree.ActiveLeafId,
                "assistant",
                new MessageContent($"Assistant turn {turn}"),
                new MessageMetadata
                {
                    Model = "stress-model",
                    InputTokens = turn + 10,
                    OutputTokens = turn + 20,
                    StopReason = stopReason,
                });
            expectedAssistantIds.Add(assistantNode.Id);
            expectedAssistantStopReasons.Add(stopReason);

            runtime.Writer.PendingContent = $"Pending draft turn {turn}";
            runtime.Writer.State = turn % 2 == 0 ? WriterState.PendingReview : WriterState.Idle;
            runtime.Roleplay.ActiveAiCharacter = $"guide-{turn}";
            runtime.Roleplay.HasExplicitAiCharacterSelection = true;
            runtime.Narrative.DirectorNotes = $"Director notes {turn}";
            runtime.Narrative.ActivePlotFile = $"plot-{turn % 3}.md";
            runtime.Narrative.PlotProgress.CurrentBeat = $"beat-{turn}";
            runtime.Narrative.PlotProgress.CompletedBeats.Add($"completed-{turn}");
            if (turn % 4 == 0)
            {
                runtime.Narrative.PlotProgress.Deviations.Add($"deviation-{turn}");
            }

            await _sessionStore.SaveAsync(tree);
            await _runtimeStore.SaveAsync(runtime);

            tree = await _sessionStore.LoadAsync(sessionId);
            runtime = await _runtimeStore.LoadAsync(sessionId);

            var thread = tree.ToFlatThread();
            Assert.Equal((turn + 1) * 2, thread.Count);
            Assert.Equal(expectedUserIds[turn], thread[^2].Id);
            Assert.Equal(expectedAssistantIds[turn], thread[^1].Id);
            Assert.Equal($"User turn {turn}", thread[^2].Content.GetText());
            Assert.Equal($"Assistant turn {turn}", thread[^1].Content.GetText());
            Assert.Equal(stopReason, thread[^1].Metadata?.StopReason);
            Assert.Equal(expectedAssistantIds[turn], tree.ActiveLeafId);

            Assert.Equal(sessionId, runtime.SessionId);
            Assert.Equal(Mode.Writer, runtime.Mode.ActiveMode);
            Assert.Equal("novel-stress", runtime.Mode.ProjectName);
            Assert.Equal("chapter-01.md", runtime.Mode.CurrentFile);
            Assert.Equal("stress-profile", runtime.Profile.ProfileId);
            Assert.Equal("stress-lore", runtime.Profile.ActiveLoreSet);
            Assert.Equal($"Pending draft turn {turn}", runtime.Writer.PendingContent);
            Assert.Equal(turn % 2 == 0 ? WriterState.PendingReview : WriterState.Idle, runtime.Writer.State);
            Assert.Equal($"guide-{turn}", runtime.Roleplay.ActiveAiCharacter);
            Assert.Equal($"Director notes {turn}", runtime.Narrative.DirectorNotes);
            Assert.Equal($"plot-{turn % 3}.md", runtime.Narrative.ActivePlotFile);
            Assert.Equal($"beat-{turn}", runtime.Narrative.PlotProgress.CurrentBeat);
            Assert.Contains($"completed-{turn}", runtime.Narrative.PlotProgress.CompletedBeats);
            if (turn % 4 == 0)
            {
                Assert.Contains($"deviation-{turn}", runtime.Narrative.PlotProgress.Deviations);
            }
        }

        var finalTree = await _sessionStore.LoadAsync(sessionId);
        var finalRuntime = await _runtimeStore.LoadAsync(sessionId);
        var finalThread = finalTree.ToFlatThread();

        Assert.Equal(turnCount * 2, finalThread.Count);

        var userIdsFromStore = finalThread
            .Where(node => string.Equals(node.Role, "user", StringComparison.Ordinal))
            .Select(node => node.Id)
            .ToList();
        var assistantIdsFromStore = finalThread
            .Where(node => string.Equals(node.Role, "assistant", StringComparison.Ordinal))
            .Select(node => node.Id)
            .ToList();
        var assistantStopReasonsFromStore = finalThread
            .Where(node => string.Equals(node.Role, "assistant", StringComparison.Ordinal))
            .Select(node => node.Metadata?.StopReason ?? StopReason.EndTurn)
            .ToList();

        Assert.Equal(expectedUserIds, userIdsFromStore);
        Assert.Equal(expectedAssistantIds, assistantIdsFromStore);
        Assert.Equal(expectedAssistantStopReasons, assistantStopReasonsFromStore);

        Assert.Equal("Pending draft turn 23", finalRuntime.Writer.PendingContent);
        Assert.Equal(WriterState.Idle, finalRuntime.Writer.State);
        Assert.Equal("guide-23", finalRuntime.Roleplay.ActiveAiCharacter);
        Assert.Equal("Director notes 23", finalRuntime.Narrative.DirectorNotes);
        Assert.Equal("beat-23", finalRuntime.Narrative.PlotProgress.CurrentBeat);
        Assert.Contains("completed-23", finalRuntime.Narrative.PlotProgress.CompletedBeats);
        Assert.Contains("deviation-20", finalRuntime.Narrative.PlotProgress.Deviations);
    }
}
