using Den.RulesEngine;
using Den.RulesEngine.Werewolf;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Contracts;
using QuillForge.Web.Services;

namespace QuillForge.Architecture.Tests;

public sealed class WerewolfUiScenarioTests
{
    [Fact]
    public async Task GameViewResponse_NormalBridgePath_ProjectsPopulatedModuleAuthoring()
    {
        var fixture = CreateFixture();
        var sessionId = Guid.NewGuid();

        var start = await fixture.Bridge.StartFromTemplateAsync(
            sessionId,
            new StartGameFromTemplateCommand("werewolf-test-template", "Human", 42, Instant(0)));

        Assert.Equal(SessionMutationStatus.Success, start.Status);
        var response = new GameViewResponse { View = start.Value!.View };

        Assert.NotNull(response.View.ModuleAuthoring);
        Assert.Contains(response.View.ModuleAuthoring!.Stages, stage => stage.StageId == "night" && stage.DisplayName == "Night");
        Assert.Contains(response.View.ModuleAuthoring.ActionForms, form =>
            form.IntentName == "night-action" && form.Fields.Any(field => field.ValueKind == "ChoiceName"));
        Assert.True(response.View.ModuleAuthoring.ProjectionCapabilities.SupportsPublicEventProjection);
        Assert.True(response.View.ModuleAuthoring.ProjectionCapabilities.SupportsParticipantPrivateProjection);
    }

