using System.Text.RegularExpressions;
using Den.RulesEngine;
using Den.RulesEngine.Werewolf;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Services;

namespace QuillForge.ProviderHarness.Tests;

public sealed partial class HarnessGameScenarioRunner
{
    private const string DeterministicMode = "scripted-fake-completion";
    private const string DeterministicDescription = "Regression assertions are prompt-level deterministic: scripted fake completions produce exact agent action and memory responses. Optional live-provider exploratory runs are nondeterministic and should be judged by captured trace artifacts, not golden assertions.";

    private readonly HarnessRunArtifactStore _artifactStore;

    public HarnessGameScenarioRunner(HarnessRunArtifactStore artifactStore)
    {
        _artifactStore = artifactStore;
    }

    public async Task<HarnessGameScenarioReport> RunWerewolfVillageWinAsync(CancellationToken ct = default)
    {
        var scenarioName = "game-werewolf-village-win";
        var sessionId = Guid.NewGuid();
        var completion = new ScriptedGameCompletionService();
        var fixture = CreateFixture(completion, memoryTokenBudget: 64);
        var runtimeEvents = new List<IGameRuntimeEvent>();
        var actionResults = new List<GameAgentTurnParticipantResult>();
        var memoryResults = new List<GameAgentMemorySummaryParticipantResult>();

        var start = await fixture.Bridge.StartFromTemplateAsync(
            sessionId,
            new StartGameFromTemplateCommand("werewolf-harness-template", "Alice", 42, Instant(0)),
            ct);
        RequireSuccess(start.Status, start.Error, "start Werewolf harness game");
        runtimeEvents.AddRange(start.Value!.RuntimeEvents);

        await RequestInputsAsync(
            fixture.Runtime,
            sessionId,
            WerewolfConstants.NightStage.StageId,
            "night-action",
            [new LegalIntentOption(WerewolfConstants.SkipNightChoice, "Skip night", "No baseline night action.")],
            Instant(1),
            runtimeEvents,
            ct);
        await SubmitHumanPendingInputAsync(fixture.Bridge, sessionId, "alice", WerewolfConstants.SkipNightChoice, Instant(1).AddSeconds(30), runtimeEvents, ct);

        completion.RejectNextForParticipant("drew", "scripted-invalid-response");
        var night = await fixture.AgentTurns.RunPendingAgentTurnsAsync(
            sessionId,
            new RunGameAgentTurnsCommand(Instant(2), MaxConcurrency: 1),
            ct);
        RequireSuccess(night.Status, night.Error, "run night agent turns");
        actionResults.AddRange(night.Value!.ParticipantResults);
        runtimeEvents.AddRange(night.Value.RuntimeEvents);

        var afterNight = await fixture.Runtime.LoadViewAsync(sessionId, ct)
            ?? throw new InvalidOperationException("Game runtime disappeared after night actions.");
        var liveAfterNight = afterNight.EngineSnapshot!.ToState();
        var werewolfTarget = liveAfterNight.Participants
            .Where(participant => participant.IsActive)
            .Single(participant => participant.ParticipantSetIds.Contains(WerewolfConstants.WerewolfRoleSetId))
            .ParticipantId
            .Value;
        completion.VoteTargetParticipantId = werewolfTarget;

        await AdvanceToVotingAndRequestVotesAsync(
            fixture.Runtime,
            sessionId,
            liveAfterNight,
            Instant(3),
            runtimeEvents,
            ct);
        await SubmitHumanPendingInputAsync(fixture.Bridge, sessionId, "alice", werewolfTarget, Instant(3).AddSeconds(30), runtimeEvents, ct);

        var vote = await fixture.AgentTurns.RunPendingAgentTurnsAsync(
            sessionId,
            new RunGameAgentTurnsCommand(Instant(4), MaxConcurrency: 1),
            ct);
        RequireSuccess(vote.Status, vote.Error, "run voting agent turns");
        actionResults.AddRange(vote.Value!.ParticipantResults);
        runtimeEvents.AddRange(vote.Value.RuntimeEvents);

        var finalRuntime = await fixture.Runtime.LoadViewAsync(sessionId, ct)
            ?? throw new InvalidOperationException("Game runtime disappeared after voting.");
        var publicView = await fixture.Bridge.GetViewAsync(sessionId, null, ct);
        var playerViews = await CapturePlayerViewsAsync(fixture.Bridge, sessionId, finalRuntime, ct);
        var trace = HarnessGameTraceBuilder.FromRuntime(
            _artifactStore.RunId,
            scenarioName,
            sessionId,
            finalRuntime,
            publicView,
            playerViews,
            actionResults,
            memoryResults,
            runtimeEvents,
            DeterministicMode,
            DeterministicDescription,
            liveProviderRun: false);
        var report = new HarnessGameScenarioReport
        {
            ScenarioName = scenarioName,
            GameTrace = trace,
        };
        report = report with { PersistedReport = HarnessRunReportWriter.WriteGameReport(_artifactStore, report) };
        return report;
    }

