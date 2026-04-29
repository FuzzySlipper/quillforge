using System.Reflection;
using Den.RulesEngine;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public sealed class GameAgentMemoryServiceTests
{
    [Fact]
    public async Task RunRoundEndMemorySummaries_DetectsRoundEndedFactsAndPersistsDecisionEnvelopeAndMemory()
    {
        var completion = new ScriptedCompletionService(request => SummaryJson($"remembered by {request.ProviderAlias}/{request.Model}"));
        var tracker = new InMemoryTokenUsageTracker(NullLogger<InMemoryTokenUsageTracker>.Instance);
        var fixture = CreateFixture(new UsageTrackingCompletionService(
            completion,
            tracker,
            NullLogger<UsageTrackingCompletionService>.Instance));
        var sessionId = Guid.NewGuid();
        await StartRuntimeAsync(fixture.Runtime, sessionId, singleAgent: true);
        await EndRoundAsync(fixture.Runtime, sessionId, Instant(1));

        var result = await fixture.Memory.RunRoundEndMemorySummariesAsync(
            sessionId,
            new RunGameAgentMemorySummariesCommand(Instant(2)));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        var participant = Assert.Single(result.Value!.ParticipantResults);
        Assert.Equal(GameAgentMemorySummaryOutcome.Recorded, participant.Outcome);
        Assert.Equal("provider-a", participant.ProviderAlias);
        Assert.Equal("model-a", participant.Model);
        Assert.True(Assert.Single(completion.Requests).CacheSystemPrompt);

        Assert.NotNull(result.Value.Game);
        var runtime = result.Value.Game!;
        var memory = Assert.Single(runtime.AgentMemories);
        Assert.Equal(1, memory.Revision);
        Assert.Equal(0, memory.LastSummarizedRoundNumber);
        Assert.Contains("provider-a/model-a", memory.Summary, StringComparison.Ordinal);
        Assert.NotNull(memory.ContentHash);
        Assert.Single(runtime.MemorySummaryDecisions);
        var decision = runtime.MemorySummaryDecisions.Single();
        Assert.Equal("agent-a", decision.ParticipantId);
        Assert.Equal(0, decision.RoundNumber);
        Assert.Equal("provider-a", decision.ProviderAlias);
        Assert.Equal("model-a", decision.Model);
        Assert.False(decision.Trimmed);
        Assert.NotNull(decision.SnapshotId);

        var envelope = Assert.Single(runtime.PromptEnvelopes);
        Assert.Equal("agent-a", envelope.ParticipantId);
        Assert.Contains("Prior memory summary", envelope.PromptText, StringComparison.Ordinal);
        Assert.Contains("remembered by provider-a/model-a", envelope.ResponseText, StringComparison.Ordinal);
        Assert.Contains(result.Value.RuntimeEvents, item => item is GameRuntimeAgentMemorySummaryRecordedEvent recorded
            && recorded.ParticipantId == "agent-a"
            && recorded.RoundNumber == 0);

        var usage = tracker.GetSessionUsage(sessionId);
        Assert.Contains(usage.ByAgent, item => item.AgentName == "game-memory:agent-a");
    }

    [Fact]
    public async Task RunRoundEndMemorySummaries_UsesOnlyEventsSinceLastMemoryCursor()
    {
        var completion = new ScriptedCompletionService(_ => SummaryJson("updated memory"));
        var fixture = CreateFixture(completion);
        var sessionId = Guid.NewGuid();
        await StartRuntimeAsync(fixture.Runtime, sessionId, singleAgent: true);
        await fixture.Runtime.PostPublicMessageAsync(
            sessionId,
            new PostGameRuntimePublicMessageCommand(Guid.NewGuid(), "human-1", ParticipantMessageAuthorKind.Human, "first public clue", Instant(1)));
        await EndRoundAsync(fixture.Runtime, sessionId, Instant(2));
        await fixture.Memory.RunRoundEndMemorySummariesAsync(sessionId, new RunGameAgentMemorySummariesCommand(Instant(3)));

        await fixture.Runtime.PostPublicMessageAsync(
            sessionId,
            new PostGameRuntimePublicMessageCommand(Guid.NewGuid(), "human-1", ParticipantMessageAuthorKind.Human, "second public clue", Instant(4)));
        await EndRoundAsync(fixture.Runtime, sessionId, Instant(5));
        await fixture.Memory.RunRoundEndMemorySummariesAsync(sessionId, new RunGameAgentMemorySummariesCommand(Instant(6), MaxSummaries: 1));

        Assert.Equal(2, completion.Requests.Count);
        var secondPrompt = completion.Requests[1].Messages[0].Content.GetText();
        Assert.DoesNotContain("first public clue", secondPrompt, StringComparison.Ordinal);
        Assert.Contains("second public clue", secondPrompt, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunRoundEndMemorySummaries_UsesPerAgentVisibleEventsAndDoesNotLeakHiddenFacts()
    {
        var completion = new ScriptedCompletionService(_ => SummaryJson("visible memory"));
        var fixture = CreateFixture(completion);
        var sessionId = Guid.NewGuid();
        await StartRuntimeAsync(fixture.Runtime, sessionId);
        await fixture.Runtime.SendDirectMessageAsync(
            sessionId,
            new SendGameRuntimeDirectMessageCommand(
                Guid.NewGuid(),
                "human-1",
                ParticipantMessageAuthorKind.Human,
                ["agent-a"],
                "private clue for agent a",
                Instant(1)));
        await RecordPrivateAndHiddenEventsAsync(fixture.Runtime, sessionId, Instant(2));
        await EndRoundAsync(fixture.Runtime, sessionId, Instant(3));

        await fixture.Memory.RunRoundEndMemorySummariesAsync(sessionId, new RunGameAgentMemorySummariesCommand(Instant(4)));

        Assert.Equal(2, completion.Requests.Count);
        var promptA = completion.Requests.Single(request => request.ProviderAlias == "provider-a").Messages[0].Content.GetText();
        var promptB = completion.Requests.Single(request => request.ProviderAlias == "provider-b").Messages[0].Content.GetText();
        Assert.Contains("private clue for agent a", promptA, StringComparison.Ordinal);
        Assert.DoesNotContain("private clue for agent a", promptB, StringComparison.Ordinal);
        Assert.Contains("AgentResponseRejectedEvent", promptA, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentResponseRejectedEvent", promptB, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden-system-secret", promptA, StringComparison.Ordinal);
        Assert.DoesNotContain("hidden-system-secret", promptB, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildMemorySummaryPrompt_AcceptsOnlyTypedVisibleEventsContext()
    {
        var method = typeof(GameAgentMemoryService).GetMethod(
            "BuildMemorySummaryPrompt",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        Assert.NotNull(method);
        var parameter = Assert.Single(method.GetParameters());
        Assert.Equal(typeof(GameAgentMemorySummaryPromptContext), parameter.ParameterType);
        Assert.DoesNotContain(method.GetParameters(), item => item.ParameterType == typeof(IGameEvent));
        Assert.DoesNotContain(method.GetParameters(), item => item.ParameterType == typeof(GameEventJournal));
        Assert.DoesNotContain(method.GetParameters(), item => item.ParameterType == typeof(RulesGameState));
    }

    [Fact]
    public async Task RunRoundEndMemorySummaries_AllowsDifferentModelsToRememberDifferently()
    {
        var completion = new ScriptedCompletionService(request => SummaryJson($"memory from {request.Model}"));
        var fixture = CreateFixture(completion);
        var sessionId = Guid.NewGuid();
        await StartRuntimeAsync(fixture.Runtime, sessionId);
        await EndRoundAsync(fixture.Runtime, sessionId, Instant(1));

        var result = await fixture.Memory.RunRoundEndMemorySummariesAsync(sessionId, new RunGameAgentMemorySummariesCommand(Instant(2)));

        var memories = result.Value!.Game!.AgentMemories.OrderBy(item => item.ParticipantId, StringComparer.Ordinal).ToArray();
        Assert.Equal("memory from model-a", memories[0].Summary);
        Assert.Equal("memory from default", memories[1].Summary);
    }

    [Fact]
    public async Task RunRoundEndMemorySummaries_PersistsRejectedDecisionWithReason()
    {
        var completion = new ScriptedCompletionService(_ => "not json");
        var fixture = CreateFixture(completion);
        var sessionId = Guid.NewGuid();
        await StartRuntimeAsync(fixture.Runtime, sessionId, singleAgent: true);
        await EndRoundAsync(fixture.Runtime, sessionId, Instant(1));

        var result = await fixture.Memory.RunRoundEndMemorySummariesAsync(sessionId, new RunGameAgentMemorySummariesCommand(Instant(2)));

        var participant = Assert.Single(result.Value!.ParticipantResults);
        Assert.Equal(GameAgentMemorySummaryOutcome.Rejected, participant.Outcome);
        Assert.Equal("parse-fail", participant.ReasonCode);
        var decision = Assert.Single(result.Value.Game!.MemorySummaryDecisions);
        Assert.Equal("parse-fail", decision.RejectionReason);
        Assert.Null(decision.SummaryContentHash);
        var memory = Assert.Single(result.Value.Game.AgentMemories);
        Assert.Equal(0, memory.Revision);
    }

    [Fact]
    public async Task RunRoundEndMemorySummaries_RecordsBudgetExceededAndTrimmedDecision()
    {
        var completion = new ScriptedCompletionService(_ => SummaryJson("one two three four five"), new TokenUsage(10, 5));
        var fixture = CreateFixture(completion, memoryTokenBudget: 3);
        var sessionId = Guid.NewGuid();
        await StartRuntimeAsync(fixture.Runtime, sessionId, singleAgent: true, memoryTokenBudget: 3);
        await EndRoundAsync(fixture.Runtime, sessionId, Instant(1));

        var result = await fixture.Memory.RunRoundEndMemorySummariesAsync(sessionId, new RunGameAgentMemorySummariesCommand(Instant(2)));

        var memory = Assert.Single(result.Value!.Game!.AgentMemories);
        Assert.Equal("one two three", memory.Summary);
        var decision = Assert.Single(result.Value.Game.MemorySummaryDecisions);
        Assert.True(decision.ExceededTokenBudget);
        Assert.True(decision.Trimmed);
        var participant = Assert.Single(result.Value.ParticipantResults);
        Assert.Equal("summary-trimmed", participant.ReasonCode);
    }

    [Fact]
    public async Task RunRoundEndMemorySummaries_TrimsAtWordBoundaryWhenBudgetCutsInsideLongWord()
    {
        var completion = new ScriptedCompletionService(_ => SummaryJson("alpha betagammadelta"), new TokenUsage(10, 20));
        var fixture = CreateFixture(completion, memoryTokenBudget: 10);
        var sessionId = Guid.NewGuid();
        await StartRuntimeAsync(fixture.Runtime, sessionId, singleAgent: true, memoryTokenBudget: 10);
        await EndRoundAsync(fixture.Runtime, sessionId, Instant(1));

        var result = await fixture.Memory.RunRoundEndMemorySummariesAsync(sessionId, new RunGameAgentMemorySummariesCommand(Instant(2)));

        var memory = Assert.Single(result.Value!.Game!.AgentMemories);
        Assert.Equal("alpha", memory.Summary);
        var decision = Assert.Single(result.Value.Game.MemorySummaryDecisions);
        Assert.True(decision.ExceededTokenBudget);
        Assert.True(decision.Trimmed);
    }

    [Fact]
    public async Task RunRoundEndMemorySummaries_UsesProviderReportedTokensInsteadOfWordCountProxy()
    {
        var summary = "one two three four five";
        var completion = new ScriptedCompletionService(_ => SummaryJson(summary), new TokenUsage(10, 3));
        var fixture = CreateFixture(completion, memoryTokenBudget: 3);
        var sessionId = Guid.NewGuid();
        await StartRuntimeAsync(fixture.Runtime, sessionId, singleAgent: true, memoryTokenBudget: 3);
        await EndRoundAsync(fixture.Runtime, sessionId, Instant(1));

        var result = await fixture.Memory.RunRoundEndMemorySummariesAsync(sessionId, new RunGameAgentMemorySummariesCommand(Instant(2)));

        var memory = Assert.Single(result.Value!.Game!.AgentMemories);
        Assert.Equal(summary, memory.Summary);
        var decision = Assert.Single(result.Value.Game.MemorySummaryDecisions);
        Assert.False(decision.ExceededTokenBudget);
        Assert.False(decision.Trimmed);
        var participant = Assert.Single(result.Value.ParticipantResults);
        Assert.Equal("recorded", participant.ReasonCode);
    }

    private static Fixture CreateFixture(ICompletionService completionService, int memoryTokenBudget = 128)
    {
        var registry = new GameModuleRegistry();
        var register = registry.Register(new MemoryTestModule(memoryTokenBudget));
        Assert.True(register.IsValid);
        var store = new InMemoryStateStore();
        var runtime = new GameRuntimeService(
            store,
            new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance),
            registry,
            new RulesEngineService(registry),
            new ParticipantChannelService(),
            new DefaultGameEventNarrationComposer(),
            NullLogger<GameRuntimeService>.Instance);
        var visibleEvents = new AgentVisibleEventsService(new GameVisibilityProjector(), new ParticipantChannelService());
        var memory = new GameAgentMemoryService(
            runtime,
            registry,
            completionService,
            visibleEvents,
            new AppConfig(),
            NullLogger<GameAgentMemoryService>.Instance);
        return new Fixture(runtime, memory, store);
    }

    private static async Task StartRuntimeAsync(
        IGameRuntimeService runtime,
        Guid sessionId,
        bool singleAgent = false,
        int memoryTokenBudget = 128)
    {
        var participants = new List<ParticipantSetup>
        {
            new(new ParticipantId("human-1"), "Human", ParticipantKind.Human),
            new(new ParticipantId("agent-a"), "Agent A", ParticipantKind.Agent),
        };
        var bindings = new List<GameRuntimeParticipantBinding>
        {
            new()
            {
                ParticipantId = "human-1",
                DisplayName = "Human",
                Kind = GameRuntimeParticipantKind.Human,
                UserSeatId = "human-1",
            },
            new()
            {
                ParticipantId = "agent-a",
                DisplayName = "Agent A",
                Kind = GameRuntimeParticipantKind.Agent,
                ProviderAlias = "provider-a",
                ModelOverride = "model-a",
                CharacterPrompt = "Remember like a careful analyst.",
                Personality = "cautious",
            },
        };
        if (!singleAgent)
        {
            participants.Add(new ParticipantSetup(new ParticipantId("agent-b"), "Agent B", ParticipantKind.Agent));
            bindings.Add(new GameRuntimeParticipantBinding
            {
                ParticipantId = "agent-b",
                DisplayName = "Agent B",
                Kind = GameRuntimeParticipantKind.Agent,
                ProviderAlias = "provider-b",
            });
        }

        var result = await runtime.StartAsync(
            sessionId,
            new StartGameRuntimeCommand(
                "memory-test-template",
                new GameInstanceId("game-memory-test"),
                new GameModuleId(MemoryTestModule.ModuleId),
                new GameModuleVersion(MemoryTestModule.ModuleVersion),
                42,
                new GameTemplateVersion("1.0.0"),
                GameSetup.Empty,
                participants,
                bindings,
                memoryTokenBudget,
                Instant(0)));
        Assert.Equal(SessionMutationStatus.Success, result.Status);
    }

    private static async Task EndRoundAsync(IGameRuntimeService runtime, Guid sessionId, DateTimeOffset occurredAt)
    {
        var view = await runtime.LoadViewAsync(sessionId);
        Assert.NotNull(view?.EngineSnapshot);
        var result = await runtime.ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(
                new EndRoundIntentCommand(GameIntentCommandId.NewId(), view.EngineSnapshot.GameInstanceId, "round-complete"),
                occurredAt));
        Assert.Equal(SessionMutationStatus.Success, result.Status);
    }

    private static async Task RecordPrivateAndHiddenEventsAsync(IGameRuntimeService runtime, Guid sessionId, DateTimeOffset occurredAt)
    {
        var view = await runtime.LoadViewAsync(sessionId);
        Assert.NotNull(view?.EngineSnapshot);
        var gameInstanceId = view.EngineSnapshot.GameInstanceId;
        var privateResult = await runtime.ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(
                new RecordAgentResponseRejectedIntentCommand(
                    GameIntentCommandId.NewId(),
                    gameInstanceId,
                    new PendingInputId("pending-agent-a"),
                    new ParticipantId("agent-a"),
                    "private-test",
                    "private-test",
                    GameEventVisibility.PrivateToParticipant(new ParticipantId("agent-a"))),
                occurredAt));
        Assert.Equal(SessionMutationStatus.Success, privateResult.Status);

        view = await runtime.LoadViewAsync(sessionId);
        Assert.NotNull(view?.EngineSnapshot);
        var hiddenResult = await runtime.ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(
                new RecordAgentResponseRejectedIntentCommand(
                    GameIntentCommandId.NewId(),
                    gameInstanceId,
                    new PendingInputId("pending-agent-a"),
                    new ParticipantId("agent-a"),
                    "hidden-system-secret",
                    "hidden-system-secret",
                    GameEventVisibility.HiddenSystemOnly),
                occurredAt.AddSeconds(1)));
        Assert.Equal(SessionMutationStatus.Success, hiddenResult.Status);
    }

    private static string SummaryJson(string summary) =>
        "{\"summary\":\"" + summary + "\"}";

    private static DateTimeOffset Instant(int minutes) =>
        DateTimeOffset.Parse("2026-04-27T12:00:00+00:00").AddMinutes(minutes);

    private sealed record Fixture(
        IGameRuntimeService Runtime,
        GameAgentMemoryService Memory,
        InMemoryStateStore Store);

    private sealed class ScriptedCompletionService : ICompletionService
    {
        private readonly Func<CompletionRequest, string> _handler;
        private readonly TokenUsage _usage;

        public ScriptedCompletionService(Func<CompletionRequest, string> handler, TokenUsage? usage = null)
        {
            _handler = handler;
            _usage = usage ?? new TokenUsage(10, 5);
        }

        public List<CompletionRequest> Requests { get; } = [];

        public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(new CompletionResponse
            {
                Content = new MessageContent(_handler(request)),
                StopReason = StopReason.EndTurn,
                Usage = _usage,
            });
        }

        public async IAsyncEnumerable<StreamEvent> StreamAsync(CompletionRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var response = await CompleteAsync(request, ct);
            yield return new TextDeltaEvent(response.Content.GetText());
            yield return new DoneEvent(response.StopReason, response.Usage);
        }
    }

    private sealed class InMemoryStateStore : ISessionStateStore
    {
        private readonly Dictionary<Guid, SessionState> _states = [];

        public Task<SessionState> LoadAsync(Guid? sessionId, CancellationToken ct = default)
        {
            if (sessionId is null)
            {
                return Task.FromResult(new SessionState());
            }

            if (!_states.TryGetValue(sessionId.Value, out var state))
            {
                state = new SessionState { SessionId = sessionId };
                _states[sessionId.Value] = state;
            }

            return Task.FromResult(state);
        }

        public Task SaveAsync(SessionState state, CancellationToken ct = default)
        {
            Assert.NotNull(state.SessionId);
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

    private sealed class MemoryTestModule : IGameModule
    {
        public const string ModuleId = "memory-test";
        public const string ModuleVersion = "1.0.0";
        private static readonly GameStageId ChoiceStageId = new("choice");
        private readonly int _memoryTokenBudget;

        public MemoryTestModule(int memoryTokenBudget)
        {
            _memoryTokenBudget = memoryTokenBudget;
        }

        public GameModuleDescriptor Descriptor => new(
            new GameModuleId(ModuleId),
            new GameModuleVersion(ModuleVersion),
            new GameTemplateVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            "Memory Test",
            new PlayerCountRange(2, 3),
            [])
        {
            CommunicationCapabilities = new GameCommunicationCapabilities(true, true),
            MemoryExpectations = new GameMemoryExpectations(true, _memoryTokenBudget, 8),
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
            var pending = context.Participants
                .Where(participant => participant.Kind == ParticipantKind.Agent)
                .Select(participant => new PendingInputState(
                    new PendingInputId($"pending-{participant.ParticipantId.Value}"),
                    participant.ParticipantId,
                    ChoiceStageId,
                    "choose",
                    PendingInputStatus.Waiting,
                    [new LegalIntentOption("approve", "Approve", "Approve the proposal.")]))
                .ToArray();

            return RulesGameState.CreateNotStarted(context.GameInstanceId, Descriptor, context.Seed, participants) with
            {
                Stage = new GameStageState(ChoiceStageId, "Choice", 1, true, true),
                PendingInputs = pending,
            };
        }

        public IReadOnlyList<LegalIntentDescriptor> GetLegalIntentDescriptors(RulesGameState state, ParticipantId participantId) => [];

        public GameModuleTransitionResult HandleIntentCommand(GameModuleTransitionContext context) =>
            GameModuleTransitionResult.Accepted(context.State, []);

        public IReadOnlyList<GameRuleHandlerDescriptor> GetRuleHandlerDescriptors() => [];

        public IReadOnlyList<GamePromptAsset> GetPromptAssets() =>
        [
            new GamePromptAsset("memory-rules", GamePromptAssetKind.RulesText, "Test memory rules reference."),
            new GamePromptAsset("memory-instructions", GamePromptAssetKind.ParticipantInstructions, "Remember only visible facts."),
        ];
    }
}
