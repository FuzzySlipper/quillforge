using Den.RulesEngine;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class GameRuntimeService : IGameRuntimeService
{
    private readonly ISessionStateStore _store;
    private readonly ISessionMutationGate _gate;
    private readonly GameModuleRegistry _moduleRegistry;
    private readonly RulesEngineService _rulesEngine;
    private readonly ILogger<GameRuntimeService> _logger;

    public GameRuntimeService(
        ISessionStateStore store,
        ISessionMutationGate gate,
        GameModuleRegistry moduleRegistry,
        RulesEngineService rulesEngine,
        ILogger<GameRuntimeService> logger)
    {
        _store = store;
        _gate = gate;
        _moduleRegistry = moduleRegistry;
        _rulesEngine = rulesEngine;
        _logger = logger;
    }

    public async Task<GameRuntimeState?> LoadViewAsync(Guid sessionId, CancellationToken ct = default)
    {
        var state = await _store.LoadAsync(sessionId, ct);
        return GameRuntimeStateCloner.Clone(state.Game);
    }

    public async Task<SessionMutationResult<GameRuntimeMutationResult>> StartAsync(
        Guid sessionId,
        StartGameRuntimeCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "start_game_runtime";
        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return Busy();
        }

        var state = await _store.LoadAsync(sessionId, ct);
        if (state.Game?.IsActive == true)
        {
            _logger.LogWarning(
                "Game runtime start rejected: session={SessionId} activeGame={GameInstanceId} status={Status}",
                sessionId,
                state.Game.GameInstanceId,
                state.Game.Status);
            return SessionMutationResult<GameRuntimeMutationResult>.Invalid("A game is already active for this session.");
        }

        var bindingIssue = ValidateParticipantBindings(command);
        if (bindingIssue is not null)
        {
            return SessionMutationResult<GameRuntimeMutationResult>.Invalid(bindingIssue);
        }

        var registration = _moduleRegistry.ValidateRegistered(command.ModuleId, command.ModuleVersion);
        if (!registration.IsValid)
        {
            return Invalid(registration.Issues[0]);
        }

        var module = _moduleRegistry.Find(command.ModuleId, command.ModuleVersion);
        if (module is null)
        {
            return SessionMutationResult<GameRuntimeMutationResult>.Invalid("Requested game module is not registered.");
        }

        var setupValidation = new GameSetupValidationService(_moduleRegistry).Validate(
            command.ModuleId,
            command.ModuleVersion,
            command.TemplateVersion,
            command.Setup,
            command.Participants);
        if (!setupValidation.IsValid)
        {
            return Invalid(setupValidation.Issues[0]);
        }

        var participantStates = command.Participants
            .Select(participant => new ParticipantState(
                participant.ParticipantId,
                participant.DisplayName,
                participant.Kind,
                []))
            .ToArray();
        var initialState = RulesGameState.CreateNotStarted(
            command.GameInstanceId,
            module.Descriptor,
            command.Seed,
            participantStates);
        var startIntent = new StartGameIntentCommand(
            GameIntentCommandId.NewId(),
            command.GameInstanceId,
            command.ModuleId,
            command.ModuleVersion,
            command.Seed,
            command.Setup,
            command.Participants);
        var applyResult = _rulesEngine.Apply(initialState, startIntent);
        if (!applyResult.IsAccepted)
        {
            return Invalid(applyResult.Issues[0]);
        }

        var runtime = CreateRuntime(command, applyResult.State, applyResult.Events);
        state.Game = runtime;
        await _store.SaveAsync(state, ct);

        _logger.LogInformation(
            "Game runtime started: session={SessionId} game={GameInstanceId} module={ModuleId} version={ModuleVersion} status={Status}",
            sessionId,
            runtime.GameInstanceId,
            runtime.ModuleId,
            runtime.ModuleVersion,
            runtime.Status);

        var runtimeEvent = new GameRuntimeStartedEvent(
            runtime.GameInstanceId!,
            runtime.TemplateId,
            runtime.ModuleId!,
            runtime.ModuleVersion!,
            runtime.Status,
            command.StartedAt);
        return SessionMutationResult<GameRuntimeMutationResult>.Success(
            new GameRuntimeMutationResult(GameRuntimeStateCloner.Clone(runtime)!, [runtimeEvent], applyResult.Events));
    }

    public async Task<SessionMutationResult<GameRuntimeMutationResult>> ApplyEngineCommandAsync(
        Guid sessionId,
        ApplyGameRuntimeEngineCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "apply_game_engine_command";
        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return Busy();
        }

        var state = await _store.LoadAsync(sessionId, ct);
        var runtime = state.Game;
        if (runtime?.EngineSnapshot is null)
        {
            return SessionMutationResult<GameRuntimeMutationResult>.Invalid("No game runtime is available for this session.");
        }

        if (!string.Equals(runtime.GameInstanceId, command.EngineCommand.GameInstanceId.Value, StringComparison.Ordinal))
        {
            return SessionMutationResult<GameRuntimeMutationResult>.Invalid("Engine command targets a different game instance.");
        }

        var liveState = runtime.EngineSnapshot.ToState();
        var applyResult = _rulesEngine.Apply(liveState, command.EngineCommand);
        if (!applyResult.IsAccepted)
        {
            runtime.EngineSnapshot = RulesGameStateSnapshot.FromState(applyResult.State);
            runtime.Status = ToRuntimeStatus(applyResult.State.Status);
            runtime.LastUpdatedAt = command.OccurredAt;
            GameRuntimeStateCloner.AppendHostRecord(
                runtime,
                GameRuntimeHostRecordKind.EngineCommandApplied,
                command.OccurredAt,
                applyResult.Issues[0].Code,
                $"Engine command rejected: {applyResult.Issues[0].Message}");
            await _store.SaveAsync(state, ct);
            return Invalid(applyResult.Issues[0]);
        }

        UpdateRuntimeFromApplyResult(runtime, applyResult.State, command.OccurredAt);
        var hostRecordKind = runtime.Status == GameRuntimeStatus.Aborted
            ? GameRuntimeHostRecordKind.Aborted
            : GameRuntimeHostRecordKind.EngineCommandApplied;
        GameRuntimeStateCloner.AppendHostRecord(
            runtime,
            hostRecordKind,
            command.OccurredAt,
            "engine_command_applied",
            $"Applied {command.EngineCommand.GetType().Name}.");
        await _store.SaveAsync(state, ct);

        _logger.LogInformation(
            "Game engine command applied: session={SessionId} game={GameInstanceId} command={CommandType} status={Status} eventCount={EventCount}",
            sessionId,
            runtime.GameInstanceId,
            command.EngineCommand.GetType().Name,
            runtime.Status,
            applyResult.Events.Count);

        var runtimeEvent = new GameRuntimeEngineCommandAppliedEvent(
            runtime.GameInstanceId!,
            command.EngineCommand.CommandId,
            command.EngineCommand.GetType().Name,
            runtime.Status,
            applyResult.Events,
            command.OccurredAt);
        return SessionMutationResult<GameRuntimeMutationResult>.Success(
            new GameRuntimeMutationResult(GameRuntimeStateCloner.Clone(runtime)!, [runtimeEvent], applyResult.Events));
    }

    public async Task<SessionMutationResult<GameRuntimeMutationResult>> ResumeAsync(
        Guid sessionId,
        ResumeGameRuntimeCommand command,
        CancellationToken ct = default)
    {
        const string operationName = "resume_game_runtime";
        await using var lease = await _gate.TryAcquireAsync(sessionId, operationName, ct);
        if (lease is null)
        {
            return Busy();
        }

        var state = await _store.LoadAsync(sessionId, ct);
        var runtime = state.Game;
        if (runtime?.EngineSnapshot is null || string.IsNullOrWhiteSpace(runtime.GameInstanceId))
        {
            return SessionMutationResult<GameRuntimeMutationResult>.Invalid("No game runtime is available for this session.");
        }

        runtime.LastResumedAt = command.ResumedAt;
        runtime.LastUpdatedAt = command.ResumedAt;
        GameRuntimeStateCloner.AppendHostRecord(
            runtime,
            GameRuntimeHostRecordKind.Resumed,
            command.ResumedAt,
            "resumed_session",
            "Game runtime resumed from persisted session state.");
        await _store.SaveAsync(state, ct);

        var runtimeEvent = new GameRuntimeResumedEvent(runtime.GameInstanceId, runtime.Status, command.ResumedAt);
        return SessionMutationResult<GameRuntimeMutationResult>.Success(
            new GameRuntimeMutationResult(GameRuntimeStateCloner.Clone(runtime)!, [runtimeEvent], []));
    }

    public Task<SessionMutationResult<GameRuntimeMutationResult>> AbortAsync(
        Guid sessionId,
        AbortGameRuntimeCommand command,
        CancellationToken ct = default)
    {
        return AbortAsyncCore(sessionId, command, ct);
    }

    private async Task<SessionMutationResult<GameRuntimeMutationResult>> AbortAsyncCore(
        Guid sessionId,
        AbortGameRuntimeCommand command,
        CancellationToken ct)
    {
        var state = await _store.LoadAsync(sessionId, ct);
        var runtime = state.Game;
        if (runtime?.EngineSnapshot is null || string.IsNullOrWhiteSpace(runtime.GameInstanceId))
        {
            return SessionMutationResult<GameRuntimeMutationResult>.Invalid("No game runtime is available for this session.");
        }

        var abortIntent = new AbortGameIntentCommand(
            command.CommandId,
            runtime.EngineSnapshot.GameInstanceId,
            NormalizeReasonCode(command.ReasonCode));
        var result = await ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(abortIntent, command.AbortedAt),
            ct);
        if (result.Status != SessionMutationStatus.Success || result.Value is null)
        {
            return result;
        }

        var runtimeEvent = new GameRuntimeAbortedEvent(
            result.Value.Game.GameInstanceId!,
            abortIntent.ReasonCode,
            result.Value.Game.Status,
            command.AbortedAt);
        return SessionMutationResult<GameRuntimeMutationResult>.Success(
            result.Value with
            {
                RuntimeEvents = result.Value.RuntimeEvents.Concat([runtimeEvent]).ToArray()
            });
    }

    private static GameRuntimeState CreateRuntime(
        StartGameRuntimeCommand command,
        RulesGameState startedState,
        IReadOnlyList<IGameEvent> events)
    {
        var runtime = new GameRuntimeState
        {
            Status = ToRuntimeStatus(startedState.Status),
            GameInstanceId = command.GameInstanceId.Value,
            TemplateId = NormalizeChoice(command.TemplateId),
            ModuleId = command.ModuleId.Value,
            ModuleVersion = command.ModuleVersion.Value,
            Seed = command.Seed,
            StartedAt = command.StartedAt,
            LastUpdatedAt = command.StartedAt,
            EngineSnapshot = RulesGameStateSnapshot.FromState(startedState),
            ParticipantBindings = command.ParticipantBindings.Select(CloneBinding).ToList(),
            Communication = CreateCommunicationState(command.ParticipantBindings),
            EventDeliveryCursors = command.ParticipantBindings.Select(binding => new GameRuntimeEventDeliveryCursor
            {
                ParticipantId = binding.ParticipantId,
            }).ToList(),
            PromptCursors = command.ParticipantBindings
                .Where(binding => binding.Kind == GameRuntimeParticipantKind.Agent)
                .Select(binding => new GameRuntimeAgentPromptDeliveryCursor
                {
                    ParticipantId = binding.ParticipantId,
                }).ToList(),
            AgentMemories = command.ParticipantBindings
                .Where(binding => binding.Kind == GameRuntimeParticipantKind.Agent)
                .Select(binding => new GameRuntimeAgentMemoryState
                {
                    ParticipantId = binding.ParticipantId,
                    TokenBudget = command.AgentMemoryTokenBudget,
                }).ToList(),
        };

        GameRuntimeStateCloner.AppendHostRecord(
            runtime,
            GameRuntimeHostRecordKind.Started,
            command.StartedAt,
            "game_started",
            $"Game runtime started with {events.Count} engine event(s).");

        return runtime;
    }

    private static void UpdateRuntimeFromApplyResult(GameRuntimeState runtime, RulesGameState state, DateTimeOffset occurredAt)
    {
        runtime.EngineSnapshot = RulesGameStateSnapshot.FromState(state);
        runtime.Status = ToRuntimeStatus(state.Status);
        runtime.LastUpdatedAt = occurredAt;
        if (runtime.Status is GameRuntimeStatus.Ended or GameRuntimeStatus.Aborted)
        {
            runtime.EndedAt = occurredAt;
        }
    }

    private static ParticipantCommunicationState CreateCommunicationState(
        IReadOnlyList<GameRuntimeParticipantBinding> bindings)
    {
        var communication = new ParticipantCommunicationState();
        var sequence = communication.NextSequence;
        foreach (var binding in bindings)
        {
            var participantId = new GameParticipantId(binding.ParticipantId);
            communication.Participants.Add(new ParticipantPresenceState
            {
                ParticipantId = participantId,
                DisplayName = binding.DisplayName,
                IsJoined = true,
                JoinedSequence = sequence,
            });
            communication.Cursors.Add(new ParticipantCommunicationCursor
            {
                ParticipantId = participantId,
            });
            sequence++;
        }

        communication.NextSequence = sequence;
        return communication;
    }

    private static GameRuntimeStatus ToRuntimeStatus(RulesGameStatus status) => status switch
    {
        RulesGameStatus.NotStarted => GameRuntimeStatus.NotStarted,
        RulesGameStatus.Running => GameRuntimeStatus.Running,
        RulesGameStatus.WaitingForInput => GameRuntimeStatus.WaitingForInput,
        RulesGameStatus.Resolving => GameRuntimeStatus.Resolving,
        RulesGameStatus.Ended => GameRuntimeStatus.Ended,
        RulesGameStatus.Aborted => GameRuntimeStatus.Aborted,
        _ => GameRuntimeStatus.NotStarted,
    };

    private static GameRuntimeParticipantBinding CloneBinding(GameRuntimeParticipantBinding binding) => new()
    {
        ParticipantId = binding.ParticipantId,
        DisplayName = binding.DisplayName,
        Kind = binding.Kind,
        ProviderAlias = binding.ProviderAlias,
        ModelOverride = binding.ModelOverride,
        CharacterPrompt = binding.CharacterPrompt,
        Personality = binding.Personality,
        UserSeatId = binding.UserSeatId,
    };

    private static string? ValidateParticipantBindings(StartGameRuntimeCommand command)
    {
        if (command.Participants.Count == 0)
        {
            return "At least one engine participant is required.";
        }

        var participantIds = command.Participants
            .Select(participant => participant.ParticipantId.Value)
            .ToHashSet(StringComparer.Ordinal);
        if (participantIds.Count != command.Participants.Count)
        {
            return "Engine participant IDs must be unique.";
        }

        var bindingIds = command.ParticipantBindings
            .Select(binding => NormalizeChoice(binding.ParticipantId) ?? string.Empty)
            .ToHashSet(StringComparer.Ordinal);
        if (bindingIds.Count != command.ParticipantBindings.Count || bindingIds.Contains(string.Empty))
        {
            return "Runtime participant bindings must have unique participant IDs.";
        }

        if (!participantIds.SetEquals(bindingIds))
        {
            return "Runtime participant bindings must match engine participants.";
        }

        return null;
    }

    private static SessionMutationResult<GameRuntimeMutationResult> Busy() =>
        SessionMutationResult<GameRuntimeMutationResult>.Busy(
            "Another mutating operation is already running for this session.");

    private static SessionMutationResult<GameRuntimeMutationResult> Invalid(ValidationIssue issue) =>
        SessionMutationResult<GameRuntimeMutationResult>.Invalid($"{issue.Code}: {issue.Message}");

    private static string NormalizeReasonCode(string? reasonCode) =>
        string.IsNullOrWhiteSpace(reasonCode) ? "aborted_by_host" : reasonCode.Trim();

    private static string? NormalizeChoice(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
