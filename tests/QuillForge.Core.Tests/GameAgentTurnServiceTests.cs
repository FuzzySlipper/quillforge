using Den.RulesEngine;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public sealed class GameAgentTurnServiceTests
{
    [Fact]
    public async Task RunPendingAgentTurns_UsesConfiguredProviderAliasesAndModelOverrides()
    {
        var completion = new ScriptedCompletionService(request => AcceptedJsonForRequest(request, "approve"));
        var tracker = new InMemoryTokenUsageTracker(NullLogger<InMemoryTokenUsageTracker>.Instance);
        var fixture = CreateFixture(new UsageTrackingCompletionService(
            completion,
            tracker,
            NullLogger<UsageTrackingCompletionService>.Instance));
        var sessionId = Guid.NewGuid();
        await StartRuntimeAsync(fixture.Runtime, sessionId);

        var result = await fixture.AgentTurns.RunPendingAgentTurnsAsync(
            sessionId,
            new RunGameAgentTurnsCommand(Instant(1), MaxConcurrency: 1));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.Equal(2, result.Value!.ParticipantResults.Count);
        Assert.Equal(new[] { "provider-a", "provider-b" }, completion.Requests.Select(request => request.ProviderAlias).ToArray());
        Assert.Equal(new[] { "model-a", "default" }, completion.Requests.Select(request => request.Model).ToArray());
        Assert.All(completion.Requests, request => Assert.Empty(request.Tools!));

        var usage = tracker.GetSessionUsage(sessionId);
        Assert.Equal(2, usage.TotalRequests);
        Assert.Contains(usage.ByAgent, entry => entry.AgentName == "game-agent:agent-a" && entry.InputTokens == 10 && entry.OutputTokens == 5);
        Assert.Contains(usage.ByAgent, entry => entry.AgentName == "game-agent:agent-b" && entry.InputTokens == 10 && entry.OutputTokens == 5);
    }

    [Fact]
    public async Task RunPendingAgentTurns_AppliesCompletedResponsesInParticipantIdOrder()
    {
        var completion = new ScriptedCompletionService(request => AcceptedJsonForRequest(request, "approve"));
        var fixture = CreateFixture(completion);
        var sessionId = Guid.NewGuid();
        await StartRuntimeAsync(fixture.Runtime, sessionId);

        var result = await fixture.AgentTurns.RunPendingAgentTurnsAsync(
            sessionId,
            new RunGameAgentTurnsCommand(Instant(1), MaxConcurrency: 2));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        var submitted = result.Value!.EngineEvents.OfType<PlayerChoiceSubmittedEvent>().ToArray();
        Assert.Equal(["agent-a", "agent-b"], submitted.Select(item => item.ParticipantId.Value).ToArray());
    }

    [Fact]
    public async Task RunPendingAgentTurns_RejectsIllegalActionAndRecordsNoActionFact()
    {
        var completion = new ScriptedCompletionService(_ => AcceptedJson("invent-new-rule"));
        var fixture = CreateFixture(completion);
        var sessionId = Guid.NewGuid();
        await StartRuntimeAsync(fixture.Runtime, sessionId, singleAgent: true);

        var result = await fixture.AgentTurns.RunPendingAgentTurnsAsync(
            sessionId,
            new RunGameAgentTurnsCommand(Instant(1)));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        var participant = Assert.Single(result.Value!.ParticipantResults);
        Assert.Equal(GameAgentTurnOutcome.Rejected, participant.Outcome);
        Assert.Equal("illegal-action", participant.ReasonCode);
        Assert.Contains(result.Value.EngineEvents, item => item is AgentResponseRejectedEvent rejected
            && rejected.ParticipantId.Value == "agent-a"
            && rejected.ReasonCode == "illegal-action");
        Assert.Contains(result.Value.EngineEvents, item => item is NoActionTakenEvent noAction
            && noAction.ParticipantId.Value == "agent-a"
            && noAction.ReasonCode == "illegal-action");
    }

    [Fact]
    public async Task RunPendingAgentTurns_RecordsParseFailureAsAgentResponseRejected()
    {
        var completion = new ScriptedCompletionService(_ => "not json");
        var fixture = CreateFixture(completion);
        var sessionId = Guid.NewGuid();
        await StartRuntimeAsync(fixture.Runtime, sessionId, singleAgent: true);

        var result = await fixture.AgentTurns.RunPendingAgentTurnsAsync(
            sessionId,
            new RunGameAgentTurnsCommand(Instant(1)));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.Contains(result.Value!.EngineEvents, item => item is AgentResponseRejectedEvent rejected
            && rejected.ReasonCode == "parse-fail");
        Assert.Contains(result.Value.EngineEvents, item => item is NoActionTakenEvent noAction
            && noAction.ReasonCode == "parse-fail");
    }

    [Fact]
    public async Task RunPendingAgentTurns_HiddenInfoAttemptUsesHiddenSystemVisibility()
    {
        var completion = new ScriptedCompletionService(_ => RejectedJson("hidden-info-attempt", "Tried to use hidden information."));
        var fixture = CreateFixture(completion);
        var sessionId = Guid.NewGuid();
        await StartRuntimeAsync(fixture.Runtime, sessionId, singleAgent: true);

        var result = await fixture.AgentTurns.RunPendingAgentTurnsAsync(
            sessionId,
            new RunGameAgentTurnsCommand(Instant(1)));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        var participant = Assert.Single(result.Value!.ParticipantResults);
        Assert.Equal("hidden-info-attempt", participant.ReasonCode);
        var rejected = Assert.Single(result.Value.EngineEvents.OfType<AgentResponseRejectedEvent>());
        Assert.Equal(GameEventVisibilityKind.HiddenSystemOnly, rejected.Visibility.Kind);
        var noAction = Assert.Single(result.Value.EngineEvents.OfType<NoActionTakenEvent>());
        Assert.Equal("hidden-info-attempt", noAction.ReasonCode);
        Assert.Equal(GameEventVisibilityKind.HiddenSystemOnly, noAction.Visibility.Kind);

        var publicProjection = new GameVisibilityProjector().ProjectPublic(result.Value.Game!.EngineSnapshot!.ToState().EventJournal);
        Assert.DoesNotContain(publicProjection.Events, gameEvent => gameEvent.EventType == nameof(NoActionTakenEvent));
    }

    [Fact]
    public async Task RunPendingAgentTurns_MissingProviderAliasRecordsProviderLevelNoActionWithoutProviderCall()
    {
        var completion = new ScriptedCompletionService(_ => AcceptedJson("approve"));
        var fixture = CreateFixture(completion);
        var sessionId = Guid.NewGuid();
        await StartRuntimeAsync(fixture.Runtime, sessionId, singleAgent: true);
        fixture.Store.SetAgentProviderAlias(sessionId, "agent-a", " ");

        var result = await fixture.AgentTurns.RunPendingAgentTurnsAsync(
            sessionId,
            new RunGameAgentTurnsCommand(Instant(1)));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.Empty(completion.Requests);
        var participant = Assert.Single(result.Value!.ParticipantResults);
        Assert.Equal(GameAgentTurnOutcome.Rejected, participant.Outcome);
        Assert.Equal("provider-level-failure", participant.ReasonCode);
        Assert.Null(participant.ProviderAlias);
        Assert.Contains(result.Value.EngineEvents.OfType<AgentResponseRejectedEvent>(), rejected => rejected.ReasonCode == "provider-level-failure");
        Assert.Contains(result.Value.EngineEvents.OfType<NoActionTakenEvent>(), noAction => noAction.ReasonCode == "provider-level-failure");
    }

    [Fact]
    public async Task RunPendingAgentTurns_ProviderExceptionRecordsProviderLevelNoAction()
    {
        var completion = new ThrowingCompletionService(new InvalidOperationException("provider boom"));
        var fixture = CreateFixture(completion);
        var sessionId = Guid.NewGuid();
        await StartRuntimeAsync(fixture.Runtime, sessionId, singleAgent: true);

        var result = await fixture.AgentTurns.RunPendingAgentTurnsAsync(
            sessionId,
            new RunGameAgentTurnsCommand(Instant(1)));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.Single(completion.Requests);
        var participant = Assert.Single(result.Value!.ParticipantResults);
        Assert.Equal(GameAgentTurnOutcome.Rejected, participant.Outcome);
        Assert.Equal("provider-level-failure", participant.ReasonCode);
        Assert.Contains("provider boom", participant.Message, StringComparison.Ordinal);
        Assert.Contains(result.Value.EngineEvents.OfType<AgentResponseRejectedEvent>(), rejected => rejected.ReasonCode == "provider-level-failure");
        Assert.Contains(result.Value.EngineEvents.OfType<NoActionTakenEvent>(), noAction => noAction.ReasonCode == "provider-level-failure");
    }

    [Fact]
    public async Task RunPendingAgentTurns_ResponseTimeoutRecordsRetryExhaustionNoAction()
    {
        var completion = new TimeoutCompletionService();
        var fixture = CreateFixture(completion);
        var sessionId = Guid.NewGuid();
        await StartRuntimeAsync(fixture.Runtime, sessionId, singleAgent: true);

        var result = await fixture.AgentTurns.RunPendingAgentTurnsAsync(
            sessionId,
            new RunGameAgentTurnsCommand(Instant(1), ResponseTimeout: TimeSpan.FromMilliseconds(1)));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.Single(completion.Requests);
        var participant = Assert.Single(result.Value!.ParticipantResults);
        Assert.Equal(GameAgentTurnOutcome.Rejected, participant.Outcome);
        Assert.Equal("retry-exhaustion", participant.ReasonCode);
        Assert.Contains(result.Value.EngineEvents.OfType<AgentResponseRejectedEvent>(), rejected => rejected.ReasonCode == "retry-exhaustion");
        Assert.Contains(result.Value.EngineEvents.OfType<NoActionTakenEvent>(), noAction => noAction.ReasonCode == "retry-exhaustion");
    }

    [Fact]
    public async Task RunPendingAgentTurns_BuildsPromptFromRulesMemoryVisibleFeedAndPendingInput()
    {
        var completion = new ScriptedCompletionService(_ => AcceptedJson("approve"));
        var fixture = CreateFixture(completion);
        var sessionId = Guid.NewGuid();
        await StartRuntimeAsync(fixture.Runtime, sessionId, singleAgent: true);
        fixture.Store.SetAgentMemory(sessionId, "agent-a", "Remember to be cautious.");
        await fixture.Runtime.PostPublicMessageAsync(
            sessionId,
            new PostGameRuntimePublicMessageCommand(
                Guid.NewGuid(),
                "human-1",
                ParticipantMessageAuthorKind.Human,
                "Table talk visible to agents.",
                Instant(1)));

        var result = await fixture.AgentTurns.RunPendingAgentTurnsAsync(
            sessionId,
            new RunGameAgentTurnsCommand(Instant(2)));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        var request = Assert.Single(completion.Requests);
        var prompt = request.SystemPrompt + "\n" + request.Messages[0].Content.GetText();
        Assert.Contains("Test rules reference", prompt, StringComparison.Ordinal);
        Assert.Contains("Use only visible facts", prompt, StringComparison.Ordinal);
        Assert.Contains("Remember to be cautious", prompt, StringComparison.Ordinal);
        Assert.Contains("Table talk visible to agents", prompt, StringComparison.Ordinal);
        Assert.DoesNotContain("Game started with module", prompt, StringComparison.Ordinal);
        Assert.Contains("pending-agent-a", prompt, StringComparison.Ordinal);
        Assert.Contains("approve", prompt, StringComparison.Ordinal);
        Assert.Contains("must not invent or change rules", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(result.Value!.RuntimeEvents, item => item is GameRuntimeAgentPromptRecordedEvent);
    }

    private static Fixture CreateFixture(ICompletionService completionService)
    {
        var registry = new GameModuleRegistry();
        var register = registry.Register(new AgentTurnTestModule());
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
        var agentTurns = new GameAgentTurnService(
            runtime,
            registry,
            completionService,
            new AgentVisibleEventsService(new GameVisibilityProjector(), new ParticipantChannelService()),
            new AppConfig(),
            NullLogger<GameAgentTurnService>.Instance);
        return new Fixture(runtime, agentTurns, store);
    }

    private static async Task StartRuntimeAsync(
        IGameRuntimeService runtime,
        Guid sessionId,
        bool singleAgent = false)
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
                CharacterPrompt = "Careful analyst.",
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
                "agent-test-template",
                new GameInstanceId("game-agent-test"),
                new GameModuleId(AgentTurnTestModule.ModuleId),
                new GameModuleVersion(AgentTurnTestModule.ModuleVersion),
                42,
                new GameTemplateVersion("1.0.0"),
                GameSetup.Empty,
                participants,
                bindings,
                128,
                Instant(0)));
        Assert.Equal(SessionMutationStatus.Success, result.Status);
    }

    private static string AcceptedJsonForRequest(CompletionRequest request, string choiceName)
    {
        var prompt = request.Messages[0].Content.GetText();
        var participantId = prompt.Contains("agent-b", StringComparison.Ordinal) ? "agent-b" : "agent-a";
        return AcceptedJson(participantId, choiceName);
    }

    private static string AcceptedJson(string choiceName) =>
        AcceptedJson("agent-a", choiceName);

    private static string AcceptedJson(string participantId, string choiceName) =>
        "{\"accepted\":true,\"pendingInputId\":\"pending-" + participantId + "\",\"choiceName\":\"" + choiceName + "\",\"message\":\"ok\"}";

    private static string RejectedJson(string reasonCode, string message) =>
        "{\"accepted\":false,\"reasonCode\":\"" + reasonCode + "\",\"message\":\"" + message + "\"}";

    private static DateTimeOffset Instant(int minutes) =>
        DateTimeOffset.Parse("2026-04-27T12:00:00+00:00").AddMinutes(minutes);

    private sealed record Fixture(
        IGameRuntimeService Runtime,
        GameAgentTurnService AgentTurns,
        InMemoryStateStore Store);

    private sealed class ScriptedCompletionService : ICompletionService
    {
        private readonly Func<CompletionRequest, string> _handler;

        public ScriptedCompletionService(Func<CompletionRequest, string> handler)
        {
            _handler = handler;
        }

        public List<CompletionRequest> Requests { get; } = [];

        public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
        {
            lock (Requests)
            {
                Requests.Add(request);
            }
            var text = _handler(request);
            return Task.FromResult(new CompletionResponse
            {
                Content = new MessageContent(text),
                StopReason = StopReason.EndTurn,
                Usage = new TokenUsage(10, 5),
            });
        }

        public async IAsyncEnumerable<StreamEvent> StreamAsync(CompletionRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var response = await CompleteAsync(request, ct);
            yield return new TextDeltaEvent(response.Content.GetText());
            yield return new DoneEvent(response.StopReason, response.Usage);
        }
    }

    private sealed class ThrowingCompletionService : ICompletionService
    {
        private readonly Exception _exception;

        public ThrowingCompletionService(Exception exception)
        {
            _exception = exception;
        }

        public List<CompletionRequest> Requests { get; } = [];

        public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            throw _exception;
        }

        public async IAsyncEnumerable<StreamEvent> StreamAsync(CompletionRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var response = await CompleteAsync(request, ct);
            yield return new TextDeltaEvent(response.Content.GetText());
            yield return new DoneEvent(response.StopReason, response.Usage);
        }
    }

    private sealed class TimeoutCompletionService : ICompletionService
    {
        public List<CompletionRequest> Requests { get; } = [];

        public async Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            throw new InvalidOperationException("Timeout delay unexpectedly completed.");
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

        public void SetAgentMemory(Guid sessionId, string participantId, string summary)
        {
            var memory = _states[sessionId].Game!.AgentMemories.Single(item => item.ParticipantId == participantId);
            memory.Summary = summary;
            memory.Revision = 1;
        }

        public void SetAgentProviderAlias(Guid sessionId, string participantId, string? providerAlias)
        {
            var binding = _states[sessionId].Game!.ParticipantBindings.Single(item => item.ParticipantId == participantId);
            binding.ProviderAlias = providerAlias;
        }
    }

    private sealed class AgentTurnTestModule : IGameModule
    {
        public const string ModuleId = "agent-turn-test";
        public const string ModuleVersion = "1.0.0";
        private static readonly GameStageId ChoiceStageId = new("choice");

        public GameModuleDescriptor Descriptor { get; } = new(
            new GameModuleId(ModuleId),
            new GameModuleVersion(ModuleVersion),
            new GameTemplateVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            "Agent Turn Test",
            new PlayerCountRange(2, 3),
            [])
        {
            CommunicationCapabilities = new GameCommunicationCapabilities(true, true),
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
                    [
                        new LegalIntentOption("approve", "Approve", "Approve the proposal."),
                        new LegalIntentOption("reject", "Reject", "Reject the proposal."),
                    ]))
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
            new GamePromptAsset("test-rules", GamePromptAssetKind.RulesText, "Test rules reference."),
            new GamePromptAsset("test-instructions", GamePromptAssetKind.ParticipantInstructions, "Use only visible facts."),
        ];
    }
}
