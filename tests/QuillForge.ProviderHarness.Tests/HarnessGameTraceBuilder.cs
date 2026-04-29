using Den.RulesEngine;
using QuillForge.Core.Models;

namespace QuillForge.ProviderHarness.Tests;

public static class HarnessGameTraceBuilder
{
    public static HarnessGameTrace FromRuntime(
        string? runId,
        string scenarioName,
        Guid sessionId,
        GameRuntimeState runtime,
        GameBridgeView publicView,
        IReadOnlyDictionary<string, GameBridgeView> playerViews,
        IReadOnlyList<GameAgentTurnParticipantResult> actionResults,
        IReadOnlyList<GameAgentMemorySummaryParticipantResult> memoryResults,
        IReadOnlyList<IGameRuntimeEvent> runtimeEvents,
        string determinismMode,
        string determinismDescription,
        bool liveProviderRun)
    {
        var liveState = runtime.EngineSnapshot?.ToState();
        var engineEvents = liveState?.EventJournal.Events ?? [];
        var finalOutcome = engineEvents.OfType<GameEndedEvent>().LastOrDefault()?.OutcomeName;
        var actionsByParticipant = actionResults
            .GroupBy(item => item.ParticipantId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var choicesByParticipant = engineEvents
            .OfType<PlayerChoiceSubmittedEvent>()
            .GroupBy(item => item.ParticipantId.Value, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        var decisionsByParticipant = runtime.MemorySummaryDecisions
            .GroupBy(item => item.ParticipantId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        return new HarnessGameTrace
        {
            RunId = runId,
            ScenarioName = scenarioName,
            DeterminismMode = determinismMode,
            DeterminismDescription = determinismDescription,
            LiveProviderRun = liveProviderRun,
            SessionId = sessionId,
            GameInstanceId = runtime.GameInstanceId,
            TemplateId = runtime.TemplateId,
            ModuleId = runtime.ModuleId,
            ModuleVersion = runtime.ModuleVersion,
            Seed = runtime.Seed,
            Status = runtime.Status.ToString(),
            RoundNumber = publicView.RoundNumber,
            StageId = publicView.StageId,
            StageName = publicView.StageName,
            FinalOutcome = finalOutcome,
            Agents = runtime.ParticipantBindings.Select(binding => ToAgentTrace(binding, runtime)).ToArray(),
            PromptEnvelopes = runtime.PromptEnvelopes.Select(ToPromptEnvelopeTrace).ToArray(),
            Actions = actionResults.Select(result => ToActionTrace(result, choicesByParticipant)).ToArray(),
            MemorySummaries = memoryResults.Select(result => ToMemorySummaryTrace(result, decisionsByParticipant)).ToArray(),
            EngineEvents = engineEvents.Select(ToGameEventTrace).ToArray(),
            RuntimeEvents = runtimeEvents.Select(ToRuntimeEventTrace).ToArray(),
            PublicFeed = publicView.Public.Feed.Select(ToCommunicationTrace).ToArray(),
            PrivateEventsByParticipant = playerViews.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<HarnessGameVisibleEventTrace>)(pair.Value.Player?.EngineEvents.Select(ToVisibleEventTrace).ToArray() ?? []),
                StringComparer.Ordinal),
            PrivateFeedByParticipant = playerViews.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<HarnessGameCommunicationTrace>)(pair.Value.Player?.Feed.Select(ToCommunicationTrace).ToArray() ?? []),
                StringComparer.Ordinal),
            FailureSurface = BuildFailureSurface(engineEvents, runtime.MemorySummaryDecisions),
            Usage = SumUsage(actionResults.Select(item => item.Usage).Concat(memoryResults.Select(item => item.Usage))),
        };
    }

    private static HarnessGameAgentTrace ToAgentTrace(GameRuntimeParticipantBinding binding, GameRuntimeState runtime)
    {
        var cursor = runtime.PromptCursors.FirstOrDefault(item =>
            string.Equals(item.ParticipantId, binding.ParticipantId, StringComparison.Ordinal));
        var memory = runtime.AgentMemories.FirstOrDefault(item =>
            string.Equals(item.ParticipantId, binding.ParticipantId, StringComparison.Ordinal));

        return new HarnessGameAgentTrace
        {
            ParticipantId = binding.ParticipantId,
            DisplayName = binding.DisplayName,
            Kind = binding.Kind.ToString(),
            ProviderAlias = binding.ProviderAlias,
            Model = string.IsNullOrWhiteSpace(binding.ModelOverride) ? "default" : binding.ModelOverride,
            PromptCursor = cursor is null
                ? null
                : new HarnessGamePromptCursorTrace
                {
                    PublicEngineEventSequence = cursor.LastDeliveredPublicEngineEventSequence,
                    PrivateEngineEventIds = cursor.DeliveredPrivateEventIds.ToArray(),
                    CommunicationSequence = cursor.CommunicationDeliveredThroughSequence,
                    MemoryRevision = cursor.MemoryRevision,
                    LastPromptEnvelopeId = cursor.LastPromptEnvelopeId,
                },
            Memory = memory is null
                ? null
                : new HarnessGameMemoryStateTrace
                {
                    Revision = memory.Revision,
                    TokenBudget = memory.TokenBudget,
                    Summary = memory.Summary,
                    ContentHash = memory.ContentHash,
                    LastSummarizedRoundNumber = memory.LastSummarizedRoundNumber,
                    LastSummarizedPublicEngineEventSequence = memory.LastSummarizedPublicEngineEventSequence,
                    LastSummarizedPrivateEventIds = memory.LastSummarizedPrivateEventIds.ToArray(),
                    LastSummarizedCommunicationSequence = memory.LastSummarizedCommunicationSequence,
                },
        };
    }

    private static HarnessGamePromptEnvelopeTrace ToPromptEnvelopeTrace(GameRuntimeAgentPromptEnvelope envelope) =>
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

    private static HarnessGameActionTrace ToActionTrace(
        GameAgentTurnParticipantResult result,
        IReadOnlyDictionary<string, PlayerChoiceSubmittedEvent[]> choicesByParticipant)
    {
        var choice = choicesByParticipant.TryGetValue(result.ParticipantId, out var participantChoices)
            ? participantChoices.FirstOrDefault(item => item.PendingInputId.Value == result.PendingInputId)
            : null;
        return new HarnessGameActionTrace
        {
            ParticipantId = result.ParticipantId,
            PendingInputId = result.PendingInputId,
            Outcome = result.Outcome.ToString(),
            ReasonCode = result.ReasonCode,
            Message = result.Message,
            ProviderAlias = result.ProviderAlias,
            Model = result.Model,
            ChoiceName = choice?.ChoiceName,
            Usage = new HarnessUsage(result.Usage.InputTokens, result.Usage.OutputTokens),
        };
    }

    private static HarnessGameMemorySummaryTrace ToMemorySummaryTrace(
        GameAgentMemorySummaryParticipantResult result,
        IReadOnlyDictionary<string, MemorySummaryDecision[]> decisionsByParticipant)
    {
        var decision = decisionsByParticipant.TryGetValue(result.ParticipantId, out var participantDecisions)
            ? participantDecisions.LastOrDefault(item => item.RoundNumber == result.RoundNumber)
            : null;
        return new HarnessGameMemorySummaryTrace
        {
            ParticipantId = result.ParticipantId,
            RoundNumber = result.RoundNumber,
            Outcome = result.Outcome.ToString(),
            ReasonCode = result.ReasonCode,
            Message = result.Message,
            ProviderAlias = result.ProviderAlias,
            Model = result.Model,
            Usage = new HarnessUsage(result.Usage.InputTokens, result.Usage.OutputTokens),
            DecisionId = decision?.DecisionId,
            ExceededTokenBudget = decision?.ExceededTokenBudget ?? false,
            Trimmed = decision?.Trimmed ?? false,
            Retried = decision?.Retried ?? false,
            RejectionReason = decision?.RejectionReason,
            SummaryContentHash = decision?.SummaryContentHash,
        };
    }

    private static HarnessGameEventTrace ToGameEventTrace(IGameEvent gameEvent)
    {
        var facts = GameEventIntrospection.Inspect(gameEvent);
        return new HarnessGameEventTrace
        {
            EventId = gameEvent.EventId.ToString(),
            Sequence = gameEvent.Sequence,
            EventType = gameEvent.GetType().Name,
            OccurredAt = gameEvent.OccurredAt,
            Visibility = gameEvent.Visibility.Kind.ToString(),
            ParticipantId = facts.ParticipantId,
            PendingInputId = facts.PendingInputId,
            ChoiceName = facts.ChoiceName,
            ReasonCode = facts.ReasonCode,
            OutcomeName = facts.OutcomeName,
        };
    }

    private static HarnessGameRuntimeEventTrace ToRuntimeEventTrace(IGameRuntimeEvent runtimeEvent) =>
        runtimeEvent switch
        {
            GameRuntimeAgentPromptRecordedEvent prompt => new HarnessGameRuntimeEventTrace
            {
                EventName = runtimeEvent.EventName,
                OccurredAt = runtimeEvent.OccurredAt,
                ParticipantId = prompt.ParticipantId,
                ProviderAlias = prompt.ProviderAlias,
                Model = prompt.Model,
                PromptTokens = prompt.PromptTokens,
                ResponseTokens = prompt.ResponseTokens,
            },
            GameRuntimeAgentMemorySummaryRecordedEvent memory => new HarnessGameRuntimeEventTrace
            {
                EventName = runtimeEvent.EventName,
                OccurredAt = runtimeEvent.OccurredAt,
                ParticipantId = memory.ParticipantId,
                ProviderAlias = memory.ProviderAlias,
                Model = memory.Model,
                PromptTokens = memory.PromptTokens,
                ResponseTokens = memory.ResponseTokens,
            },
            _ => new HarnessGameRuntimeEventTrace
            {
                EventName = runtimeEvent.EventName,
                OccurredAt = runtimeEvent.OccurredAt,
            }
        };

    private static HarnessGameVisibleEventTrace ToVisibleEventTrace(VisibleGameEvent gameEvent) =>
        new(gameEvent.EventId.ToString(), gameEvent.Sequence, gameEvent.EventType, gameEvent.OccurredAt);

    private static HarnessGameCommunicationTrace ToCommunicationTrace(ParticipantFeedEntry entry) =>
        new()
        {
            Sequence = entry.Sequence,
            Kind = entry.Kind.ToString(),
            MessageId = entry.MessageId?.ToString(),
            LinkId = entry.LinkId?.ToString(),
            AuthorParticipantId = entry.Author?.ParticipantId.Value,
            AuthorKind = entry.Author?.Kind.ToString(),
            RecipientParticipantIds = entry.RecipientParticipantIds.Select(item => item.Value).ToArray(),
            Text = entry.Text,
            GameEventId = entry.GameEventId,
            GameEventSequence = entry.GameEventSequence,
            Summary = entry.Summary,
            CreatedAt = entry.CreatedAt,
        };

    private static HarnessGameFailureSurfaceTrace BuildFailureSurface(
        IReadOnlyList<IGameEvent> engineEvents,
        IReadOnlyList<MemorySummaryDecision> memoryDecisions) =>
        new()
        {
            AgentResponseRejected = engineEvents.OfType<AgentResponseRejectedEvent>()
                .Select(item => new HarnessGameAgentFailureTrace(
                    item.ParticipantId.Value,
                    item.PendingInputId.Value,
                    item.ReasonCode,
                    item.Reason,
                    item.Visibility.Kind.ToString(),
                    item.Sequence))
                .ToArray(),
            NoActionTaken = engineEvents.OfType<NoActionTakenEvent>()
                .Select(item => new HarnessGameNoActionTrace(
                    item.ParticipantId.Value,
                    item.PendingInputId.Value,
                    item.ReasonCode,
                    item.Sequence))
                .ToArray(),
            IntentCommandRejected = engineEvents.OfType<IntentCommandRejectedEvent>()
                .Select(item => new HarnessGameIntentCommandRejectedTrace(
                    item.CommandId.ToString(),
                    item.ReasonCode,
                    item.Reason,
                    item.Sequence))
                .ToArray(),
            GameAborted = engineEvents.OfType<GameAbortedEvent>()
                .Select(item => new HarnessGameAbortedTrace(
                    item.ReasonCode,
                    item.Sequence))
                .ToArray(),
            MemoryDecisionFlags = memoryDecisions
                .Where(item => item.ExceededTokenBudget || item.Trimmed || item.Retried || !string.IsNullOrWhiteSpace(item.RejectionReason))
                .Select(item => new HarnessGameMemoryDecisionFailureTrace(
                    item.ParticipantId,
                    item.RoundNumber,
                    item.ExceededTokenBudget,
                    item.Trimmed,
                    item.Retried,
                    item.RejectionReason,
                    item.SummaryContentHash))
                .ToArray(),
        };

    private static HarnessUsage SumUsage(IEnumerable<TokenUsage> usage)
    {
        var input = 0;
        var output = 0;
        foreach (var item in usage)
        {
            input += item.InputTokens;
            output += item.OutputTokens;
        }

        return new HarnessUsage(input, output);
    }

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
