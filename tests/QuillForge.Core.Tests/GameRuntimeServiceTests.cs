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
        Assert.Contains(result.Value.Game.Communication.GameEventLinks, link => link.Summary == "GameStartedEvent occurred.");

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
        Assert.Contains(result.Value.Game.Communication.GameEventLinks, link => link.Summary == "GameEndedEvent occurred.");

        var persisted = await store.LoadAsync(sessionId);
        Assert.Equal(RulesGameStatus.Ended, persisted.Game?.EngineSnapshot?.Status);
    }

    [Fact]
    public async Task SendDirectMessageAsync_RejectsWhenModuleCapabilitiesForbidDirectMessages()
    {
        var sessionId = Guid.NewGuid();
        var store = new InMemorySessionRuntimeStore();
        var service = CreateService(store, moduleAllowsDirectMessages: false);
        await service.StartAsync(sessionId, CreateStartCommand(hostAllowsDirectMessages: true));

        var result = await service.SendDirectMessageAsync(
            sessionId,
            new SendGameRuntimeDirectMessageCommand(
                Guid.NewGuid(),
                "human-1",
                ParticipantMessageAuthorKind.Human,
                ["agent-1"],
                "secret",
                DateTimeOffset.Parse("2026-04-27T12:20:00+00:00")));

        Assert.Equal(SessionMutationStatus.Invalid, result.Status);
        Assert.Contains("dm_forbidden", result.Error, StringComparison.Ordinal);

        var persisted = await store.LoadAsync(sessionId);
        Assert.Empty(persisted.Game!.Communication.DirectMessages);
    }

    private static GameRuntimeService CreateService(
        InMemorySessionRuntimeStore store,
        ISessionMutationGate? gate = null,
        bool moduleAllowsDirectMessages = true)
    {
        var registry = new GameModuleRegistry();
        var register = registry.Register(new TestModule(moduleAllowsDirectMessages));
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

    private static StartGameRuntimeCommand CreateStartCommand(bool hostAllowsDirectMessages = true) => new(
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
        DateTimeOffset.Parse("2026-04-27T11:00:00+00:00"),
        HostAllowsDirectMessages: hostAllowsDirectMessages);

    private sealed class TestModule : IGameModule
    {
        public TestModule(bool allowsDirectMessages)
        {
            Descriptor = CreateDescriptor(allowsDirectMessages);
        }

        public GameModuleDescriptor Descriptor { get; }

        private static GameModuleDescriptor CreateDescriptor(bool allowsDirectMessages) => new(
            new GameModuleId("test-game"),
            new GameModuleVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            "Test Game",
            new PlayerCountRange(2, 6),
            [])
        {
            CommunicationCapabilities = new GameCommunicationCapabilities(true, allowsDirectMessages),
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
            return RulesGameState.CreateNotStarted(context.GameInstanceId, context.Descriptor, context.Seed, participants) with
            {
                Stage = new GameStageState(new GameStageId("discussion"), "Discussion", 1, true, true),
            };
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