    public async Task<HarnessGameScenarioReport> RunWerewolfExploratoryNightAsync(
        ICompletionService completionService,
        GameTemplate template,
        string scenarioName = "game-werewolf-live-exploratory-night",
        long seed = 42,
        CancellationToken ct = default)
    {
        var sessionId = Guid.NewGuid();
        var fixture = CreateFixture(completionService, template);
        var runtimeEvents = new List<IGameRuntimeEvent>();
        var actionResults = new List<GameAgentTurnParticipantResult>();
        var memoryResults = new List<GameAgentMemorySummaryParticipantResult>();

        var start = await fixture.Bridge.StartFromTemplateAsync(
            sessionId,
            new StartGameFromTemplateCommand(template.TemplateId, "Harness Human", seed, Instant(20)),
            ct);
        RequireSuccess(start.Status, start.Error, "start exploratory Werewolf game");
        runtimeEvents.AddRange(start.Value!.RuntimeEvents);

        await RequestInputsAsync(
            fixture.Runtime,
            sessionId,
            WerewolfConstants.NightStage.StageId,
            "night-action",
            [new LegalIntentOption(WerewolfConstants.SkipNightChoice, "Skip night", "No baseline night action.")],
            Instant(21),
            runtimeEvents,
            ct);
        if (!string.IsNullOrWhiteSpace(template.Roster.UserSeatParticipantId))
        {
            await SubmitHumanPendingInputAsync(
                fixture.Bridge,
                sessionId,
                template.Roster.UserSeatParticipantId,
                WerewolfConstants.SkipNightChoice,
                Instant(21).AddSeconds(30),
                runtimeEvents,
                ct);
        }

        var night = await fixture.AgentTurns.RunPendingAgentTurnsAsync(
            sessionId,
            new RunGameAgentTurnsCommand(Instant(22), MaxConcurrency: 1),
            ct);
        RequireSuccess(night.Status, night.Error, "run exploratory night agent turns");
        actionResults.AddRange(night.Value!.ParticipantResults);
        runtimeEvents.AddRange(night.Value.RuntimeEvents);

        var finalRuntime = await fixture.Runtime.LoadViewAsync(sessionId, ct)
            ?? throw new InvalidOperationException("Game runtime disappeared after exploratory night.");
        var publicView = await fixture.Bridge.GetViewAsync(sessionId, null, ct);
        var playerViews = await CapturePlayerViewsAsync(fixture.Bridge, sessionId, finalRuntime, ct);
        var trace = HarnessGameTraceBuilder.FromRuntime(
            _artifactStore.RunId,
            scenarioName,
            sessionId,
            finalRuntime,
            publicView,
            playerViews,
            actionResults,
            memoryResults,
            runtimeEvents,
            "live-provider-exploratory",
            "Live-provider exploratory game runs are nondeterministic; inspect the captured prompt, action, failure, and outcome trace rather than asserting a golden semantic result.",
            liveProviderRun: true);
        var report = new HarnessGameScenarioReport
        {
            ScenarioName = scenarioName,
            GameTrace = trace,
        };
        report = report with { PersistedReport = HarnessRunReportWriter.WriteGameReport(_artifactStore, report) };
        return report;
    }

