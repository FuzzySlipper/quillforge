using Den.RulesEngine;

namespace QuillForge.Core.Models;

internal static class GameRuntimeStateCloner
{
    public static GameRuntimeState? Clone(GameRuntimeState? state)
    {
        if (state is null)
        {
            return null;
        }

        return new GameRuntimeState
        {
            Status = state.Status,
            GameInstanceId = state.GameInstanceId,
            TemplateId = state.TemplateId,
            ModuleId = state.ModuleId,
            ModuleVersion = state.ModuleVersion,
            Seed = state.Seed,
            StartedAt = state.StartedAt,
            LastResumedAt = state.LastResumedAt,
            LastUpdatedAt = state.LastUpdatedAt,
            EndedAt = state.EndedAt,
            EngineSnapshot = CloneSnapshot(state.EngineSnapshot),
            Communication = CloneCommunication(state.Communication),
            HostAllowsPublicMessages = state.HostAllowsPublicMessages,
            HostAllowsDirectMessages = state.HostAllowsDirectMessages,
            ParticipantBindings = state.ParticipantBindings.Select(CloneParticipantBinding).ToList(),
            EventDeliveryCursors = state.EventDeliveryCursors.Select(CloneEventDeliveryCursor).ToList(),
            AgentMemories = state.AgentMemories.Select(CloneAgentMemory).ToList(),
            MemorySummaryDecisions = state.MemorySummaryDecisions.Select(CloneMemorySummaryDecision).ToList(),
            PromptCursors = state.PromptCursors.Select(ClonePromptCursor).ToList(),
            PromptEnvelopes = state.PromptEnvelopes.Select(ClonePromptEnvelope).ToList(),
            NextHostRecordSequence = state.NextHostRecordSequence,
            HostRecords = state.HostRecords.Select(CloneHostRecord).ToList(),
        };
    }

    public static GameRuntimeState? CloneForFork(
        GameRuntimeState? state,
        Guid sourceSessionId,
        Guid targetSessionId,
        DateTimeOffset occurredAt)
    {
        var clone = Clone(state);
        if (clone is null)
        {
            return null;
        }

        AppendHostRecord(
            clone,
            GameRuntimeHostRecordKind.Forked,
            occurredAt,
            "forked_session",
            $"Game runtime forked from session {sourceSessionId} to session {targetSessionId}.",
            sourceSessionId,
            targetSessionId);

        return clone;
    }

    public static GameRuntimeHostRecord AppendHostRecord(
        GameRuntimeState state,
        GameRuntimeHostRecordKind kind,
        DateTimeOffset occurredAt,
        string reasonCode,
        string summary,
        Guid? sourceSessionId = null,
        Guid? targetSessionId = null)
    {
        var record = new GameRuntimeHostRecord
        {
            Sequence = state.NextHostRecordSequence,
            Kind = kind,
            OccurredAt = occurredAt,
            ReasonCode = reasonCode,
            Summary = summary,
            SourceSessionId = sourceSessionId,
            TargetSessionId = targetSessionId,
        };
        state.NextHostRecordSequence++;
        state.HostRecords.Add(record);
        return record;
    }

    private static RulesGameStateSnapshot? CloneSnapshot(RulesGameStateSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            return null;
        }