    [Fact]
    public async Task StartThenNightPublicMessageRejection_IsVisibleInDiagnosticLog()
    {
        var fixture = CreateFixture();
        var sessionId = Guid.NewGuid();

        var start = await fixture.Bridge.StartFromTemplateAsync(
            sessionId,
            new StartGameFromTemplateCommand("werewolf-test-template", "Human", 42, Instant(0)));

        Assert.Equal(SessionMutationStatus.Success, start.Status);
        Assert.Equal("night", start.Value!.View.StageId);

        var post = await fixture.Bridge.PostPublicMessageAsync(
            sessionId,
            new PostGameRuntimePublicMessageCommand(
                Guid.CreateVersion7(),
                "human-1",
                ParticipantMessageAuthorKind.Human,
                "Is anyone there?",
                Instant(1)));

        Assert.Equal(SessionMutationStatus.Invalid, post.Status);
        Assert.StartsWith("public_channel_forbidden:", post.Error, StringComparison.Ordinal);

        var diagnosticLog = new GameDiagnosticLogService(
            fixture.Runtime,
            new InMemoryTokenUsageTracker(NullLogger<InMemoryTokenUsageTracker>.Instance));
        var log = await diagnosticLog.GetLogAsync(sessionId);
        var snapshot = Assert.Single(log.Events, item => item.Operation == "runtime_snapshot");
        Assert.Equal("night", snapshot.Details["stageId"]);
        Assert.Equal("0", snapshot.Details["waitingInputCount"]);
        Assert.Contains(log.Events, item =>
            item.Operation == "runtime_waiting_without_pending_inputs"
            && item.Level == GameDiagnosticLogLevel.Warning
            && item.Details["stageId"] == "night");
        Assert.Contains(log.Events, item =>
            item.Category == GameDiagnosticLogCategory.Rejection
            && item.ReasonCode == "public_channel_forbidden"
            && item.Summary.Contains("Public channel message rejected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WerewolfBridge_Playthrough_ProjectsRoleStageVoteAndOutcomeWithoutLeakingRoles()
    {
        var fixture = CreateFixture();
        var sessionId = Guid.NewGuid();

        var start = await fixture.Bridge.StartFromTemplateAsync(
            sessionId,
            new StartGameFromTemplateCommand("werewolf-test-template", "Human", 42, Instant(0)));

        Assert.Equal(SessionMutationStatus.Success, start.Status);
        Assert.Equal("werewolf", start.Value!.View.ModuleId);
        Assert.Equal("night", start.Value.View.StageId);
        Assert.Contains(start.Value.View.Player!.Feed, entry => entry.Summary?.StartsWith("Your role is", StringComparison.Ordinal) == true);
        Assert.DoesNotContain(start.Value.View.Public.Feed, entry => entry.Summary?.StartsWith("Your role is", StringComparison.Ordinal) == true);

        var gameId = new GameInstanceId(start.Value.View.GameInstanceId!);
        await RequestInputsAsync(fixture.Runtime, sessionId, gameId, WerewolfConstants.NightStage.StageId, "night", [new LegalIntentOption(WerewolfConstants.SkipNightChoice, "Skip night", "No baseline night action.")], Instant(1));
        foreach (var input in (await fixture.Bridge.GetViewAsync(sessionId, "human-1")).Player!.PendingInputs.ToArray())
        {
            await fixture.Bridge.SubmitTypedActionAsync(sessionId, new SubmitGameTypedActionCommand(input.ParticipantId.Value, input.PendingInputId.Value.ToString(), WerewolfConstants.SkipNightChoice, Instant(2)));
        }
        foreach (var participantId in new[] { "agent-a", "agent-b" })
        {
            var view = await fixture.Bridge.GetViewAsync(sessionId, participantId);
            foreach (var input in view.Player!.PendingInputs.ToArray())
            {
                await fixture.Bridge.SubmitTypedActionAsync(sessionId, new SubmitGameTypedActionCommand(participantId, input.PendingInputId.Value.ToString(), WerewolfConstants.SkipNightChoice, Instant(2)));
            }
        }

        var day = await fixture.Bridge.GetViewAsync(sessionId, "human-1");
        Assert.Equal("day-discussion", day.StageId);
        Assert.Contains(day.Public.Narration, entry => entry.Text.Contains("Day discussion begins", StringComparison.Ordinal));

        await fixture.Runtime.ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(
                new AdvanceStageIntentCommand(GameIntentCommandId.NewId(), gameId, WerewolfConstants.VotingStage),
                Instant(3)));
        var runtime = (await fixture.Runtime.LoadViewAsync(sessionId))!;
        var live = runtime.EngineSnapshot!.ToState();
        var activeIds = live.Participants.Where(participant => participant.IsActive).Select(participant => participant.ParticipantId).ToArray();
        var target = activeIds.First(participant => live.FindParticipant(participant)!.ParticipantSetIds.Contains(WerewolfConstants.WerewolfRoleSetId));
        await RequestInputsAsync(
            fixture.Runtime,
            sessionId,
            gameId,
            WerewolfConstants.VotingStage.StageId,
            "vote",
            activeIds.Select(id => new LegalIntentOption(id.Value, $"Vote {id.Value}", $"Vote to eliminate {id.Value}.")).Append(new LegalIntentOption(WerewolfConstants.AbstainChoice, "Abstain", "Do not eliminate anyone.")).ToArray(),
            Instant(4));

        foreach (var participantId in activeIds.Select(id => id.Value))
        {
            var view = await fixture.Bridge.GetViewAsync(sessionId, participantId);
            var pending = Assert.Single(view.Player!.PendingInputs);
            Assert.Contains(pending.LegalOptions, option => option.IntentName == target.Value);
            await fixture.Bridge.SubmitTypedActionAsync(sessionId, new SubmitGameTypedActionCommand(participantId, pending.PendingInputId.Value.ToString(), target.Value, Instant(5)));
        }

        var ended = await fixture.Bridge.GetViewAsync(sessionId, "human-1");
        Assert.Equal(GameRuntimeStatus.Ended, ended.Status);
        Assert.Contains(ended.Public.Narration, entry => entry.Text.Contains("Villagers win", StringComparison.Ordinal));
        Assert.Contains(ended.Public.Narration, entry => entry.Text.Contains("Game ended", StringComparison.Ordinal));
    }

    private static async Task RequestInputsAsync(
        IGameRuntimeService runtime,
        Guid sessionId,
        GameInstanceId gameId,
        GameStageId stageId,
        string intentName,
        IReadOnlyList<LegalIntentOption> options,
        DateTimeOffset occurredAt)
    {
        var result = await runtime.ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(
                new RequestPendingInputIntentCommand(
                    GameIntentCommandId.NewId(),
                    gameId,
                    stageId,
                    intentName,
                    options,
                    PendingInputAudience.AllActiveParticipants),
                occurredAt));
        Assert.Equal(SessionMutationStatus.Success, result.Status);
    }

    private static Fixture CreateFixture()
    {
        var registryResult = new GameModuleRegistryFactory().Create([new WerewolfModule()]);
        Assert.True(registryResult.ValidationResult.IsValid);
        var store = new InMemoryStateStore();
        var channel = new ParticipantChannelService();
        var narration = new WerewolfGameEventNarrationComposer();
        var runtime = new GameRuntimeService(
            store,
            new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance),
            registryResult.Registry,
            new RulesEngineService(registryResult.Registry),
            channel,
            narration,
            NullLogger<GameRuntimeService>.Instance);
        var bridge = new GameBridgeService(
            new StaticTemplateService(CreateTemplate()),
            runtime,
            registryResult.Registry,
            new ScriptedTranslationAgent(),
            channel,
            new GameVisibilityProjector(),
            narration,
            NullLogger<GameBridgeService>.Instance);
        return new Fixture(runtime, bridge);
    }