    public async Task<HarnessGameScenarioReport> RunWerewolfAbortEdgeCaseAsync(CancellationToken ct = default)
    {
        var scenarioName = "game-werewolf-abort-edge-case";
        var sessionId = Guid.NewGuid();
        var completion = new ScriptedGameCompletionService();
        var fixture = CreateFixture(completion, memoryTokenBudget: 64);
        var runtimeEvents = new List<IGameRuntimeEvent>();
        var actionResults = new List<GameAgentTurnParticipantResult>();
        var memoryResults = new List<GameAgentMemorySummaryParticipantResult>();

        var start = await fixture.Bridge.StartFromTemplateAsync(
            sessionId,
            new StartGameFromTemplateCommand("werewolf-harness-template", "Alice", 42, Instant(30)),
            ct);
        RequireSuccess(start.Status, start.Error, "start Werewolf abort edge-case game");
        runtimeEvents.AddRange(start.Value!.RuntimeEvents);

        var startedRuntime = await fixture.Runtime.LoadViewAsync(sessionId, ct)
            ?? throw new InvalidOperationException("Game runtime disappeared before abort edge-case commands.");
        var invalidChoice = await fixture.Runtime.ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(
                new SubmitPlayerChoiceIntentCommand(
                    GameIntentCommandId.NewId(),
                    startedRuntime.EngineSnapshot!.GameInstanceId,
                    new PendingInputId("missing-pending-input"),
                    new ParticipantId("alice"),
                    WerewolfConstants.AbstainChoice),
                Instant(31)),
            ct);
        if (invalidChoice.Status != SessionMutationStatus.Invalid ||
            invalidChoice.Error?.StartsWith("unknown_pending_input", StringComparison.Ordinal) != true)
        {
            throw new InvalidOperationException($"Expected invalid player choice to be rejected as unknown_pending_input; got {invalidChoice.Status}: {invalidChoice.Error}");
        }

        var abort = await fixture.Bridge.AbortAsync(
            sessionId,
            new AbortGameRuntimeCommand(GameIntentCommandId.NewId(), "harness-abort-edge-case", Instant(32)),
            ct);
        RequireSuccess(abort.Status, abort.Error, "abort Werewolf edge-case game");
        runtimeEvents.AddRange(abort.Value!.RuntimeEvents);

