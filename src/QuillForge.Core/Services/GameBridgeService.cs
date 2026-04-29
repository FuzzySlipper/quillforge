using Den.RulesEngine;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class GameBridgeService : IGameBridgeService
{
    private readonly IGameTemplateService _templateService;
    private readonly IGameRuntimeService _runtimeService;
    private readonly GameModuleRegistry _moduleRegistry;
    private readonly IGameIntentTranslationAgent _translationAgent;
    private readonly ParticipantChannelService _channelService;
    private readonly GameVisibilityProjector _visibilityProjector;
    private readonly IGameEventNarrationComposer _narrationComposer;
    private readonly ILogger<GameBridgeService> _logger;

    public GameBridgeService(
        IGameTemplateService templateService,
        IGameRuntimeService runtimeService,
        GameModuleRegistry moduleRegistry,
        IGameIntentTranslationAgent translationAgent,
        ParticipantChannelService channelService,
        GameVisibilityProjector visibilityProjector,
        IGameEventNarrationComposer narrationComposer,
        ILogger<GameBridgeService> logger)
    {
        _templateService = templateService;
        _runtimeService = runtimeService;
        _moduleRegistry = moduleRegistry;
        _translationAgent = translationAgent;
        _channelService = channelService;
        _visibilityProjector = visibilityProjector;
        _narrationComposer = narrationComposer;
        _logger = logger;
    }

    public async Task<GameBridgeView> GetViewAsync(
        Guid sessionId,
        string? participantId = null,
        CancellationToken ct = default)
    {
        var runtime = await _runtimeService.LoadViewAsync(sessionId, ct);
        return ProjectView(runtime, participantId);
    }

    public async Task<SessionMutationResult<GameBridgeMutationResult>> StartFromTemplateAsync(
        Guid sessionId,
        StartGameFromTemplateCommand command,
        CancellationToken ct = default)
    {
        GameTemplateValidationEnvelope envelope;
        try
        {
            envelope = await _templateService.LoadAsync(command.TemplateId, ct);
        }
        catch (FileNotFoundException)
        {
            return SessionMutationResult<GameBridgeMutationResult>.Invalid(
                $"Game template '{command.TemplateId}' was not found.");
        }
        catch (ArgumentException ex)
        {
            return SessionMutationResult<GameBridgeMutationResult>.Invalid(ex.Message);
        }

        if (!envelope.Validation.IsValid)
        {
            var issue = envelope.Validation.Issues[0];
            return SessionMutationResult<GameBridgeMutationResult>.Invalid($"{issue.Code}: {issue.Message}");
        }

        var template = envelope.Template;
        var loadRequest = new GameModuleLoadRequest(
            new GameModuleId(template.Module.ModuleId),
            new GameModuleVersionRange(
                new GameModuleVersion(template.Module.MinimumVersion),
                new GameModuleVersion(template.Module.MaximumVersion)),
            new GameTemplateVersion(template.TemplateVersion));
        var module = _moduleRegistry.FindLoadable(loadRequest);
        if (module is null)
        {
            return SessionMutationResult<GameBridgeMutationResult>.Invalid(
                "No registered game module satisfies this template.");
        }

        var participants = BuildParticipants(template, command.UserDisplayName);
        var bindings = BuildParticipantBindings(template, participants, command.UserDisplayName);
        var gameInstanceId = new GameInstanceId($"game-{Guid.CreateVersion7():N}");
        var runtimeResult = await _runtimeService.StartAsync(
            sessionId,
            new StartGameRuntimeCommand(
                template.TemplateId,
                gameInstanceId,
                module.Descriptor.ModuleId,
                module.Descriptor.ModuleVersion,
                command.Seed ?? Random.Shared.NextInt64(1, long.MaxValue),
                new GameTemplateVersion(template.TemplateVersion),
                ToGameSetup(template.RulesOptions.Values),
                participants,
                bindings,
                template.Memory.TokenBudget,
                command.StartedAt,
                template.Communication.PublicChannelEnabled,
                template.Communication.DirectMessagesEnabled),
            ct);

        return await ToBridgeResultAsync(sessionId, runtimeResult, participantId: template.Roster.UserSeatParticipantId, ct);
    }

    public async Task<SessionMutationResult<GameBridgeMutationResult>> SubmitTypedActionAsync(
        Guid sessionId,
        SubmitGameTypedActionCommand command,
        CancellationToken ct = default)
    {
        var runtime = await _runtimeService.LoadViewAsync(sessionId, ct);
        if (runtime?.EngineSnapshot is null || string.IsNullOrWhiteSpace(runtime.GameInstanceId))
        {
            return SessionMutationResult<GameBridgeMutationResult>.Invalid("No game runtime is available for this session.");
        }

        var engineCommand = new SubmitPlayerChoiceIntentCommand(
            GameIntentCommandId.NewId(),
            runtime.EngineSnapshot.GameInstanceId,
            new PendingInputId(command.PendingInputId),
            new ParticipantId(command.ParticipantId),
            command.ChoiceName);
        var result = await _runtimeService.ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(engineCommand, command.OccurredAt),
            ct);
        return await ToBridgeResultAsync(sessionId, result, command.ParticipantId, ct);
    }

    public async Task<SessionMutationResult<GameBridgeMutationResult>> SubmitTextActionAsync(
        Guid sessionId,
        SubmitGameTextActionCommand command,
        CancellationToken ct = default)
    {
        var runtime = await _runtimeService.LoadViewAsync(sessionId, ct);
        if (runtime?.EngineSnapshot is null || string.IsNullOrWhiteSpace(runtime.GameInstanceId))
        {
            return SessionMutationResult<GameBridgeMutationResult>.Invalid("No game runtime is available for this session.");
        }

        var liveState = runtime.EngineSnapshot.ToState();
        var projectionInput = GameVisibilityProjectionInput.FromState(liveState);
        PlayerGameProjection playerProjection;
        try
        {
            playerProjection = _visibilityProjector.ProjectPlayer(projectionInput, new ParticipantId(command.ParticipantId));
        }
        catch (ArgumentException ex)
        {
            return SessionMutationResult<GameBridgeMutationResult>.Invalid(ex.Message);
        }

        var translation = await _translationAgent.TranslateAsync(
            new GameIntentTranslationRequest(
                runtime.GameInstanceId,
                command.ParticipantId,
                command.Text,
                playerProjection.PendingInputs,
                command.OccurredAt),
            ct);
        if (!translation.IsAccepted)
        {
            _logger.LogInformation(
                "Game text action rejected by translator: session={SessionId} game={GameInstanceId} participant={ParticipantId} reason={ReasonCode}",
                sessionId,
                runtime.GameInstanceId,
                command.ParticipantId,
                translation.ReasonCode);
            return SessionMutationResult<GameBridgeMutationResult>.Invalid(
                $"{translation.ReasonCode}: {translation.Message}");
        }

        var typed = new SubmitGameTypedActionCommand(
            command.ParticipantId,
            translation.PendingInputId!,
            translation.ChoiceName!,
            command.OccurredAt);
        return await SubmitTypedActionAsync(sessionId, typed, ct);
    }

    public async Task<SessionMutationResult<GameBridgeMutationResult>> PostPublicMessageAsync(
        Guid sessionId,
        PostGameRuntimePublicMessageCommand command,
        CancellationToken ct = default)
    {
        var result = await _runtimeService.PostPublicMessageAsync(sessionId, command, ct);
        return await ToBridgeResultAsync(sessionId, result, command.ParticipantId, ct);
    }

    public async Task<SessionMutationResult<GameBridgeMutationResult>> SendDirectMessageAsync(
        Guid sessionId,
        SendGameRuntimeDirectMessageCommand command,
        CancellationToken ct = default)
    {
        var result = await _runtimeService.SendDirectMessageAsync(sessionId, command, ct);
        return await ToBridgeResultAsync(sessionId, result, command.ParticipantId, ct);
    }

    public async Task<SessionMutationResult<GameBridgeMutationResult>> EndAsync(
        Guid sessionId,
        EndGameBridgeCommand command,
        CancellationToken ct = default)
    {
        var runtime = await _runtimeService.LoadViewAsync(sessionId, ct);
        if (runtime?.EngineSnapshot is null)
        {
            return SessionMutationResult<GameBridgeMutationResult>.Invalid("No game runtime is available for this session.");
        }

        var engineCommand = new EndGameIntentCommand(
            command.CommandId,
            runtime.EngineSnapshot.GameInstanceId,
            string.IsNullOrWhiteSpace(command.OutcomeName) ? "ended_by_host" : command.OutcomeName.Trim());
        var result = await _runtimeService.ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(engineCommand, command.EndedAt),
            ct);
        return await ToBridgeResultAsync(sessionId, result, null, ct);
    }

    public async Task<SessionMutationResult<GameBridgeMutationResult>> AbortAsync(
        Guid sessionId,
        AbortGameRuntimeCommand command,
        CancellationToken ct = default)
    {
        var result = await _runtimeService.AbortAsync(sessionId, command, ct);
        return await ToBridgeResultAsync(sessionId, result, null, ct);
    }

    private async Task<SessionMutationResult<GameBridgeMutationResult>> ToBridgeResultAsync(
        Guid sessionId,
        SessionMutationResult<GameRuntimeMutationResult> result,
        string? participantId,
        CancellationToken ct)
    {
        if (result.Status != SessionMutationStatus.Success || result.Value is null)
        {
            return new SessionMutationResult<GameBridgeMutationResult>
            {
                Status = result.Status,
                Error = result.Error,
            };
        }

        var view = ProjectView(await _runtimeService.LoadViewAsync(sessionId, ct), participantId);
        return SessionMutationResult<GameBridgeMutationResult>.Success(
            GameBridgeMutationResult.FromRuntime(view, result.Value));
    }

    private async Task<SessionMutationResult<GameBridgeMutationResult>> ToBridgeResultAsync(
        Guid sessionId,
        SessionMutationResult<GameRuntimeCommunicationMutationResult> result,
        string? participantId,
        CancellationToken ct)
    {
        if (result.Status != SessionMutationStatus.Success || result.Value is null)
        {
            return new SessionMutationResult<GameBridgeMutationResult>
            {
                Status = result.Status,
                Error = result.Error,
            };
        }

        var view = ProjectView(await _runtimeService.LoadViewAsync(sessionId, ct), participantId);
        return SessionMutationResult<GameBridgeMutationResult>.Success(
            GameBridgeMutationResult.FromCommunication(view, result.Value));
    }

    private GameBridgeView ProjectView(GameRuntimeState? runtime, string? participantId)
    {
        if (runtime?.EngineSnapshot is null)
        {
            return new GameBridgeView(
                GameRuntimeStatus.NotStarted,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [],
                new GameBridgePublicView([], []),
                null);
        }

        var liveState = runtime.EngineSnapshot.ToState();
        var module = _moduleRegistry.Find(liveState.ModuleId, liveState.ModuleVersion);
        var moduleAuthoring = module is null ? null : ToModuleAuthoringView(module);
        var projectionInput = GameVisibilityProjectionInput.FromState(liveState);
        var publicProjection = _visibilityProjector.ProjectPublic(projectionInput.EventJournal);
        var publicFeed = _channelService.ProjectPublicFeed(runtime.Communication).Entries;
        var player = string.IsNullOrWhiteSpace(participantId)
            ? null
            : ProjectPlayer(runtime, projectionInput, participantId.Trim(), moduleAuthoring);

        return new GameBridgeView(
            runtime.Status,
            runtime.GameInstanceId,
            runtime.TemplateId,
            runtime.ModuleId,
            runtime.ModuleVersion,
            liveState.Round.RoundNumber,
            liveState.Stage.StageId.Value,
            liveState.Stage.DisplayName,
            BuildRoster(runtime, participantId),
            new GameBridgePublicView(
                publicProjection.Events.Select(eventView => ToNarrationEntry(liveState, eventView)).ToArray(),
                publicFeed),
            player)
        {
            ModuleAuthoring = moduleAuthoring,
        };
    }

    private static IReadOnlyList<GameBridgeParticipantView> BuildRoster(GameRuntimeState runtime, string? currentParticipantId)
    {
        var joinedIds = runtime.Communication.Participants
            .Where(participant => participant.IsJoined)
            .Select(participant => participant.ParticipantId.Value)
            .ToHashSet(StringComparer.Ordinal);
        var current = string.IsNullOrWhiteSpace(currentParticipantId) ? null : currentParticipantId.Trim();

        return runtime.ParticipantBindings
            .Select(binding => new GameBridgeParticipantView(
                binding.ParticipantId,
                binding.DisplayName,
                binding.Kind,
                joinedIds.Contains(binding.ParticipantId),
                current is not null && string.Equals(binding.ParticipantId, current, StringComparison.Ordinal)))
            .OrderBy(participant => participant.ParticipantId, StringComparer.Ordinal)
            .ToArray();
    }

    private GameBridgePlayerView? ProjectPlayer(
        GameRuntimeState runtime,
        GameVisibilityProjectionInput projectionInput,
        string participantId,
        GameBridgeModuleAuthoringView? moduleAuthoring)
    {
        PlayerGameProjection playerProjection;
        try
        {
            playerProjection = _visibilityProjector.ProjectPlayer(projectionInput, new ParticipantId(participantId));
        }
        catch (ArgumentException)
        {
            return null;
        }

        var feed = _channelService.ProjectParticipantFeed(runtime.Communication, new GameParticipantId(participantId));
        var cursor = runtime.EventDeliveryCursors.FirstOrDefault(item =>
            string.Equals(item.ParticipantId, participantId, StringComparison.Ordinal));
        return new GameBridgePlayerView(
            participantId,
            playerProjection.Participant.DisplayName,
            playerProjection.Events,
            playerProjection.PendingInputs,
            feed.Entries,
            cursor)
        {
            ActionForms = MatchActionForms(playerProjection.PendingInputs, moduleAuthoring),
        };
    }

    private GameBridgeNarrationEntry ToNarrationEntry(RulesGameState liveState, VisibleGameEvent gameEvent)
    {
        var typedEvent = liveState.EventJournal.Events.FirstOrDefault(item => item.EventId == gameEvent.EventId);
        var text = typedEvent is null
            ? $"{gameEvent.EventType} occurred."
            : _narrationComposer.ComposeSummary(typedEvent);
        return new GameBridgeNarrationEntry(
            gameEvent.EventId.ToString(),
            gameEvent.Sequence,
            gameEvent.EventType,
            text,
            gameEvent.OccurredAt);
    }

    private static GameBridgeModuleAuthoringView ToModuleAuthoringView(IGameModule module)
    {
        var descriptor = module.Descriptor;
        var requiredAssets = descriptor.RequiredPromptAssets
            .Select(asset => $"{asset.AssetId}:{asset.Kind}")
            .ToHashSet(StringComparer.Ordinal);

        return new GameBridgeModuleAuthoringView(
            descriptor.SetupFields.Select(ToSetupFieldView).ToArray(),
            descriptor.AuthoringHooks.Stages.Select(ToStageHookView).ToArray(),
            descriptor.AuthoringHooks.ActionForms.Select(ToActionFormView).ToArray(),
            module.GetPromptAssets()
                .Select(asset => new GameBridgePromptAssetView(
                    asset.AssetId,
                    asset.Kind.ToString(),
                    requiredAssets.Contains($"{asset.AssetId}:{asset.Kind}")))
                .ToArray(),
            new GameBridgeCommunicationCapabilitiesView(
                descriptor.CommunicationCapabilities.AllowsPublicChannelMessages,
                descriptor.CommunicationCapabilities.AllowsDirectMessages),
            new GameBridgeMemoryExpectationsView(
                descriptor.MemoryExpectations.UsesRoundSummaries,
                descriptor.MemoryExpectations.SuggestedSummaryTokenBudget,
                descriptor.MemoryExpectations.MaximumRetainedRoundSummaries),
            new GameBridgeProjectionCapabilitiesView(
                descriptor.AuthoringHooks.ProjectionCapabilities.SupportsPublicEventProjection,
                descriptor.AuthoringHooks.ProjectionCapabilities.SupportsParticipantPrivateProjection,
                descriptor.AuthoringHooks.ProjectionCapabilities.SupportsHostInspectorProjection));
    }

    private static GameBridgeSetupFieldView ToSetupFieldView(GameSetupFieldDescriptor field) =>
        new(field.Name, field.ValueKind.ToString(), field.IsRequired, field.DisplayName, field.Description);

    private static GameBridgeStageHookView ToStageHookView(GameStageDescriptor stage) =>
        new(
            stage.StageId.Value,
            stage.DisplayName,
            stage.Description,
            stage.Sequence,
            stage.AllowsPublicMessages,
            stage.AllowsDirectMessages);

    private static GameBridgeActionFormView ToActionFormView(GameActionFormDescriptor form) =>
        new(
            form.IntentName,
            form.StageId.Value,
            form.DisplayName,
            form.Description,
            form.Layout.ToString(),
            form.Fields
                .Select(field => new GameBridgeActionFieldView(
                    field.Name,
                    field.ValueKind.ToString(),
                    field.IsRequired,
                    field.DisplayName,
                    field.Description))
                .ToArray());

    private static IReadOnlyList<GameBridgeActionFormView> MatchActionForms(
        IReadOnlyList<PendingInputState> pendingInputs,
        GameBridgeModuleAuthoringView? moduleAuthoring)
    {
        if (moduleAuthoring is null || pendingInputs.Count == 0)
        {
            return [];
        }

        return pendingInputs
            .Select(input => moduleAuthoring.ActionForms.FirstOrDefault(form =>
                string.Equals(form.IntentName, input.IntentName, StringComparison.Ordinal)
                && string.Equals(form.StageId, input.StageId.Value, StringComparison.Ordinal)))
            .OfType<GameBridgeActionFormView>()
            .DistinctBy(form => $"{form.StageId}:{form.IntentName}")
            .ToArray();
    }

    private static IReadOnlyList<ParticipantSetup> BuildParticipants(GameTemplate template, string? userDisplayName)
    {
        var participants = new List<ParticipantSetup>();
        if (!string.IsNullOrWhiteSpace(template.Roster.UserSeatParticipantId))
        {
            participants.Add(new ParticipantSetup(
                new ParticipantId(template.Roster.UserSeatParticipantId.Trim()),
                NormalizeDisplayName(userDisplayName, "User"),
                ParticipantKind.Human));
        }

        foreach (var agent in template.Roster.AgentPlayers)
        {
            participants.Add(new ParticipantSetup(
                new ParticipantId(agent.ParticipantId),
                NormalizeDisplayName(agent.FixedName, agent.ParticipantId),
                ParticipantKind.Agent));
        }

        return participants;
    }

    private static IReadOnlyList<GameRuntimeParticipantBinding> BuildParticipantBindings(
        GameTemplate template,
        IReadOnlyList<ParticipantSetup> participants,
        string? userDisplayName)
    {
        var agentByParticipant = template.Roster.AgentPlayers.ToDictionary(
            agent => agent.ParticipantId,
            StringComparer.Ordinal);
        var bindings = new List<GameRuntimeParticipantBinding>();
        foreach (var participant in participants)
        {
            if (agentByParticipant.TryGetValue(participant.ParticipantId.Value, out var agent))
            {
                bindings.Add(new GameRuntimeParticipantBinding
                {
                    ParticipantId = participant.ParticipantId.Value,
                    DisplayName = participant.DisplayName,
                    Kind = GameRuntimeParticipantKind.Agent,
                    ProviderAlias = agent.ProviderAlias,
                    ModelOverride = agent.ModelOverride,
                    CharacterPrompt = agent.CharacterPrompt,
                    Personality = agent.Personality,
                });
                continue;
            }

            bindings.Add(new GameRuntimeParticipantBinding
            {
                ParticipantId = participant.ParticipantId.Value,
                DisplayName = NormalizeDisplayName(userDisplayName, participant.DisplayName),
                Kind = GameRuntimeParticipantKind.Human,
                UserSeatId = participant.ParticipantId.Value,
            });
        }

        return bindings;
    }

    private static GameSetup ToGameSetup(IReadOnlyList<GameTemplateRuleOptionValue> values) =>
        new(values.Select(ToGameSetupValue).ToArray());

    private static GameSetupValue ToGameSetupValue(GameTemplateRuleOptionValue value) =>
        value.Kind switch
        {
            GameTemplateRuleOptionValueKind.String => new StringGameSetupValue(value.Name, value.StringValue ?? string.Empty),
            GameTemplateRuleOptionValueKind.Int => new IntGameSetupValue(value.Name, value.IntValue ?? 0),
            GameTemplateRuleOptionValueKind.Bool => new BoolGameSetupValue(value.Name, value.BoolValue ?? false),
            GameTemplateRuleOptionValueKind.ParticipantId => new ParticipantIdGameSetupValue(value.Name, new ParticipantId(value.ParticipantIdValue ?? string.Empty)),
            GameTemplateRuleOptionValueKind.ParticipantSet => new ParticipantSetGameSetupValue(value.Name, value.ParticipantSetValue.Select(item => new ParticipantId(item)).ToArray()),
            _ => throw new ArgumentException($"Unsupported template rule option kind '{value.Kind}'.", nameof(value)),
        };

    private static string NormalizeDisplayName(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
}
