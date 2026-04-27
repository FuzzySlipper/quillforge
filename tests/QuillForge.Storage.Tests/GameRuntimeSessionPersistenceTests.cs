using Den.RulesEngine;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core;
using QuillForge.Core.Models;
using QuillForge.Storage.FileSystem;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.Tests;

public sealed class GameRuntimeSessionPersistenceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"quillforge-game-runtime-{Guid.NewGuid():N}");

    public GameRuntimeSessionPersistenceTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public async Task FileSystemSessionRuntimeStore_RoundTripsGameRuntimeStateThroughSessionStatePath()
    {
        var store = CreateStore();
        var sessionId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var state = new SessionState
        {
            SessionId = sessionId,
            Game = CreateGameRuntime(),
        };

        await store.SaveAsync(state);

        var loaded = await store.LoadAsync(sessionId);

        Assert.NotNull(loaded.Game);
        Assert.Equal(GameRuntimeStatus.Running, loaded.Game.Status);
        Assert.Equal("game-001", loaded.Game.GameInstanceId);
        Assert.Equal("test-game", loaded.Game.ModuleId);
        Assert.Equal(1234, loaded.Game.Seed);
        Assert.Single(loaded.Game.EngineSnapshot!.EventJournal.Events);
        Assert.IsType<GameStartedEvent>(loaded.Game.EngineSnapshot.EventJournal.Events[0]);
        Assert.Single(loaded.Game.Communication.Participants);
        Assert.Single(loaded.Game.AgentMemories);
        Assert.Single(loaded.Game.EventDeliveryCursors);
        Assert.Contains(loaded.Game.HostRecords, record => record.Kind == GameRuntimeHostRecordKind.Started);

        var expectedFile = Path.Combine(_tempDir, ContentPaths.DataSessionState, $"{sessionId}.json");
        Assert.True(File.Exists(expectedFile));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    private FileSystemSessionRuntimeStore CreateStore() => new(
        _tempDir,
        new AtomicFileWriter(NullLogger<AtomicFileWriter>.Instance),
        NullLogger<FileSystemSessionRuntimeStore>.Instance);

    private static GameRuntimeState CreateGameRuntime()
    {
        var gameInstanceId = new GameInstanceId("game-001");
        var moduleId = new GameModuleId("test-game");
        var moduleVersion = new GameModuleVersion("1.0.0");
        var participant = ParticipantState.Human(new ParticipantId("human-1"), "Human");
        var state = RulesGameState.CreateNotStarted(
            gameInstanceId,
            new GameModuleDescriptor(
                moduleId,
                moduleVersion,
                new GameTemplateVersion("1.0.0"),
                new GameTemplateVersion("1.0.0"),
                "Test Game",
                new PlayerCountRange(1, 4),
                []),
            1234,
            [participant]);
        state = state with
        {
            Status = RulesGameStatus.Running,
            EventJournal = state.EventJournal.Append(GameStartedEvent.Create(gameInstanceId, moduleId, moduleVersion, 1234)),
        };

        return new GameRuntimeState
        {
            Status = GameRuntimeStatus.Running,
            GameInstanceId = gameInstanceId.Value,
            TemplateId = "template-1",
            ModuleId = moduleId.Value,
            ModuleVersion = moduleVersion.Value,
            Seed = 1234,
            StartedAt = DateTimeOffset.Parse("2026-04-27T11:00:00+00:00"),
            LastUpdatedAt = DateTimeOffset.Parse("2026-04-27T11:00:00+00:00"),
            EngineSnapshot = RulesGameStateSnapshot.FromState(state),
            ParticipantBindings =
            [
                new GameRuntimeParticipantBinding
                {
                    ParticipantId = "human-1",
                    DisplayName = "Human",
                    Kind = GameRuntimeParticipantKind.Human,
                    UserSeatId = "user",
                },
            ],
            Communication = new ParticipantCommunicationState
            {
                NextSequence = 2,
                Participants =
                [
                    new ParticipantPresenceState
                    {
                        ParticipantId = new GameParticipantId("human-1"),
                        DisplayName = "Human",
                        IsJoined = true,
                        JoinedSequence = 1,
                    },
                ],
            },
            EventDeliveryCursors =
            [
                new GameRuntimeEventDeliveryCursor
                {
                    ParticipantId = "human-1",
                    DeliveredThroughEngineEventSequence = 1,
                },
            ],
            AgentMemories =
            [
                new GameRuntimeAgentMemoryState
                {
                    ParticipantId = "agent-1",
                    TokenBudget = 512,
                    Summary = "Remember the opening claim.",
                },
            ],
            HostRecords =
            [
                new GameRuntimeHostRecord
                {
                    Sequence = 1,
                    Kind = GameRuntimeHostRecordKind.Started,
                    OccurredAt = DateTimeOffset.Parse("2026-04-27T11:00:00+00:00"),
                    ReasonCode = "game_started",
                    Summary = "Started test game.",
                },
            ],
            NextHostRecordSequence = 2,
        };
    }
}