        return new RulesGameStateSnapshot(
            snapshot.GameInstanceId,
            snapshot.ModuleId,
            snapshot.ModuleVersion,
            snapshot.Status,
            snapshot.Random,
            snapshot.Round,
            snapshot.Stage,
            snapshot.Participants.ToArray(),
            snapshot.PendingInputs.ToArray(),
            new GameEventJournalSnapshot(
                snapshot.EventJournal.GameInstanceId,
                snapshot.EventJournal.NextSequence,
                snapshot.EventJournal.Events.ToArray()));
    }

    private static ParticipantCommunicationState CloneCommunication(ParticipantCommunicationState state) => new()
    {
        NextSequence = state.NextSequence,
        Participants = state.Participants.Select(participant => new ParticipantPresenceState
        {
            ParticipantId = participant.ParticipantId,
            DisplayName = participant.DisplayName,
            IsJoined = participant.IsJoined,
            JoinedSequence = participant.JoinedSequence,
            LeftSequence = participant.LeftSequence,
        }).ToList(),
        ChannelMessages = state.ChannelMessages.ToList(),
        DirectMessages = state.DirectMessages
            .Select(message => message with { RecipientParticipantIds = message.RecipientParticipantIds.ToArray() })
            .ToList(),
        GameEventLinks = state.GameEventLinks
            .Select(link => link with { VisibleToParticipantIds = link.VisibleToParticipantIds.ToArray() })
            .ToList(),
        Cursors = state.Cursors.Select(cursor => new ParticipantCommunicationCursor
        {
            ParticipantId = cursor.ParticipantId,
            DeliveredThroughSequence = cursor.DeliveredThroughSequence,
            ReadThroughSequence = cursor.ReadThroughSequence,
        }).ToList(),
    };

    private static GameRuntimeParticipantBinding CloneParticipantBinding(GameRuntimeParticipantBinding binding) => new()
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

    private static GameRuntimeEventDeliveryCursor CloneEventDeliveryCursor(GameRuntimeEventDeliveryCursor cursor) => new()
    {
        ParticipantId = cursor.ParticipantId,
        DeliveredThroughEngineEventSequence = cursor.DeliveredThroughEngineEventSequence,
        DeliveredThroughCommunicationSequence = cursor.DeliveredThroughCommunicationSequence,
        MemoryRevision = cursor.MemoryRevision,
        LastPromptEnvelopeId = cursor.LastPromptEnvelopeId,
    };

    private static GameRuntimeAgentMemoryState CloneAgentMemory(GameRuntimeAgentMemoryState memory) => new()
    {
        ParticipantId = memory.ParticipantId,
        Revision = memory.Revision,
        TokenBudget = memory.TokenBudget,
        Summary = memory.Summary,
        ContentHash = memory.ContentHash,
        LastSummarizedRoundNumber = memory.LastSummarizedRoundNumber,
        LastSummarizedPublicEngineEventSequence = memory.LastSummarizedPublicEngineEventSequence,
        LastSummarizedPrivateEventIds = memory.LastSummarizedPrivateEventIds.ToList(),
        LastSummarizedCommunicationSequence = memory.LastSummarizedCommunicationSequence,
        UpdatedAt = memory.UpdatedAt,
    };

    private static MemorySummaryDecision CloneMemorySummaryDecision(MemorySummaryDecision decision) => decision with
    {
        PriorCursor = decision.PriorCursor with { PrivateEngineEventIds = decision.PriorCursor.PrivateEngineEventIds.ToArray() },
        NewCursor = decision.NewCursor with { PrivateEngineEventIds = decision.NewCursor.PrivateEngineEventIds.ToArray() },
    };

    private static GameRuntimeAgentPromptDeliveryCursor ClonePromptCursor(GameRuntimeAgentPromptDeliveryCursor cursor) => new()
    {
        ParticipantId = cursor.ParticipantId,
        LastDeliveredPublicEngineEventSequence = cursor.LastDeliveredPublicEngineEventSequence,
        DeliveredPrivateEventIds = cursor.DeliveredPrivateEventIds.ToList(),
        CommunicationDeliveredThroughSequence = cursor.CommunicationDeliveredThroughSequence,
        MemoryRevision = cursor.MemoryRevision,
        LastPromptEnvelopeId = cursor.LastPromptEnvelopeId,
    };

    private static GameRuntimeAgentPromptEnvelope ClonePromptEnvelope(GameRuntimeAgentPromptEnvelope envelope) => new()
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
        PromptText = envelope.PromptText,
        ResponseText = envelope.ResponseText,
    };

    private static GameRuntimeHostRecord CloneHostRecord(GameRuntimeHostRecord record) => new()
    {
        RecordId = record.RecordId,
        Sequence = record.Sequence,
        Kind = record.Kind,
        OccurredAt = record.OccurredAt,
        ReasonCode = record.ReasonCode,
        Summary = record.Summary,
        SourceSessionId = record.SourceSessionId,
        TargetSessionId = record.TargetSessionId,
    };
}