        var finalRuntime = await fixture.Runtime.LoadViewAsync(sessionId, ct)
            ?? throw new InvalidOperationException("Game runtime disappeared after abort edge-case scenario.");
        var publicView = await fixture.Bridge.GetViewAsync(sessionId, null, ct);
        var playerViews = await CapturePlayerViewsAsync(fixture.Bridge, sessionId, finalRuntime, ct);
        var trace = HarnessGameTraceBuilder.FromRuntime(
            _artifactStore.RunId,
            scenarioName,
            sessionId,
            finalRuntime,
            publicView,
            playerViews,
            actionResults,
            memoryResults,
            runtimeEvents,
            DeterministicMode,
            DeterministicDescription,
            liveProviderRun: false);
        var report = new HarnessGameScenarioReport
        {
            ScenarioName = scenarioName,
            GameTrace = trace,
        };
        report = report with { PersistedReport = HarnessRunReportWriter.WriteGameReport(_artifactStore, report) };
        return report;
    }

    public async Task<HarnessGameScenarioReport> RunWerewolfMemoryAfterRoundAsync(CancellationToken ct = default)
    {
        var scenarioName = "game-werewolf-round-memory";
        var sessionId = Guid.NewGuid();
        var completion = new ScriptedGameCompletionService { MemorySummary = "I remember the night roles, the day table talk, and the public round boundary." };
        var fixture = CreateFixture(completion, memoryTokenBudget: 6);
        var runtimeEvents = new List<IGameRuntimeEvent>();
        var actionResults = new List<GameAgentTurnParticipantResult>();
        var memoryResults = new List<GameAgentMemorySummaryParticipantResult>();

        var start = await fixture.Bridge.StartFromTemplateAsync(
            sessionId,
            new StartGameFromTemplateCommand("werewolf-harness-template", "Alice", 42, Instant(10)),
            ct);
        RequireSuccess(start.Status, start.Error, "start Werewolf memory harness game");
        runtimeEvents.AddRange(start.Value!.RuntimeEvents);

        await SubmitHumanPendingInputAsync(fixture.Bridge, sessionId, "alice", WerewolfConstants.SkipNightChoice, Instant(11).AddSeconds(30), runtimeEvents, ct);

        var night = await fixture.AgentTurns.RunPendingAgentTurnsAsync(
            sessionId,
            new RunGameAgentTurnsCommand(Instant(12), MaxConcurrency: 1),
            ct);
        RequireSuccess(night.Status, night.Error, "run night agent turns for memory scenario");
        actionResults.AddRange(night.Value!.ParticipantResults);
        runtimeEvents.AddRange(night.Value.RuntimeEvents);

        var publicMessage = await fixture.Runtime.PostPublicMessageAsync(
            sessionId,
            new PostGameRuntimePublicMessageCommand(
                Guid.Parse("00000000-0000-0000-0000-000000000846"),
                "alice",
                ParticipantMessageAuthorKind.Human,
                "We should compare notes carefully before voting.",
                Instant(13)),
            ct);
        RequireSuccess(publicMessage.Status, publicMessage.Error, "post public table-talk message");

        var runtime = await fixture.Runtime.LoadViewAsync(sessionId, ct)
            ?? throw new InvalidOperationException("Game runtime disappeared before round-end memory.");
        var round = await fixture.Runtime.ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(
                new EndRoundIntentCommand(GameIntentCommandId.NewId(), runtime.EngineSnapshot!.GameInstanceId, "harness-round-boundary"),
                Instant(14)),
            ct);
        RequireSuccess(round.Status, round.Error, "record round boundary");
        runtimeEvents.AddRange(round.Value!.RuntimeEvents);

        var memory = await fixture.Memory.RunRoundEndMemorySummariesAsync(
            sessionId,
            new RunGameAgentMemorySummariesCommand(Instant(15)),
            ct);
        RequireSuccess(memory.Status, memory.Error, "run round-end memory summaries");
        memoryResults.AddRange(memory.Value!.ParticipantResults);
        runtimeEvents.AddRange(memory.Value.RuntimeEvents);

        var finalRuntime = await fixture.Runtime.LoadViewAsync(sessionId, ct)
            ?? throw new InvalidOperationException("Game runtime disappeared after memory summaries.");
        var publicView = await fixture.Bridge.GetViewAsync(sessionId, null, ct);
        var playerViews = await CapturePlayerViewsAsync(fixture.Bridge, sessionId, finalRuntime, ct);
        var trace = HarnessGameTraceBuilder.FromRuntime(
            _artifactStore.RunId,
            scenarioName,
            sessionId,
            finalRuntime,
            publicView,
            playerViews,
            actionResults,
            memoryResults,
            runtimeEvents,
            DeterministicMode,
            DeterministicDescription,
            liveProviderRun: false);
        var report = new HarnessGameScenarioReport
        {
            ScenarioName = scenarioName,
            GameTrace = trace,
        };
        report = report with { PersistedReport = HarnessRunReportWriter.WriteGameReport(_artifactStore, report) };
        return report;
    }

    private static Fixture CreateFixture(ICompletionService completion, int memoryTokenBudget) =>
        CreateFixture(completion, CreateWerewolfHarnessTemplate(memoryTokenBudget));

    private static Fixture CreateFixture(ICompletionService completion, GameTemplate template)
    {
        var registryResult = new GameModuleRegistryFactory().Create([new WerewolfModule()]);
        if (!registryResult.ValidationResult.IsValid)
        {
            throw new InvalidOperationException(registryResult.ValidationResult.Issues[0].Message);
        }

        var channel = new ParticipantChannelService();
        var store = new InMemoryStateStore();
        var narration = new WerewolfGameEventNarrationComposer();
        var runtime = new GameRuntimeService(
            store,
            new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance),
            registryResult.Registry,
            new RulesEngineService(registryResult.Registry),
            channel,
            narration,
            NullLogger<GameRuntimeService>.Instance);
        var visibleEvents = new AgentVisibleEventsService(new GameVisibilityProjector(), channel);
        var appConfig = new AppConfig();
        var agentTurns = new GameAgentTurnService(
            runtime,
            registryResult.Registry,
            completion,
            visibleEvents,
            new DefaultOnlyGamePromptTemplateService(),
            new DefaultOnlyGamePersonaPromptService(),
            appConfig,
            NullLogger<GameAgentTurnService>.Instance);
        var memory = new GameAgentMemoryService(
            runtime,
            registryResult.Registry,
            completion,
            visibleEvents,
            new DefaultOnlyGamePersonaPromptService(),
            appConfig,
            NullLogger<GameAgentMemoryService>.Instance);
        var bridge = new GameBridgeService(
            new StaticTemplateService(template),
            runtime,
            registryResult.Registry,
            new RejectedTranslationAgent(),
            agentTurns,
            channel,
            new GameVisibilityProjector(),
            narration,
            NullLogger<GameBridgeService>.Instance);
        return new Fixture(runtime, bridge, agentTurns, memory);
    }

    internal static GameTemplate CreateWerewolfHarnessTemplate(int memoryTokenBudget) => new()
    {
        TemplateId = "werewolf-harness-template",
        DisplayName = "Werewolf Harness Template",
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
            RosterSize = 4,
            UserSeatParticipantId = "alice",
            AgentPlayers =
            [
                new GameTemplateAgentPlayerConfig { ParticipantId = "bob", ProviderAlias = "scripted-beta", ModelOverride = "beta-model", FixedName = "Bob", CharacterPrompt = "Skeptical questioner.", Personality = "skeptical" },
                new GameTemplateAgentPlayerConfig { ParticipantId = "carol", ProviderAlias = "scripted-gamma", ModelOverride = "gamma-model", FixedName = "Carol", CharacterPrompt = "Direct voter.", Personality = "direct" },
                new GameTemplateAgentPlayerConfig { ParticipantId = "drew", ProviderAlias = "scripted-delta", ModelOverride = "delta-model", FixedName = "Drew", CharacterPrompt = "Erratic smoke-test participant.", Personality = "erratic" },
            ],
        },
        Memory = new GameTemplateMemorySettings { TokenBudget = memoryTokenBudget },
        Communication = new GameTemplateCommunicationSettings { PublicChannelEnabled = true, DirectMessagesEnabled = true },
    };

    private static async Task RequestInputsAsync(
        IGameRuntimeService runtime,
        Guid sessionId,
        GameStageId stageId,
        string intentName,
        IReadOnlyList<LegalIntentOption> options,
        DateTimeOffset occurredAt,
        List<IGameRuntimeEvent> runtimeEvents,
        CancellationToken ct)
    {
        var view = await runtime.LoadViewAsync(sessionId, ct)
            ?? throw new InvalidOperationException("Game runtime is not available.");
        var result = await runtime.ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(
                new RequestPendingInputIntentCommand(
                    GameIntentCommandId.NewId(),
                    view.EngineSnapshot!.GameInstanceId,
                    stageId,
                    intentName,
                    options,
                    PendingInputAudience.AllActiveParticipants),
                occurredAt),
            ct);
        RequireSuccess(result.Status, result.Error, $"request {intentName} inputs");
        runtimeEvents.AddRange(result.Value!.RuntimeEvents);
    }

    private static async Task SubmitHumanPendingInputAsync(
        IGameBridgeService bridge,
        Guid sessionId,
        string participantId,
        string choiceName,
        DateTimeOffset occurredAt,
        List<IGameRuntimeEvent> runtimeEvents,
        CancellationToken ct)
    {
        var view = await bridge.GetViewAsync(sessionId, participantId, ct);
        var pending = view.Player?.PendingInputs.FirstOrDefault();
        if (pending is null)
        {
            throw new InvalidOperationException($"No pending input is available for human participant '{participantId}'.");
        }

        var result = await bridge.SubmitTypedActionAsync(
            sessionId,
            new SubmitGameTypedActionCommand(participantId, pending.PendingInputId.Value, choiceName, occurredAt),
            ct);
        RequireSuccess(result.Status, result.Error, $"submit human choice for {participantId}");
        runtimeEvents.AddRange(result.Value!.RuntimeEvents);
    }

    private static async Task AdvanceToVotingAndRequestVotesAsync(
        IGameRuntimeService runtime,
        Guid sessionId,
        RulesGameState liveState,
        DateTimeOffset occurredAt,
        List<IGameRuntimeEvent> runtimeEvents,
        CancellationToken ct)
    {
        var advance = await runtime.ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(
                new AdvanceStageIntentCommand(GameIntentCommandId.NewId(), liveState.GameInstanceId, WerewolfConstants.VotingStage),
                occurredAt),
            ct);
        RequireSuccess(advance.Status, advance.Error, "advance to voting");
        runtimeEvents.AddRange(advance.Value!.RuntimeEvents);

        var refreshed = (await runtime.LoadViewAsync(sessionId, ct))!.EngineSnapshot!.ToState();
        var activeIds = refreshed.Participants.Where(participant => participant.IsActive).Select(participant => participant.ParticipantId).ToArray();
        await RequestInputsAsync(
            runtime,
            sessionId,
            WerewolfConstants.VotingStage.StageId,
            "vote",
            activeIds
                .Select(id => new LegalIntentOption(id.Value, $"Vote {id.Value}", $"Vote to eliminate {id.Value}."))
                .Append(new LegalIntentOption(WerewolfConstants.AbstainChoice, "Abstain", "Do not eliminate anyone."))
                .ToArray(),
            occurredAt.AddSeconds(1),
            runtimeEvents,
            ct);
    }

    private static async Task<IReadOnlyDictionary<string, GameBridgeView>> CapturePlayerViewsAsync(
        IGameBridgeService bridge,
        Guid sessionId,
        GameRuntimeState runtime,
        CancellationToken ct)
    {
        var views = new Dictionary<string, GameBridgeView>(StringComparer.Ordinal);
        foreach (var binding in runtime.ParticipantBindings.OrderBy(item => item.ParticipantId, StringComparer.Ordinal))
        {
            views[binding.ParticipantId] = await bridge.GetViewAsync(sessionId, binding.ParticipantId, ct);
        }

        return views;
    }

    private static void RequireSuccess(SessionMutationStatus status, string? error, string operation)
    {
        if (status != SessionMutationStatus.Success)
        {
            throw new InvalidOperationException($"Failed to {operation}: {error}");
        }
    }

    private static DateTimeOffset Instant(int minutes) =>
        DateTimeOffset.Parse("2026-04-28T18:00:00+00:00").AddMinutes(minutes);

    private sealed record Fixture(
        IGameRuntimeService Runtime,
        IGameBridgeService Bridge,
        IGameAgentTurnService AgentTurns,
        IGameAgentMemoryService Memory);

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

    private sealed class RejectedTranslationAgent : IGameIntentTranslationAgent
    {
        public Task<GameIntentTranslationResult> TranslateAsync(GameIntentTranslationRequest request, CancellationToken ct = default) =>
            Task.FromResult(GameIntentTranslationResult.Rejected("not_used", "Harness submits typed or scripted actions."));
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
            if (state.SessionId is null)
            {
                throw new InvalidOperationException("Session state must have a session id before save.");
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

    private sealed partial class ScriptedGameCompletionService : ICompletionService
    {
        private readonly HashSet<string> _rejectOnce = new(StringComparer.Ordinal);

        public string? VoteTargetParticipantId { get; set; }

        public string MemorySummary { get; set; } = "I remember the visible round facts.";

        public List<CompletionRequest> Requests { get; } = [];

        public void RejectNextForParticipant(string participantId, string localDiscriminator)
        {
            _rejectOnce.Add($"{participantId}:{localDiscriminator}");
        }

        public Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            var userPrompt = request.Messages.Count == 0 ? string.Empty : request.Messages[0].Content.GetText();
            var participantId = ExtractParticipantId(userPrompt);
            var content = (request.SystemPrompt ?? string.Empty).Contains("update imperfect private memory", StringComparison.OrdinalIgnoreCase)
                ? MemoryJson(MemorySummary)
                : ActionJson(userPrompt, participantId);

            return Task.FromResult(new CompletionResponse
            {
                Content = new MessageContent(content),
                StopReason = StopReason.EndTurn,
                Usage = UsageFor(request.ProviderAlias, request.Model),
            });
        }

        public async IAsyncEnumerable<StreamEvent> StreamAsync(
            CompletionRequest request,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
        {
            var response = await CompleteAsync(request, ct);
            yield return new TextDeltaEvent(response.Content.GetText());
            yield return new DoneEvent(response.StopReason, response.Usage);
        }

        private string ActionJson(string prompt, string participantId)
        {
            var rejected = _rejectOnce.FirstOrDefault(item => item.StartsWith(participantId + ":", StringComparison.Ordinal));
            if (rejected is not null)
            {
                _rejectOnce.Remove(rejected);
                return "not json from scripted harness";
            }

            var pendingInputId = ExtractPendingInputId(prompt);
            var choice = prompt.Contains($"- {WerewolfConstants.SkipNightChoice}:", StringComparison.Ordinal)
                ? WerewolfConstants.SkipNightChoice
                : VoteTargetParticipantId ?? WerewolfConstants.AbstainChoice;
            return "{\"accepted\":true,\"pendingInputId\":\"" + pendingInputId + "\",\"choiceName\":\"" + choice + "\",\"message\":\"scripted harness choice\"}";
        }

        private static string ExtractParticipantId(string prompt)
        {
            var match = ParticipantLineRegex().Match(prompt);
            return match.Success ? match.Groups[1].Value : "unknown";
        }

        private static string ExtractPendingInputId(string prompt)
        {
            var match = PendingInputLineRegex().Match(prompt);
            return match.Success ? match.Groups[1].Value : "missing-pending-input";
        }

        private static string MemoryJson(string summary) =>
            "{\"summary\":\"" + summary.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal) + "\"}";

        private static TokenUsage UsageFor(string? providerAlias, string? model)
        {
            var input = Math.Max(1, (providerAlias?.Length ?? 3) + 20);
            var output = Math.Max(1, (model?.Length ?? 4) + 5);
            return new TokenUsage(input, output);
        }

        [GeneratedRegex(@"Participant: .+ \(([^)]+)\)")]
        private static partial Regex ParticipantLineRegex();

        [GeneratedRegex(@"pendingInputId: ([^\r\n]+)")]
        private static partial Regex PendingInputLineRegex();
    }
}