    private static GameTemplate CreateTemplate() => new()
    {
        TemplateId = "werewolf-test-template",
        DisplayName = "Werewolf Test Template",
        TemplateVersion = "1.0.0",
        Module = new GameTemplateModuleSelection
        {
            ModuleId = WerewolfModuleAssemblyMarker.ModuleId.Value,
            MinimumVersion = WerewolfModuleAssemblyMarker.ModuleVersion.Value,
            MaximumVersion = WerewolfModuleAssemblyMarker.ModuleVersion.Value,
        },
        RulesOptions = new GameTemplateRulesOptions
        {
            Values =
            [
                new GameTemplateRuleOptionValue { Name = WerewolfConstants.WerewolfCountSetupField, Kind = GameTemplateRuleOptionValueKind.Int, IntValue = 1 },
                new GameTemplateRuleOptionValue { Name = WerewolfConstants.SeerEnabledSetupField, Kind = GameTemplateRuleOptionValueKind.Bool, BoolValue = false },
                new GameTemplateRuleOptionValue { Name = WerewolfConstants.OneNightCompatibleSetupField, Kind = GameTemplateRuleOptionValueKind.Bool, BoolValue = false },
            ],
        },
        Roster = new GameTemplateRosterSettings
        {
            RosterSize = 3,
            UserSeatParticipantId = "human-1",
            AgentPlayers =
            [
                new GameTemplateAgentPlayerConfig { ParticipantId = "agent-a", ProviderAlias = "fake", FixedName = "Agent A" },
                new GameTemplateAgentPlayerConfig { ParticipantId = "agent-b", ProviderAlias = "fake", FixedName = "Agent B" },
            ],
        },
        Memory = new GameTemplateMemorySettings { TokenBudget = 128 },
        Communication = new GameTemplateCommunicationSettings { PublicChannelEnabled = true, DirectMessagesEnabled = true },
    };

    private static DateTimeOffset Instant(int minutes) =>
        DateTimeOffset.Parse("2026-04-28T12:00:00+00:00").AddMinutes(minutes);

    private sealed record Fixture(IGameRuntimeService Runtime, IGameBridgeService Bridge);

    private sealed class StaticTemplateService : IGameTemplateService
    {
        private readonly GameTemplate _template;

        public StaticTemplateService(GameTemplate template)
        {
            _template = template;
        }

        public Task<IReadOnlyList<GameTemplateSummary>> ListAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<GameTemplateSummary>>([]);

        public Task<GameTemplateValidationEnvelope> LoadAsync(string templateId, CancellationToken ct = default) => Task.FromResult(new GameTemplateValidationEnvelope
        {
            Template = _template,
            Validation = GameTemplateValidationResult.Valid,
        });

        public Task<GameTemplateValidationEnvelope> SaveAsync(string templateId, GameTemplate template, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GameTemplateValidationEnvelope> CloneAsync(string sourceTemplateId, string targetTemplateId, string? displayName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task DeleteAsync(string templateId, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<GameTemplateValidationResult> ValidateAsync(GameTemplate template, CancellationToken ct = default) => Task.FromResult(GameTemplateValidationResult.Valid);
    }

    private sealed class ScriptedTranslationAgent : IGameIntentTranslationAgent
    {
        public Task<GameIntentTranslationResult> TranslateAsync(GameIntentTranslationRequest request, CancellationToken ct = default) =>
            Task.FromResult(GameIntentTranslationResult.Rejected("not_used", "This scenario submits typed actions."));
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

        public Task<IReadOnlyList<Guid>> FindSessionIdsByProfileIdAsync(string profileId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<Guid>>([]);
    }
}
