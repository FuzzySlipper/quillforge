using Den.RulesEngine;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public sealed class GameRuntimeServiceTests
{
    [Fact]
    public async Task StartAsync_PersistsGameRuntimeStateAndInitializesBindings()
    {
        var sessionId = Guid.NewGuid();
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);

        var result = await service.StartAsync(sessionId, CreateStartCommand());

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.NotNull(result.Value);
        Assert.Equal(GameRuntimeStatus.Running, result.Value.Game.Status);
        Assert.Equal("test-game", result.Value.Game.ModuleId);
        Assert.Equal("1.0.0", result.Value.Game.ModuleVersion);
        Assert.Equal(1234, result.Value.Game.Seed);
        Assert.Equal(2, result.Value.Game.ParticipantBindings.Count);
        Assert.Single(result.Value.Game.AgentMemories);
        Assert.Single(result.Value.Game.PromptCursors);
        Assert.Equal(2, result.Value.Game.EventDeliveryCursors.Count);
        Assert.Equal(2, result.Value.Game.Communication.Participants.Count);
        Assert.Contains(result.Value.EngineEvents, gameEvent => gameEvent is GameStartedEvent);

        var persisted = await store.LoadAsync(sessionId);
        Assert.NotNull(persisted.Game?.EngineSnapshot);
        Assert.Equal("game-001", persisted.Game.GameInstanceId);
        Assert.Contains(persisted.Game.HostRecords, record => record.Kind == GameRuntimeHostRecordKind.Started);
    }

    [Fact]
    public async Task ResumeAsync_RecordsResumeWithoutChangingEngineSnapshot()
    {
        var sessionId = Guid.NewGuid();
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);
        await service.StartAsync(sessionId, CreateStartCommand());

        var resumedAt = DateTimeOffset.Parse("2026-04-27T12:00:00+00:00");
        var result = await service.ResumeAsync(sessionId, new ResumeGameRuntimeCommand(resumedAt));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.Equal(resumedAt, result.Value!.Game.LastResumedAt);
        Assert.Contains(result.Value.Game.HostRecords, record => record.Kind == GameRuntimeHostRecordKind.Resumed);
        Assert.Contains(result.Value.RuntimeEvents, gameEvent => gameEvent is GameRuntimeResumedEvent);
        Assert.Contains(result.Value.Game.EngineSnapshot!.EventJournal.Events, gameEvent => gameEvent is GameStartedEvent);
    }

    [Fact]
    public async Task AbortAsync_AppliesEngineAbortAndMarksRuntimeAborted()
    {
        var sessionId = Guid.NewGuid();
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);
        await service.StartAsync(sessionId, CreateStartCommand());

        var abortedAt = DateTimeOffset.Parse("2026-04-27T12:05:00+00:00");
        var result = await service.AbortAsync(
            sessionId,
            new AbortGameRuntimeCommand(GameIntentCommandId.NewId(), "user_aborted", abortedAt));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.Equal(GameRuntimeStatus.Aborted, result.Value!.Game.Status);
        Assert.Equal(abortedAt, result.Value.Game.EndedAt);
        Assert.Contains(result.Value.EngineEvents, gameEvent => gameEvent is GameAbortedEvent);
        Assert.Contains(result.Value.RuntimeEvents, gameEvent => gameEvent is GameRuntimeAbortedEvent);

        var persisted = await store.LoadAsync(sessionId);
        Assert.Equal(GameRuntimeStatus.Aborted, persisted.Game?.Status);
    }

    [Fact]
    public async Task ApplyEngineCommandAsync_RejectsOverlappingSameSessionEngineCommandAsBusy()
    {
        var sessionId = Guid.NewGuid();
        var store = new InMemorySessionRuntimeStore();
        var gate = new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance);
        var service = CreateService(store, gate);
        await service.StartAsync(sessionId, CreateStartCommand());

        await using var heldLease = await gate.TryAcquireAsync(sessionId, "held_by_other_game_mutation");
        Assert.NotNull(heldLease);

        var result = await service.ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(
                new EndGameIntentCommand(GameIntentCommandId.NewId(), new GameInstanceId("game-001"), "test_outcome"),
                DateTimeOffset.Parse("2026-04-27T12:10:00+00:00")));

        Assert.Equal(SessionMutationStatus.Busy, result.Status);
    }

    [Fact]
    public async Task ApplyEngineCommandAsync_UpdatesSnapshotThroughServiceBoundary()
    {
        var sessionId = Guid.NewGuid();
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store);
        await service.StartAsync(sessionId, CreateStartCommand());

        var result = await service.ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(
                new EndGameIntentCommand(GameIntentCommandId.NewId(), new GameInstanceId("game-001"), "test_outcome"),
                DateTimeOffset.Parse("2026-04-27T12:15:00+00:00")));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.Equal(GameRuntimeStatus.Ended, result.Value!.Game.Status);
        Assert.Contains(result.Value.EngineEvents, gameEvent => gameEvent is GameEndedEvent);

        var persisted = await store.LoadAsync(sessionId);
        Assert.Equal(RulesGameStatus.Ended, persisted.Game?.EngineSnapshot?.Status);
    }

    private static GameRuntimeService CreateService(
        InMemorySessionRuntimeStore store,
        ISessionMutationGate? gate = null)
    {
        var registry = new GameModuleRegistry();
        var register = registry.Register(new TestModule());
        Assert.True(register.IsValid);
        var rulesEngine = new RulesEngineService(registry);
        return new GameRuntimeService(
            store,
            gate ?? new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance),
            registry,
            rulesEngine,
            new ParticipantChannelService(),
            NullLogger<GameRuntimeService>.Instance);
    }

    private static StartGameRuntimeCommand CreateStartCommand() => new(
        "test-template",
        new GameInstanceId("game-001"),
        new GameModuleId("test-game"),
        new GameModuleVersion("1.0.0"),
        1234,
        new GameTemplateVersion("1.0.0"),
        GameSetup.Empty,
        [
            new ParticipantSetup(new ParticipantId("human-1"), "Human", ParticipantKind.Human),
            new ParticipantSetup(new ParticipantId("agent-1"), "Agent", ParticipantKind.Agent),
        ],
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
        512,
        DateTimeOffset.Parse("2026-04-27T11:00:00+00:00"));

    private sealed class TestModule : IGameModule
    {
        public GameModuleDescriptor Descriptor { get; } = new(
            new GameModuleId("test-game"),
            new GameModuleVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            "Test Game",
            new PlayerCountRange(2, 6),
            [])
        {
            ParticipantRequirements = new GameParticipantRequirements(true, true, false, 1, 1),
        };

        public ValidationResult ValidateSetup(GameSetupValidationContext context) => ValidationResult.Valid;

        public RulesGameState CreateInitialState(GameSetupInitializationContext context)
        {
            var participants = context.Participants
                .Select(participant => new ParticipantState(
                    participant.ParticipantId,
                    participant.DisplayName,
                    participant.Kind,
                    []))
                .ToArray();
            return RulesGameState.CreateNotStarted(context.GameInstanceId, context.Descriptor, context.Seed, participants);
        }

        public IReadOnlyList<LegalIntentDescriptor> GetLegalIntentDescriptors(RulesGameState state, ParticipantId participantId) => [];

        public GameModuleTransitionResult HandleIntentCommand(GameModuleTransitionContext context) =>
            GameModuleTransitionResult.Accepted(context.State, []);

        public IReadOnlyList<GameRuleHandlerDescriptor> GetRuleHandlerDescriptors() => [];

        public IReadOnlyList<GamePromptAsset> GetPromptAssets() => [];
    }

    private sealed class InMemorySessionRuntimeStore : ISessionStateStore
    {
        private readonly Dictionary<Guid, SessionState> _states = new();

        public Task<SessionState> LoadAsync(Guid? sessionId, CancellationToken ct = default)
        {
            if (!sessionId.HasValue)
            {
                return Task.FromResult(new SessionState());
            }

            if (!_states.TryGetValue(sessionId.Value, out var state))
            {
                return Task.FromResult(new SessionState { SessionId = sessionId.Value });
            }

            return Task.FromResult(state);
        }

        public Task SaveAsync(SessionState state, CancellationToken ct = default)
        {
            if (!state.SessionId.HasValue)
            {
                throw new InvalidOperationException("Session id is required.");
            }

            _states[state.SessionId.Value] = state;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(Guid sessionId, CancellationToken ct = default)
        {
            _states.Remove(sessionId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<Guid>> FindSessionIdsByProfileIdAsync(string profileId, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
