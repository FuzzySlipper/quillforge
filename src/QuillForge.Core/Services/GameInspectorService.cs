using Den.RulesEngine;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class GameInspectorService : IGameInspectorService
{
    private readonly IGameRuntimeService _runtimeService;
    private readonly ITokenUsageTracker _tokenUsageTracker;

    public GameInspectorService(
        IGameRuntimeService runtimeService,
        ITokenUsageTracker tokenUsageTracker)
    {
        _runtimeService = runtimeService;
        _tokenUsageTracker = tokenUsageTracker;
    }

    public async Task<GameInspectorProjection> GetProjectionAsync(
        Guid sessionId,
        int promptEnvelopeLimit = 10,
        CancellationToken ct = default)
    {
        var runtime = await _runtimeService.LoadViewAsync(sessionId, ct);
        if (runtime?.EngineSnapshot is null)
        {
            return new GameInspectorProjection
            {
                SessionId = sessionId,
                HasGame = false,
                TokenUsage = _tokenUsageTracker.GetSessionUsage(sessionId),
            };
        }

        var liveState = runtime.EngineSnapshot.ToState();
        var envelopeLimit = Math.Max(0, promptEnvelopeLimit);
        return new GameInspectorProjection
        {
            SessionId = sessionId,
            HasGame = true,
            GameInstanceId = runtime.GameInstanceId,
            TemplateId = runtime.TemplateId,
            ModuleId = runtime.ModuleId,
            ModuleVersion = runtime.ModuleVersion,
            Seed = runtime.Seed,
            RuntimeStatus = runtime.Status.ToString(),
            Engine = ToEngineProjection(liveState),
            Participants = ToParticipantProjections(runtime, liveState),
            PromptCursors = runtime.PromptCursors
                .OrderBy(item => item.ParticipantId, StringComparer.Ordinal)
                .Select(ToPromptCursorProjection)
                .ToArray(),
            EventDeliveryCursors = runtime.EventDeliveryCursors
                .OrderBy(item => item.ParticipantId, StringComparer.Ordinal)
                .Select(ToEventDeliveryCursorProjection)
                .ToArray(),
            AgentMemories = runtime.AgentMemories
                .OrderBy(item => item.ParticipantId, StringComparer.Ordinal)
                .Select(ToMemoryProjection)
                .ToArray(),
            PromptEnvelopes = runtime.PromptEnvelopes
                .OrderByDescending(item => item.CreatedAt)
                .ThenByDescending(item => item.EnvelopeId, StringComparer.Ordinal)
                .Take(envelopeLimit)
                .OrderBy(item => item.CreatedAt)
                .ThenBy(item => item.EnvelopeId, StringComparer.Ordinal)
                .Select(ToPromptEnvelopeProjection)
                .ToArray(),
            TokenUsage = _tokenUsageTracker.GetSessionUsage(sessionId),
        };
    }

    private static GameInspectorEngineProjection ToEngineProjection(RulesGameState state) =>
        new()
        {
            Status = state.Status.ToString(),
            RoundNumber = state.Round.RoundNumber,
            StageId = state.Stage.StageId.Value,
            StageName = state.Stage.DisplayName,
            StageAllowsPublicMessages = state.Stage.AllowsPublicMessages,
            StageAllowsDirectMessages = state.Stage.AllowsDirectMessages,
            EventJournalNextSequence = state.EventJournal.NextSequence,
            EventJournal = state.EventJournal.Events.Select(ToEventProjection).ToArray(),
            PendingInputs = state.PendingInputs.Select(ToPendingInputProjection).ToArray(),
        };

    private static IReadOnlyList<GameInspectorParticipantProjection> ToParticipantProjections(
        GameRuntimeState runtime,
        RulesGameState state)
    {
        var engineParticipants = state.Participants.ToDictionary(
            item => item.ParticipantId.Value,
            StringComparer.Ordinal);
        return runtime.ParticipantBindings
            .Select(binding =>
            {
                engineParticipants.TryGetValue(binding.ParticipantId, out var participant);
                return new GameInspectorParticipantProjection
                {
                    ParticipantId = binding.ParticipantId,
                    DisplayName = binding.DisplayName,
                    Kind = binding.Kind.ToString(),
                    IsActive = participant?.IsActive ?? false,
                    ProviderAlias = binding.ProviderAlias,
                    Model = binding.Kind == GameRuntimeParticipantKind.Agent
                        ? (string.IsNullOrWhiteSpace(binding.ModelOverride) ? "default" : binding.ModelOverride)
                        : null,
                };
            })
            .OrderBy(item => item.ParticipantId, StringComparer.Ordinal)
            .ToArray();
    }

    private static GameInspectorEventProjection ToEventProjection(IGameEvent gameEvent) =>
        new()
        {
            EventId = gameEvent.EventId.ToString(),
            Sequence = gameEvent.Sequence,
            EventType = gameEvent.GetType().Name,
            OccurredAt = gameEvent.OccurredAt,
            Visibility = gameEvent.Visibility.Kind.ToString(),
            ParticipantId = ParticipantIdFor(gameEvent),
            PendingInputId = PendingInputIdFor(gameEvent),
            ReasonCode = ReasonCodeFor(gameEvent),
            OutcomeName = gameEvent is GameEndedEvent ended ? ended.OutcomeName : null,
        };

    private static GameInspectorPendingInputProjection ToPendingInputProjection(PendingInputState input) =>
        new()
        {
            PendingInputId = input.PendingInputId.Value,
            ParticipantId = input.ParticipantId.Value,
            StageId = input.StageId.Value,
            IntentName = input.IntentName,
            Status = input.Status.ToString(),
            LegalChoiceNames = input.LegalOptions.Select(option => option.IntentName).ToArray(),
        };

    private static GameInspectorPromptCursorProjection ToPromptCursorProjection(GameRuntimeAgentPromptDeliveryCursor cursor) =>
        new()
        {
            ParticipantId = cursor.ParticipantId,
            LastDeliveredPublicEngineEventSequence = cursor.LastDeliveredPublicEngineEventSequence,
            DeliveredPrivateEventIds = cursor.DeliveredPrivateEventIds.ToArray(),
            CommunicationDeliveredThroughSequence = cursor.CommunicationDeliveredThroughSequence,
            MemoryRevision = cursor.MemoryRevision,
            LastPromptEnvelopeId = cursor.LastPromptEnvelopeId,
        };

    private static GameInspectorEventDeliveryCursorProjection ToEventDeliveryCursorProjection(GameRuntimeEventDeliveryCursor cursor) =>
        new()
        {
            ParticipantId = cursor.ParticipantId,
            DeliveredThroughEngineEventSequence = cursor.DeliveredThroughEngineEventSequence,
            DeliveredThroughCommunicationSequence = cursor.DeliveredThroughCommunicationSequence,
            MemoryRevision = cursor.MemoryRevision,
            LastPromptEnvelopeId = cursor.LastPromptEnvelopeId,
        };

    private static GameInspectorMemoryProjection ToMemoryProjection(GameRuntimeAgentMemoryState memory) =>
        new()
        {
            ParticipantId = memory.ParticipantId,
            Revision = memory.Revision,
            TokenBudget = memory.TokenBudget,
            Summary = memory.Summary,
            ContentHash = memory.ContentHash,
            LastSummarizedRoundNumber = memory.LastSummarizedRoundNumber,
            LastSummarizedPublicEngineEventSequence = memory.LastSummarizedPublicEngineEventSequence,
            LastSummarizedPrivateEventIds = memory.LastSummarizedPrivateEventIds.ToArray(),
            LastSummarizedCommunicationSequence = memory.LastSummarizedCommunicationSequence,
            UpdatedAt = memory.UpdatedAt,
        };

    private static GameInspectorPromptEnvelopeProjection ToPromptEnvelopeProjection(GameRuntimeAgentPromptEnvelope envelope) =>
        new()
        {
            EnvelopeId = envelope.EnvelopeId,
            ParticipantId = envelope.ParticipantId,
            CreatedAt = envelope.CreatedAt,
            EngineCursorSequence = envelope.EngineCursorSequence,
            CommunicationCursorSequence = envelope.CommunicationCursorSequence,
            MemoryRevision = envelope.MemoryRevision,
            ProviderAlias = envelope.ProviderAlias,
            Model = envelope.Model,
            PromptTokens = envelope.PromptTokens,
            ResponseTokens = envelope.ResponseTokens,
            PromptContentHash = envelope.PromptContentHash,
            ResponseContentHash = envelope.ResponseContentHash,
            PromptPreview = Preview(envelope.PromptText, 600),
            ResponsePreview = Preview(envelope.ResponseText, 300),
        };

    private static string? ParticipantIdFor(IGameEvent gameEvent) =>
        gameEvent switch
        {
            PlayerChoiceSubmittedEvent choice => choice.ParticipantId.Value,
            AgentResponseRejectedEvent rejected => rejected.ParticipantId.Value,
            NoActionTakenEvent noAction => noAction.ParticipantId.Value,
            PendingInputRequestedEvent requested => requested.ParticipantId.Value,
            _ => null,
        };

    private static string? PendingInputIdFor(IGameEvent gameEvent) =>
        gameEvent switch
        {
            PlayerChoiceSubmittedEvent choice => choice.PendingInputId.Value,
            AgentResponseRejectedEvent rejected => rejected.PendingInputId.Value,
            NoActionTakenEvent noAction => noAction.PendingInputId.Value,
            PendingInputRequestedEvent requested => requested.PendingInputId.Value,
            _ => null,
        };

    private static string? ReasonCodeFor(IGameEvent gameEvent) =>
        gameEvent switch
        {
            AgentResponseRejectedEvent rejected => rejected.ReasonCode,
            NoActionTakenEvent noAction => noAction.ReasonCode,
            IntentCommandRejectedEvent rejected => rejected.ReasonCode,
            GameAbortedEvent aborted => aborted.ReasonCode,
            RoundEndedEvent roundEnded => roundEnded.ReasonCode,
            _ => null,
        };

    private static string? Preview(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        var normalized = value.Replace("\r", "", StringComparison.Ordinal).Trim();
        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength] + "…";
    }
}
