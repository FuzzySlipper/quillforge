using Den.RulesEngine;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public sealed class GameInspectorServiceTests
{
    [Fact]
    public async Task GetProjectionAsync_ExposesDebugSurfaceWithoutRawPrivateEventPayloads()
    {
        var sessionId = Guid.Parse("88888888-8888-8888-8888-888888888888");
        var tracker = new InMemoryTokenUsageTracker(Microsoft.Extensions.Logging.Abstractions.NullLogger<InMemoryTokenUsageTracker>.Instance);
        tracker.Record(sessionId, "game-agent:agent-1", new TokenUsage(25, 7), 0);
        var service = new GameInspectorService(new FakeGameRuntimeService(CreateRuntime()), tracker);

        var projection = await service.GetProjectionAsync(sessionId, promptEnvelopeLimit: 1);

        Assert.True(projection.HasGame);
        Assert.Equal("game-inspector", projection.GameInstanceId);
        Assert.Equal("WaitingForInput", projection.RuntimeStatus);
        Assert.Equal(25, projection.TokenUsage.TotalInputTokens);
        Assert.Equal(2, projection.Participants.Count);
        Assert.Contains(projection.Participants, participant => participant.ParticipantId == "agent-1" && participant.ProviderAlias == "local" && participant.Model == "model-a");
        Assert.NotNull(projection.Engine);
        Assert.Contains(projection.Engine!.EventJournal, item => item.EventType == nameof(PlayerChoiceSubmittedEvent)
            && item.ParticipantId == "agent-1"
            && item.PendingInputId == "pending-agent"
            && item.Visibility == GameEventVisibilityKind.PrivateToParticipant.ToString());
        Assert.Contains(projection.Engine.EventJournal, item => item.EventType == nameof(PendingInputRequestedEvent)
            && item.ParticipantId == "human-1"
            && item.PendingInputId == "pending-human");
        Assert.Contains(projection.Engine.EventJournal, item => item.EventType == nameof(AgentResponseRejectedEvent)
            && item.ParticipantId == "agent-1"
            && item.PendingInputId == "pending-agent"
            && item.ReasonCode == "parse-fail");
        Assert.Contains(projection.Engine.EventJournal, item => item.EventType == nameof(NoActionTakenEvent)
            && item.ParticipantId == "agent-1"
            && item.PendingInputId == "pending-agent"
            && item.ReasonCode == "parse-fail");
        Assert.Contains(projection.Engine.EventJournal, item => item.EventType == nameof(RoundEndedEvent)
            && item.ReasonCode == "inspector-round-boundary");
        Assert.Contains(projection.Engine.EventJournal, item => item.EventType == nameof(GameEndedEvent)
            && item.OutcomeName == "inspector-outcome");
        Assert.DoesNotContain("secret-werewolf", projection.Engine.EventJournal.Select(item => item.ToString()));
        Assert.Contains(projection.Engine.PendingInputs, input => input.ParticipantId == "human-1" && input.Status == PendingInputStatus.Waiting.ToString());
        Assert.Single(projection.PromptEnvelopes);
        Assert.Equal("env-latest", Assert.Single(projection.PromptEnvelopes).EnvelopeId);
        Assert.Single(projection.PromptCursors);
        Assert.Single(projection.EventDeliveryCursors);
        Assert.Single(projection.AgentMemories);
    }

    private static GameRuntimeState CreateRuntime()
    {
        var gameId = new GameInstanceId("game-inspector");
        var module = new GameModuleDescriptor(
            new GameModuleId("test-game"),
            new GameModuleVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            "Inspector Test",
            new PlayerCountRange(2, 4),
            []);
        var human = ParticipantState.Human(new ParticipantId("human-1"), "Human");
        var agent = ParticipantState.Agent(new ParticipantId("agent-1"), "Agent");
        var state = RulesGameState.CreateNotStarted(gameId, module, 99, [human, agent]) with
        {
            Status = RulesGameStatus.WaitingForInput,
            Stage = new GameStageState(new GameStageId("vote"), "Vote", 2, true, false),
            PendingInputs =
            [
                new PendingInputState(
                    new PendingInputId("pending-human"),
                    new ParticipantId("human-1"),
                    new GameStageId("vote"),
                    "vote",
                    PendingInputStatus.Waiting,
                    [new LegalIntentOption("agent-1", "Vote Agent", "Vote for Agent.")]),
            ],
        };
        state = state with
        {
            EventJournal = state.EventJournal
                .Append(GameStartedEvent.Create(gameId, module.ModuleId, module.ModuleVersion, 99))
                .Append(PendingInputRequestedEvent.Create(
                    gameId,
                    new PendingInputId("pending-human"),
                    new ParticipantId("human-1"),
                    new GameStageId("vote"),
                    "vote"))
                .Append(PlayerChoiceSubmittedEvent.Create(
                    gameId,
                    new PendingInputId("pending-agent"),
                    new ParticipantId("agent-1"),
                    "secret-werewolf",
                    GameEventVisibility.PrivateToParticipant(new ParticipantId("agent-1"))))
                .Append(AgentResponseRejectedEvent.Create(
                    gameId,
                    new PendingInputId("pending-agent"),
                    new ParticipantId("agent-1"),
                    "parse-fail",
                    "The response was not JSON.",
                    GameEventVisibility.HiddenSystemOnly))
                .Append(NoActionTakenEvent.Create(
                    gameId,
                    new PendingInputId("pending-agent"),
                    new ParticipantId("agent-1"),
                    "parse-fail",
                    GameEventVisibility.HiddenSystemOnly))
                .Append(RoundEndedEvent.Create(gameId, 1, "inspector-round-boundary"))
                .Append(GameEndedEvent.Create(gameId, "inspector-outcome")),
        };

        return new GameRuntimeState
        {
            Status = GameRuntimeStatus.WaitingForInput,
            GameInstanceId = gameId.Value,
            TemplateId = "template-1",
            ModuleId = module.ModuleId.Value,
            ModuleVersion = module.ModuleVersion.Value,
            Seed = 99,
            EngineSnapshot = RulesGameStateSnapshot.FromState(state),
            ParticipantBindings =
            [
                new GameRuntimeParticipantBinding
                {
                    ParticipantId = "human-1",
                    DisplayName = "Human",
                    Kind = GameRuntimeParticipantKind.Human,
                    UserSeatId = "human-1",
                },
                new GameRuntimeParticipantBinding
                {
                    ParticipantId = "agent-1",
                    DisplayName = "Agent",
                    Kind = GameRuntimeParticipantKind.Agent,
                    ProviderAlias = "local",
                    ModelOverride = "model-a",
                },
            ],
            PromptCursors =
            [
                new GameRuntimeAgentPromptDeliveryCursor
                {
                    ParticipantId = "agent-1",
                    LastDeliveredPublicEngineEventSequence = 1,
                    DeliveredPrivateEventIds = [state.EventJournal.Events[2].EventId.ToString()],
                    CommunicationDeliveredThroughSequence = 2,
                    MemoryRevision = 1,
                    LastPromptEnvelopeId = "env-latest",
                },
            ],
            EventDeliveryCursors =
            [
                new GameRuntimeEventDeliveryCursor
                {
                    ParticipantId = "agent-1",
                    DeliveredThroughEngineEventSequence = 1,
                    DeliveredThroughCommunicationSequence = 2,
                    MemoryRevision = 1,
                    LastPromptEnvelopeId = "env-latest",
                },
            ],
            AgentMemories =
            [
                new GameRuntimeAgentMemoryState
                {
                    ParticipantId = "agent-1",
                    Revision = 1,
                    TokenBudget = 128,
                    Summary = "I only know what my private feed showed me.",
                    ContentHash = "hash-memory",
                    LastSummarizedRoundNumber = 1,
                    LastSummarizedPublicEngineEventSequence = 1,
                    LastSummarizedPrivateEventIds = [state.EventJournal.Events[2].EventId.ToString()],
                    LastSummarizedCommunicationSequence = 2,
                },
            ],
            PromptEnvelopes =
            [
                new GameRuntimeAgentPromptEnvelope
                {
                    EnvelopeId = "env-old",
                    ParticipantId = "agent-1",
                    CreatedAt = DateTimeOffset.Parse("2026-04-29T09:00:00+00:00"),
                    PromptText = "older prompt",
                },
                new GameRuntimeAgentPromptEnvelope
                {
                    EnvelopeId = "env-latest",
                    ParticipantId = "agent-1",
                    CreatedAt = DateTimeOffset.Parse("2026-04-29T09:01:00+00:00"),
                    EngineCursorSequence = 1,
                    CommunicationCursorSequence = 2,
                    MemoryRevision = 1,
                    ProviderAlias = "local",
                    Model = "model-a",
                    PromptTokens = 7,
                    ResponseTokens = 3,
                    PromptContentHash = "hash-prompt",
                    ResponseContentHash = "hash-response",
                    PromptText = "latest prompt text",
                    ResponseText = "latest response text",
                },
            ],
        };
    }

    private sealed class FakeGameRuntimeService : IGameRuntimeService
    {
        private readonly GameRuntimeState _runtime;

        public FakeGameRuntimeService(GameRuntimeState runtime)
        {
            _runtime = runtime;
        }

        public Task<GameRuntimeState?> LoadViewAsync(Guid sessionId, CancellationToken ct = default) => Task.FromResult<GameRuntimeState?>(_runtime);

        public Task<SessionMutationResult<GameRuntimeMutationResult>> StartAsync(Guid sessionId, StartGameRuntimeCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SessionMutationResult<GameRuntimeMutationResult>> ApplyEngineCommandAsync(Guid sessionId, ApplyGameRuntimeEngineCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SessionMutationResult<GameRuntimeMutationResult>> ResumeAsync(Guid sessionId, ResumeGameRuntimeCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SessionMutationResult<GameRuntimeMutationResult>> AbortAsync(Guid sessionId, AbortGameRuntimeCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SessionMutationResult<GameRuntimeMutationResult>> AppendHostRecordAsync(Guid sessionId, AppendGameRuntimeHostRecordCommand command, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<SessionMutationResult<GameRuntimeMutationResult>> AppendHostRecordsAsync(Guid sessionId, AppendGameRuntimeHostRecordsCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SessionMutationResult<GameRuntimeCommunicationMutationResult>> PostPublicMessageAsync(Guid sessionId, PostGameRuntimePublicMessageCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SessionMutationResult<GameRuntimeCommunicationMutationResult>> SendDirectMessageAsync(Guid sessionId, SendGameRuntimeDirectMessageCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SessionMutationResult<GameRuntimePromptMutationResult>> RecordAgentPromptAsync(Guid sessionId, RecordGameRuntimeAgentPromptCommand command, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<SessionMutationResult<GameRuntimeMemorySummaryMutationResult>> RecordAgentMemorySummaryAsync(Guid sessionId, RecordGameRuntimeAgentMemorySummaryCommand command, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
