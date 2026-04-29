using Den.RulesEngine;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
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
        Assert.Equal(GameRuntimeStatus.WaitingForInput, loaded.Game.Status);
        Assert.Equal("game-001", loaded.Game.GameInstanceId);
        Assert.Equal("test-game", loaded.Game.ModuleId);
        Assert.Equal(1234, loaded.Game.Seed);
        Assert.Single(loaded.Game.EngineSnapshot!.EventJournal.Events);
        Assert.IsType<GameStartedEvent>(loaded.Game.EngineSnapshot.EventJournal.Events[0]);
        Assert.Equal(2, loaded.Game.EngineSnapshot.PendingInputs.Count);
        Assert.Equal(2, loaded.Game.Communication.Participants.Count);
        Assert.Single(loaded.Game.Communication.ChannelMessages);
        Assert.Single(loaded.Game.Communication.DirectMessages);
        Assert.Single(loaded.Game.Communication.GameEventLinks);
        Assert.Single(loaded.Game.Communication.Cursors);
        Assert.Single(loaded.Game.AgentMemories);
        Assert.Single(loaded.Game.MemorySummaryDecisions);
        Assert.Single(loaded.Game.EventDeliveryCursors);
        Assert.Single(loaded.Game.PromptCursors);
        Assert.Single(loaded.Game.PromptEnvelopes);
        Assert.Contains(loaded.Game.HostRecords, record => record.Kind == GameRuntimeHostRecordKind.Started);

        var expectedFile = Path.Combine(_tempDir, ContentPaths.DataSessionState, $"{sessionId}.json");
        Assert.True(File.Exists(expectedFile));
    }

    [Fact]
    public async Task LoadedRuntime_CanResumePendingUserAndAgentActionsAfterReload()
    {
        var sessionId = Guid.Parse("cccccccc-dddd-eeee-ffff-000000000000");
        var store = CreateStore();
        await store.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Game = CreateGameRuntime(),
        });

        var reloadedStore = CreateStore();
        var registry = new GameModuleRegistry();
        Assert.True(registry.Register(new PersistenceTestModule()).IsValid);
        var runtime = new GameRuntimeService(
            reloadedStore,
            new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance),
            registry,
            new RulesEngineService(registry),
            new ParticipantChannelService(),
            new DefaultGameEventNarrationComposer(),
            NullLogger<GameRuntimeService>.Instance);

        var user = await runtime.ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(
                new SubmitPlayerChoiceIntentCommand(
                    GameIntentCommandId.NewId(),
                    new GameInstanceId("game-001"),
                    new PendingInputId("pending-human-1"),
                    new ParticipantId("human-1"),
                    "approve"),
                DateTimeOffset.Parse("2026-04-27T11:06:00+00:00")));
        Assert.True(user.Status == SessionMutationStatus.Success, user.Error);

        var completion = new ScriptedCompletionService();
        var agentTurns = new GameAgentTurnService(
            runtime,
            registry,
            completion,
            new AgentVisibleEventsService(new GameVisibilityProjector(), new ParticipantChannelService()),
            new DefaultOnlyGamePromptTemplateService(),
            new AppConfig(),
            NullLogger<GameAgentTurnService>.Instance);
        var agent = await agentTurns.RunPendingAgentTurnsAsync(
            sessionId,
            new RunGameAgentTurnsCommand(DateTimeOffset.Parse("2026-04-27T11:07:00+00:00"), MaxConcurrency: 1));
        Assert.Equal(SessionMutationStatus.Success, agent.Status);
        Assert.Contains(agent.Value!.ParticipantResults, result => result.ParticipantId == "agent-1" && result.Outcome == GameAgentTurnOutcome.Applied);

        var loaded = await reloadedStore.LoadAsync(sessionId);
        Assert.All(loaded.Game!.EngineSnapshot!.PendingInputs, input => Assert.Equal(PendingInputStatus.Submitted, input.Status));
        Assert.Contains(loaded.Game.PromptEnvelopes, envelope => envelope.ParticipantId == "agent-1" && envelope.ProviderAlias == "local");
    }

    [Fact]
    public async Task FileSystemSessionRuntimeStore_PreservesUnknownModuleEventMetadataAsStoredGameEvent()
    {
        var store = CreateStore();
        var sessionId = Guid.Parse("bbbbbbbb-cccc-dddd-eeee-ffffffffffff");
        var runtime = CreateGameRuntime();
        var liveState = runtime.EngineSnapshot!.ToState();
        runtime.EngineSnapshot = RulesGameStateSnapshot.FromState(liveState with
        {
            EventJournal = liveState.EventJournal.Append(UnknownModuleEvent.Create(liveState.GameInstanceId)),
        });
        var state = new SessionState
        {
            SessionId = sessionId,
            Game = runtime,
        };

        await store.SaveAsync(state);

        var loaded = await store.LoadAsync(sessionId);

        var stored = Assert.IsType<StoredGameEvent>(loaded.Game!.EngineSnapshot!.EventJournal.Events[1]);
        Assert.Equal(nameof(UnknownModuleEvent), stored.EventType);
        Assert.Equal(2, stored.Sequence);
        Assert.Equal(GameEventVisibilityKind.Public, stored.Visibility.Kind);
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

    private sealed record UnknownModuleEvent(
        GameEventId EventId,
        long Sequence,
        GameInstanceId GameInstanceId,
        DateTimeOffset OccurredAt,
        GameEventVisibility Visibility) : GameEventBase(EventId, Sequence, GameInstanceId, OccurredAt, Visibility)
    {
        public static UnknownModuleEvent Create(GameInstanceId gameInstanceId) =>
            new(default, 0, gameInstanceId, default, GameEventVisibility.Public);

        public override IGameEvent WithJournalMetadata(GameEventId eventId, long sequence, DateTimeOffset occurredAt) =>
            this with { EventId = eventId, Sequence = sequence, OccurredAt = occurredAt };
    }

    private sealed class ScriptedCompletionService : ICompletionService
    {
        public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default) =>
            Task.FromResult(new CompletionResponse
            {
                Content = new MessageContent("{\"accepted\":true,\"pendingInputId\":\"pending-agent-1\",\"choiceName\":\"approve\",\"message\":\"ok\"}"),
                StopReason = StopReason.EndTurn,
                Usage = new TokenUsage(9, 3),
            });

        public async IAsyncEnumerable<StreamEvent> StreamAsync(
            CompletionRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var response = await CompleteAsync(request, ct);
            yield return new TextDeltaEvent(response.Content.GetText());
            yield return new DoneEvent(response.StopReason, response.Usage);
        }
    }

    private sealed class PersistenceTestModule : IGameModule
    {
        public GameModuleDescriptor Descriptor { get; } = new(
            new GameModuleId("test-game"),
            new GameModuleVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            "Test Game",
            new PlayerCountRange(1, 4),
            [])
        {
            ParticipantRequirements = new GameParticipantRequirements(true, true, false, 1, 1),
        };

        public ValidationResult ValidateSetup(GameSetupValidationContext context) => ValidationResult.Valid;

        public RulesGameState CreateInitialState(GameSetupInitializationContext context) =>
            RulesGameState.CreateNotStarted(context.GameInstanceId, Descriptor, context.Seed, []);

        public IReadOnlyList<LegalIntentDescriptor> GetLegalIntentDescriptors(RulesGameState state, ParticipantId participantId) => [];

        public GameModuleTransitionResult HandleIntentCommand(GameModuleTransitionContext context) =>
            GameModuleTransitionResult.Accepted(context.State, []);

        public IReadOnlyList<GameRuleHandlerDescriptor> GetRuleHandlerDescriptors() => [];

        public IReadOnlyList<GamePromptAsset> GetPromptAssets() =>
        [
            new GamePromptAsset("test-rules", GamePromptAssetKind.RulesText, "Use the listed legal choices."),
        ];
    }

    private static GameRuntimeState CreateGameRuntime()
    {
        var gameInstanceId = new GameInstanceId("game-001");
        var moduleId = new GameModuleId("test-game");
        var moduleVersion = new GameModuleVersion("1.0.0");
        var participant = ParticipantState.Human(new ParticipantId("human-1"), "Human");
        var agent = ParticipantState.Agent(new ParticipantId("agent-1"), "Agent");
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
            [participant, agent]);
        state = state with
        {
            Status = RulesGameStatus.WaitingForInput,
            Stage = new GameStageState(new GameStageId("choice"), "Choice", 1, true, true),
            PendingInputs =
            [
                new PendingInputState(
                    new PendingInputId("pending-human-1"),
                    new ParticipantId("human-1"),
                    new GameStageId("choice"),
                    "choose",
                    PendingInputStatus.Waiting,
                    [new LegalIntentOption("approve", "Approve", "Approve the proposal.")]),
                new PendingInputState(
                    new PendingInputId("pending-agent-1"),
                    new ParticipantId("agent-1"),
                    new GameStageId("choice"),
                    "choose",
                    PendingInputStatus.Waiting,
                    [new LegalIntentOption("approve", "Approve", "Approve the proposal.")]),
            ],
            EventJournal = state.EventJournal.Append(GameStartedEvent.Create(gameInstanceId, moduleId, moduleVersion, 1234)),
        };

        return new GameRuntimeState
        {
            Status = GameRuntimeStatus.WaitingForInput,
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
                new GameRuntimeParticipantBinding
                {
                    ParticipantId = "agent-1",
                    DisplayName = "Agent",
                    Kind = GameRuntimeParticipantKind.Agent,
                    ProviderAlias = "local",
                    ModelOverride = "test-model",
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
                    new ParticipantPresenceState
                    {
                        ParticipantId = new GameParticipantId("agent-1"),
                        DisplayName = "Agent",
                        IsJoined = true,
                        JoinedSequence = 2,
                    },
                ],
                ChannelMessages =
                [
                    new ParticipantChannelMessage(
                        Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        2,
                        new ParticipantMessageAuthor(new GameParticipantId("human-1"), ParticipantMessageAuthorKind.Human),
                        "Public claim.",
                        DateTimeOffset.Parse("2026-04-27T11:01:00+00:00")),
                ],
                DirectMessages =
                [
                    new ParticipantDirectMessage(
                        Guid.Parse("22222222-2222-2222-2222-222222222222"),
                        3,
                        new ParticipantMessageAuthor(new GameParticipantId("agent-1"), ParticipantMessageAuthorKind.Agent),
                        [new GameParticipantId("human-1")],
                        "Private claim.",
                        DateTimeOffset.Parse("2026-04-27T11:02:00+00:00")),
                ],
                GameEventLinks =
                [
                    new ParticipantGameEventLink(
                        Guid.Parse("33333333-3333-3333-3333-333333333333"),
                        4,
                        state.EventJournal.Events[0].EventId.ToString(),
                        1,
                        ParticipantGameEventLinkVisibility.Public,
                        [],
                        "Game started.",
                        DateTimeOffset.Parse("2026-04-27T11:03:00+00:00")),
                ],
                Cursors =
                [
                    new ParticipantCommunicationCursor
                    {
                        ParticipantId = new GameParticipantId("agent-1"),
                        DeliveredThroughSequence = 4,
                        ReadThroughSequence = 3,
                    },
                ],
            },
            EventDeliveryCursors =
            [
                new GameRuntimeEventDeliveryCursor
                {
                    ParticipantId = "human-1",
                    DeliveredThroughEngineEventSequence = 1,
                    DeliveredThroughCommunicationSequence = 4,
                    MemoryRevision = 1,
                    LastPromptEnvelopeId = "envelope-1",
                },
            ],
            PromptCursors =
            [
                new GameRuntimeAgentPromptDeliveryCursor
                {
                    ParticipantId = "agent-1",
                    LastDeliveredPublicEngineEventSequence = 1,
                    CommunicationDeliveredThroughSequence = 4,
                    MemoryRevision = 1,
                    LastPromptEnvelopeId = "envelope-1",
                },
            ],
            PromptEnvelopes =
            [
                new GameRuntimeAgentPromptEnvelope
                {
                    EnvelopeId = "envelope-1",
                    ParticipantId = "agent-1",
                    CreatedAt = DateTimeOffset.Parse("2026-04-27T11:05:00+00:00"),
                    EngineCursorSequence = 1,
                    CommunicationCursorSequence = 4,
                    MemoryRevision = 1,
                    ProviderAlias = "local",
                    Model = "test-model",
                    PromptTokens = 10,
                    ResponseTokens = 4,
                    PromptContentHash = "prompt-hash",
                    ResponseContentHash = "response-hash",
                    PromptText = "Prompt text.",
                    ResponseText = "Response text.",
                },
            ],
            AgentMemories =
            [
                new GameRuntimeAgentMemoryState
                {
                    ParticipantId = "agent-1",
                    Revision = 1,
                    TokenBudget = 512,
                    Summary = "Remember the opening claim.",
                    ContentHash = "memory-hash",
                    LastSummarizedRoundNumber = 1,
                    LastSummarizedPublicEngineEventSequence = 1,
                    LastSummarizedCommunicationSequence = 4,
                },
            ],
            MemorySummaryDecisions =
            [
                new MemorySummaryDecision(
                    "decision-1",
                    "agent-1",
                    1,
                    DateTimeOffset.Parse("2026-04-27T11:04:00+00:00"),
                    AgentVisibleEventsCursor.Empty,
                    new AgentVisibleEventsCursor(1, [], 4, 1),
                    10,
                    4,
                    false,
                    false,
                    false,
                    "local",
                    "test-model",
                    "snapshot-1",
                    null,
                    "memory-hash"),
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
