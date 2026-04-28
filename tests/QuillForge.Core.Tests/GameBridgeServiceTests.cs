using Den.RulesEngine;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public sealed class GameBridgeServiceTests
{
    [Fact]
    public async Task StartFromTemplate_StartsRuntimeAndProjectsNarrationWithoutHiddenEvents()
    {
        var fixture = CreateFixture();
        var sessionId = Guid.NewGuid();

        var result = await fixture.Bridge.StartFromTemplateAsync(
            sessionId,
            new StartGameFromTemplateCommand("test-template", "Human Player", 42, Instant(0)));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        var view = result.Value!.View;
        Assert.Equal(GameRuntimeStatus.Running, view.Status);
        Assert.Equal("test-template", view.TemplateId);
        Assert.NotNull(view.GameInstanceId);
        Assert.Contains(view.Public.Narration, entry => entry.EventType == nameof(GameStartedEvent));
        Assert.DoesNotContain(view.Public.Narration, entry => entry.EventType == nameof(DeterministicEffectsAdvancedEvent));
        Assert.Contains(view.Public.Feed, entry => entry.Kind == ParticipantFeedEntryKind.GameEventLink
            && entry.Summary == "GameStartedEvent occurred.");
        Assert.DoesNotContain(view.Public.Feed, entry => entry.Summary == "DeterministicEffectsAdvancedEvent occurred.");
        Assert.NotNull(view.Player);
        Assert.Equal("human-1", view.Player!.ParticipantId);
        Assert.Contains(view.Player.PendingInputs, input => input.PendingInputId.Value == TestGameModule.PendingInputId);
    }

    [Fact]
    public async Task SubmitTypedAction_AppliesUserChoice()
    {
        var fixture = CreateFixture();
        var sessionId = Guid.NewGuid();
        await fixture.Bridge.StartFromTemplateAsync(
            sessionId,
            new StartGameFromTemplateCommand("test-template", "Human Player", 42, Instant(0)));

        var result = await fixture.Bridge.SubmitTypedActionAsync(
            sessionId,
            new SubmitGameTypedActionCommand("human-1", TestGameModule.PendingInputId, "approve", Instant(1)));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.Contains(result.Value!.EngineEvents, gameEvent => gameEvent is PlayerChoiceSubmittedEvent submitted
            && submitted.ParticipantId.Value == "human-1"
            && submitted.ChoiceName == "approve");
        Assert.Contains(result.Value.View.Player!.Feed, entry => entry.Kind == ParticipantFeedEntryKind.GameEventLink
            && entry.Summary == "PlayerChoiceSubmittedEvent occurred.");
        Assert.DoesNotContain(result.Value.View.Public.Feed, entry => entry.Summary == "PlayerChoiceSubmittedEvent occurred.");

        var agentView = await fixture.Bridge.GetViewAsync(sessionId, "agent-1");
        Assert.DoesNotContain(agentView.Player!.Feed, entry => entry.Summary == "PlayerChoiceSubmittedEvent occurred.");
    }

    [Fact]
    public async Task SubmitTypedAction_ReturnsInvalidForIllegalChoice()
    {
        var fixture = CreateFixture();
        var sessionId = Guid.NewGuid();
        await fixture.Bridge.StartFromTemplateAsync(
            sessionId,
            new StartGameFromTemplateCommand("test-template", "Human Player", 42, Instant(0)));

        var result = await fixture.Bridge.SubmitTypedActionAsync(
            sessionId,
            new SubmitGameTypedActionCommand("human-1", TestGameModule.PendingInputId, "invent-new-rule", Instant(1)));

        Assert.Equal(SessionMutationStatus.Invalid, result.Status);
        Assert.Contains("illegal_choice", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SubmitTextAction_UsesDedicatedTranslatorAndRejectsHelpfulRuleChanges()
    {
        var translator = new ScriptedTranslationAgent(GameIntentTranslationResult.Rejected(
            "out_of_scope_helpful_interpretation",
            "Translator refuses to change game structure."));
        var fixture = CreateFixture(translator);
        var sessionId = Guid.NewGuid();
        await fixture.Bridge.StartFromTemplateAsync(
            sessionId,
            new StartGameFromTemplateCommand("test-template", "Human Player", 42, Instant(0)));

        var result = await fixture.Bridge.SubmitTextActionAsync(
            sessionId,
            new SubmitGameTextActionCommand("human-1", "Help me by changing the rules so I win.", Instant(1)));

        Assert.Equal(SessionMutationStatus.Invalid, result.Status);
        Assert.Contains("out_of_scope_helpful_interpretation", result.Error, StringComparison.Ordinal);
        Assert.Single(translator.Requests);
        Assert.Equal("human-1", translator.Requests[0].ParticipantId);
    }

    [Fact]
    public async Task SubmitTextAction_AcceptsTranslatorChoiceWhenItMatchesPendingInput()
    {
        var translator = new ScriptedTranslationAgent(GameIntentTranslationResult.Accepted(
            TestGameModule.PendingInputId,
            "reject",
            0.95,
            "faithful parse"));
        var fixture = CreateFixture(translator);
        var sessionId = Guid.NewGuid();
        await fixture.Bridge.StartFromTemplateAsync(
            sessionId,
            new StartGameFromTemplateCommand("test-template", "Human Player", 42, Instant(0)));

        var result = await fixture.Bridge.SubmitTextActionAsync(
            sessionId,
            new SubmitGameTextActionCommand("human-1", "I reject it.", Instant(1)));

        Assert.Equal(SessionMutationStatus.Success, result.Status);
        Assert.Contains(result.Value!.EngineEvents, gameEvent => gameEvent is PlayerChoiceSubmittedEvent submitted
            && submitted.ChoiceName == "reject");
    }

    [Fact]
    public async Task PublicAndDirectMessages_AreProjectedByVisibility()
    {
        var fixture = CreateFixture();
        var sessionId = Guid.NewGuid();
        await fixture.Bridge.StartFromTemplateAsync(
            sessionId,
            new StartGameFromTemplateCommand("test-template", "Human Player", 42, Instant(0)));

        await fixture.Bridge.PostPublicMessageAsync(
            sessionId,
            new PostGameRuntimePublicMessageCommand(Guid.NewGuid(), "human-1", ParticipantMessageAuthorKind.Human, "hello table", Instant(1)));
        await fixture.Bridge.SendDirectMessageAsync(
            sessionId,
            new SendGameRuntimeDirectMessageCommand(Guid.NewGuid(), "human-1", ParticipantMessageAuthorKind.Human, ["agent-1"], "secret", Instant(2)));

        var publicView = await fixture.Bridge.GetViewAsync(sessionId);
        var agentView = await fixture.Bridge.GetViewAsync(sessionId, "agent-1");
        var otherView = await fixture.Bridge.GetViewAsync(sessionId, "human-1");

        Assert.Contains(publicView.Public.Feed, entry => entry.Text == "hello table");
        Assert.DoesNotContain(publicView.Public.Feed, entry => entry.Text == "secret");
        Assert.Contains(agentView.Player!.Feed, entry => entry.Text == "secret");
        Assert.Contains(otherView.Player!.Feed, entry => entry.Text == "secret");

        var mixedFeed = agentView.Player.Feed
            .Where(entry => entry.Kind is ParticipantFeedEntryKind.GameEventLink
                or ParticipantFeedEntryKind.PublicChannelMessage
                or ParticipantFeedEntryKind.DirectMessage)
            .ToArray();
        Assert.Equal(mixedFeed.Select(entry => entry.Sequence).Order().ToArray(), mixedFeed.Select(entry => entry.Sequence).ToArray());
        Assert.Contains(mixedFeed, entry => entry.Kind == ParticipantFeedEntryKind.GameEventLink);
        Assert.Contains(mixedFeed, entry => entry.Kind == ParticipantFeedEntryKind.PublicChannelMessage);
        Assert.Contains(mixedFeed, entry => entry.Kind == ParticipantFeedEntryKind.DirectMessage);
    }

    [Fact]
    public void GameIntentTranslationAgentPrompt_IsTranslationOnlyAndNotOrchestratorLike()
    {
        var prompt = GameIntentTranslationAgent.BuildSystemPrompt();

        Assert.Contains("translation-only", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not the game master", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must not decide outcomes", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Return only compact JSON", prompt, StringComparison.Ordinal);
    }

    private static Fixture CreateFixture(IGameIntentTranslationAgent? translationAgent = null)
    {
        var registry = new GameModuleRegistry();
        var register = registry.Register(new TestGameModule());
        Assert.True(register.IsValid);

        var store = new InMemoryStateStore();
        var runtime = new GameRuntimeService(
            store,
            new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance),
            registry,
            new RulesEngineService(registry),
            new ParticipantChannelService(),
            NullLogger<GameRuntimeService>.Instance);
        var channel = new ParticipantChannelService();
        var bridge = new GameBridgeService(
            new StaticTemplateService(CreateTemplate()),
            runtime,
            registry,
            translationAgent ?? new ScriptedTranslationAgent(GameIntentTranslationResult.Accepted(TestGameModule.PendingInputId, "approve", 0.9, "parsed")),
            channel,
            new GameVisibilityProjector(),
            NullLogger<GameBridgeService>.Instance);
        return new Fixture(bridge, store);
    }

    private static GameTemplate CreateTemplate() => new()
    {
        TemplateId = "test-template",
        DisplayName = "Test Template",
        TemplateVersion = "1.0.0",
        Module = new GameTemplateModuleSelection
        {
            ModuleId = TestGameModule.ModuleId,
            MinimumVersion = TestGameModule.ModuleVersion,
            MaximumVersion = TestGameModule.ModuleVersion,
        },
        Roster = new GameTemplateRosterSettings
        {
            RosterSize = 2,
            UserSeatParticipantId = "human-1",
            AgentPlayers =
            [
                new GameTemplateAgentPlayerConfig
                {
                    ParticipantId = "agent-1",
                    ProviderAlias = "local",
                    FixedName = "Agent One",
                },
            ],
        },
        Memory = new GameTemplateMemorySettings { TokenBudget = 128 },
    };

    private static DateTimeOffset Instant(int minutes) =>
        DateTimeOffset.Parse("2026-04-27T12:00:00+00:00").AddMinutes(minutes);

    private sealed record Fixture(GameBridgeService Bridge, InMemoryStateStore Store);

    private sealed class ScriptedTranslationAgent : IGameIntentTranslationAgent
    {
        private readonly GameIntentTranslationResult _result;

        public ScriptedTranslationAgent(GameIntentTranslationResult result)
        {
            _result = result;
        }

        public List<GameIntentTranslationRequest> Requests { get; } = [];

        public Task<GameIntentTranslationResult> TranslateAsync(
            GameIntentTranslationRequest request,
            CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(_result);
        }
    }

    private sealed class StaticTemplateService : IGameTemplateService
    {
        private readonly GameTemplate _template;

        public StaticTemplateService(GameTemplate template)
        {
            _template = template;
        }

        public Task<IReadOnlyList<GameTemplateSummary>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<GameTemplateSummary>>([]);

        public Task<GameTemplateValidationEnvelope> LoadAsync(string templateId, CancellationToken ct = default) =>
            Task.FromResult(new GameTemplateValidationEnvelope
            {
                Template = _template,
                Validation = GameTemplateValidationResult.Valid,
            });

        public Task<GameTemplateValidationEnvelope> SaveAsync(string templateId, GameTemplate template, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<GameTemplateValidationEnvelope> CloneAsync(string sourceTemplateId, string targetTemplateId, string? displayName, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string templateId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<GameTemplateValidationResult> ValidateAsync(GameTemplate template, CancellationToken ct = default) =>
            Task.FromResult(GameTemplateValidationResult.Valid);
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

    private sealed class TestGameModule : IGameModule
    {
        public const string ModuleId = "test-bridge-game";
        public const string ModuleVersion = "1.0.0";
        public const string PendingInputId = "pending-human-choice";

        private static readonly GameStageId ChoiceStageId = new("choice");

        public GameModuleDescriptor Descriptor { get; } = new(
            new GameModuleId(ModuleId),
            new GameModuleVersion(ModuleVersion),
            new GameTemplateVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            "Test Bridge Game",
            new PlayerCountRange(2, 2),
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
            var pending = new PendingInputState(
                new PendingInputId(PendingInputId),
                new ParticipantId("human-1"),
                ChoiceStageId,
                "choice",
                PendingInputStatus.Waiting,
                [
                    new LegalIntentOption("approve", "Approve", "Approve the proposal."),
                    new LegalIntentOption("reject", "Reject", "Reject the proposal."),
                ]);
            return RulesGameState.CreateNotStarted(context.GameInstanceId, Descriptor, context.Seed, participants) with
            {
                Stage = new GameStageState(ChoiceStageId, "Choice", 1, true, true),
                PendingInputs = [pending],
            };
        }

        public IReadOnlyList<LegalIntentDescriptor> GetLegalIntentDescriptors(RulesGameState state, ParticipantId participantId) => [];

        public GameModuleTransitionResult HandleIntentCommand(GameModuleTransitionContext context)
        {
            if (context.Command is StartGameIntentCommand && context.Phase == RulesResolutionPhase.OnRun)
            {
                return GameModuleTransitionResult.Accepted(
                    context.State,
                    [DeterministicEffectsAdvancedEvent.Create(context.State.GameInstanceId, "hidden-setup")]);
            }

            return GameModuleTransitionResult.Accepted(context.State, []);
        }

        public IReadOnlyList<GameRuleHandlerDescriptor> GetRuleHandlerDescriptors() => [];

        public IReadOnlyList<GamePromptAsset> GetPromptAssets() => [];
    }
}
