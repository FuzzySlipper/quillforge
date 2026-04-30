using Den.RulesEngine;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class GameDiagnosticLogService : IGameDiagnosticLogService
{
    public const string PrivacyNotice =
        "Host-level diagnostic view. It may include private game facts, prompt previews, response previews, participant memory summaries, and provider/model metadata. Provider API keys and secrets are never included.";

    private readonly IGameRuntimeService _runtimeService;
    private readonly ITokenUsageTracker _tokenUsageTracker;

    public GameDiagnosticLogService(
        IGameRuntimeService runtimeService,
        ITokenUsageTracker tokenUsageTracker)
    {
        _runtimeService = runtimeService;
        _tokenUsageTracker = tokenUsageTracker;
    }

    public async Task<GameDiagnosticLogProjection> GetLogAsync(
        Guid sessionId,
        GameDiagnosticLogQuery? query = null,
        CancellationToken ct = default)
    {
        var normalizedQuery = NormalizeQuery(query);
        var runtime = await _runtimeService.LoadViewAsync(sessionId, ct);
        var events = new List<EventDraft>();
        var normalizedRequestedGameInstanceId = Normalize(normalizedQuery.RequestedGameInstanceId);
        var usage = _tokenUsageTracker.GetSessionUsage(sessionId);
        var now = DateTimeOffset.UtcNow;

        if (runtime is null)
        {
            events.Add(new EventDraft(
                now,
                GameDiagnosticLogLevel.Warning,
                GameDiagnosticLogCategory.Endpoint,
                "QuillForge.Web.Endpoints.GameEndpoints",
                "diagnostic_log_requested",
                "No game runtime has been persisted for this session.",
                Details: new Dictionary<string, string?> { ["sessionId"] = sessionId.ToString() }));

            if (normalizedRequestedGameInstanceId is null)
            {
                events.Add(TokenUsageDraft(now, usage));
            }

            return BuildProjection(sessionId, null, events, normalizedQuery with { RequestedGameInstanceId = normalizedRequestedGameInstanceId }, scopeMatchesActiveGame: normalizedRequestedGameInstanceId is null);
        }

        if (normalizedRequestedGameInstanceId is not null
            && !string.Equals(runtime.GameInstanceId, normalizedRequestedGameInstanceId, StringComparison.Ordinal))
        {
            events.Add(new EventDraft(
                now,
                GameDiagnosticLogLevel.Warning,
                GameDiagnosticLogCategory.Endpoint,
                "QuillForge.Web.Endpoints.GameEndpoints",
                "diagnostic_scope_mismatch",
                "The requested game diagnostic scope does not match the session's current game runtime; current runtime details were not included to avoid mixing game logs.",
                Details: new Dictionary<string, string?>
                {
                    ["sessionId"] = sessionId.ToString(),
                    ["requestedGameInstanceId"] = normalizedRequestedGameInstanceId,
                    ["currentGameInstanceId"] = runtime.GameInstanceId,
                    ["currentRuntimeStatus"] = runtime.Status.ToString(),
                }));
            return BuildProjection(sessionId, null, events, normalizedQuery with { RequestedGameInstanceId = normalizedRequestedGameInstanceId }, scopeMatchesActiveGame: false);
        }

        AddRuntimeSnapshot(events, runtime, now);
        AddRuntimeHealthEvents(events, runtime, now);
        AddHostRecords(events, runtime);
        AddEngineEvents(events, runtime);
        AddCommunicationEvents(events, runtime);
        AddPromptEnvelopeEvents(events, runtime, normalizedQuery.PromptPreviewCharacters);
        AddPromptCursorEvents(events, runtime);
        AddMemoryEvents(events, runtime, now);
        if (normalizedRequestedGameInstanceId is null)
        {
            events.Add(TokenUsageDraft(runtime.LastUpdatedAt ?? now, usage));
        }

        return BuildProjection(sessionId, runtime, events, normalizedQuery with { RequestedGameInstanceId = normalizedRequestedGameInstanceId }, scopeMatchesActiveGame: true);
    }

    private static GameDiagnosticLogProjection BuildProjection(
        Guid sessionId,
        GameRuntimeState? runtime,
        List<EventDraft> drafts,
        GameDiagnosticLogQuery query,
        bool scopeMatchesActiveGame)
    {
        var categoryFilter = query.Categories
            .Distinct()
            .OrderBy(item => item.ToString(), StringComparer.Ordinal)
            .ToArray();
        var categoryFilterSet = categoryFilter.ToHashSet();
        var ordered = drafts
            .OrderBy(item => item.Timestamp)
            .ThenBy(item => item.SortSequence)
            .ThenBy(item => item.Operation, StringComparer.Ordinal)
            .Select((item, index) => new GameDiagnosticLogEvent
            {
                Sequence = index + 1,
                Timestamp = item.Timestamp,
                Level = item.Level,
                Category = item.Category,
                Source = item.Source,
                Operation = item.Operation,
                Summary = item.Summary,
                ReasonCode = Normalize(item.ReasonCode),
                ParticipantId = Normalize(item.ParticipantId),
                ProviderAlias = Normalize(item.ProviderAlias),
                Model = Normalize(item.Model),
                PromptTokens = item.PromptTokens,
                ResponseTokens = item.ResponseTokens,
                PromptPreview = Normalize(item.PromptPreview),
                ResponsePreview = Normalize(item.ResponsePreview),
                Details = item.Details ?? new Dictionary<string, string?>(),
            })
            .ToArray();
        var totalEventCount = ordered.Length;
        var categoryFiltered = categoryFilterSet.Count == 0
            ? ordered
            : ordered.Where(item => categoryFilterSet.Contains(item.Category)).ToArray();
        var filteredEventCount = categoryFiltered.Length;
        var cursorFiltered = query.BeforeSequence is null
            ? categoryFiltered
            : categoryFiltered.Where(item => item.Sequence < query.BeforeSequence.Value).ToArray();
        var limited = ApplyLimit(cursorFiltered, query.Limit);
        var hasMore = query.Limit is not null && cursorFiltered.Length > limited.Length;
        var nextBeforeSequence = hasMore && limited.Length > 0
            ? limited.Min(item => item.Sequence)
            : (long?)null;

        return new GameDiagnosticLogProjection
        {
            SessionId = sessionId,
            HasGame = runtime is not null,
            GameInstanceId = runtime?.GameInstanceId,
            RequestedGameInstanceId = query.RequestedGameInstanceId,
            ScopeMatchesActiveGame = scopeMatchesActiveGame,
            TemplateId = runtime?.TemplateId,
            ModuleId = runtime?.ModuleId,
            RuntimeStatus = runtime?.Status.ToString(),
            PrivacyNotice = PrivacyNotice,
            Limit = query.Limit,
            BeforeSequence = query.BeforeSequence,
            Categories = categoryFilter,
            TotalEventCount = totalEventCount,
            FilteredEventCount = filteredEventCount,
            ReturnedEventCount = limited.Length,
            HasMore = hasMore,
            NextBeforeSequence = nextBeforeSequence,
            Events = limited,
        };
    }

    private static void AddRuntimeSnapshot(List<EventDraft> events, GameRuntimeState runtime, DateTimeOffset now)
    {
        var liveState = runtime.EngineSnapshot?.ToState();
        var waitingInputs = liveState?.PendingInputs
            .Where(input => input.Status == PendingInputStatus.Waiting)
            .ToArray() ?? [];
        var participantKinds = runtime.ParticipantBindings.ToDictionary(
            binding => binding.ParticipantId,
            binding => binding.Kind,
            StringComparer.Ordinal);
        var waitingHumanInputs = waitingInputs.Count(input =>
            participantKinds.TryGetValue(input.ParticipantId.Value, out var kind) && kind == GameRuntimeParticipantKind.Human);
        var waitingAgentInputs = waitingInputs.Count(input =>
            participantKinds.TryGetValue(input.ParticipantId.Value, out var kind) && kind == GameRuntimeParticipantKind.Agent);

        events.Add(new EventDraft(
            runtime.StartedAt ?? runtime.LastUpdatedAt ?? now,
            GameDiagnosticLogLevel.Info,
            GameDiagnosticLogCategory.RuntimeMutation,
            "QuillForge.Core.Models.GameRuntimeState",
            "runtime_snapshot",
            $"Runtime status is {runtime.Status}.",
            Details: new Dictionary<string, string?>
            {
                ["gameInstanceId"] = runtime.GameInstanceId,
                ["templateId"] = runtime.TemplateId,
                ["moduleId"] = runtime.ModuleId,
                ["moduleVersion"] = runtime.ModuleVersion,
                ["seed"] = runtime.Seed?.ToString(),
                ["startedAt"] = runtime.StartedAt?.ToString("O"),
                ["lastUpdatedAt"] = runtime.LastUpdatedAt?.ToString("O"),
                ["stageId"] = liveState?.Stage.StageId.Value,
                ["stageName"] = liveState?.Stage.DisplayName,
                ["participantCount"] = runtime.ParticipantBindings.Count.ToString(),
                ["waitingInputCount"] = waitingInputs.Length.ToString(),
                ["waitingHumanInputCount"] = waitingHumanInputs.ToString(),
                ["waitingAgentInputCount"] = waitingAgentInputs.ToString(),
                ["promptEnvelopeCount"] = runtime.PromptEnvelopes.Count.ToString(),
                ["publicChannelMessageCount"] = runtime.Communication.ChannelMessages.Count.ToString(),
                ["directMessageCount"] = runtime.Communication.DirectMessages.Count.ToString(),
                ["hostAllowsPublicMessages"] = runtime.HostAllowsPublicMessages.ToString(),
                ["hostAllowsDirectMessages"] = runtime.HostAllowsDirectMessages.ToString(),
            }));
    }

    private static void AddRuntimeHealthEvents(List<EventDraft> events, GameRuntimeState runtime, DateTimeOffset now)
    {
        var liveState = runtime.EngineSnapshot?.ToState();
        if (liveState is null)
        {
            return;
        }

        var waitingInputs = liveState.PendingInputs
            .Where(input => input.Status == PendingInputStatus.Waiting)
            .ToArray();
        var participantKinds = runtime.ParticipantBindings.ToDictionary(
            binding => binding.ParticipantId,
            binding => binding.Kind,
            StringComparer.Ordinal);
        var waitingAgentInputs = waitingInputs
            .Where(input => participantKinds.TryGetValue(input.ParticipantId.Value, out var kind) && kind == GameRuntimeParticipantKind.Agent)
            .ToArray();

        if (runtime.IsActive && waitingInputs.Length == 0)
        {
            var isDiscussionStage = liveState.Stage.AllowsPublicMessages;
            events.Add(new EventDraft(
                runtime.LastUpdatedAt ?? runtime.StartedAt ?? now,
                isDiscussionStage ? GameDiagnosticLogLevel.Info : GameDiagnosticLogLevel.Warning,
                isDiscussionStage ? GameDiagnosticLogCategory.Communication : GameDiagnosticLogCategory.RuntimeMutation,
                "QuillForge.Core.Services.GameDiagnosticLogService",
                isDiscussionStage ? "runtime_waiting_for_public_discussion" : "runtime_waiting_without_pending_inputs",
                isDiscussionStage
                    ? $"Runtime is active in discussion stage '{liveState.Stage.StageId.Value}' with no waiting inputs; public channel messages are currently the expected human interaction."
                    : $"Runtime is active in stage '{liveState.Stage.StageId.Value}' with no waiting inputs; host or rules orchestration may need to request the next action.",
                Details: new Dictionary<string, string?>
                {
                    ["runtimeStatus"] = runtime.Status.ToString(),
                    ["stageId"] = liveState.Stage.StageId.Value,
                    ["stageName"] = liveState.Stage.DisplayName,
                    ["allowsPublicMessages"] = liveState.Stage.AllowsPublicMessages.ToString(),
                    ["promptEnvelopeCount"] = runtime.PromptEnvelopes.Count.ToString(),
                }));
        }

        if (waitingAgentInputs.Length > 0)
        {
            events.Add(new EventDraft(
                runtime.LastUpdatedAt ?? runtime.StartedAt ?? now,
                GameDiagnosticLogLevel.Warning,
                GameDiagnosticLogCategory.AgentPrompt,
                "QuillForge.Core.Services.GameDiagnosticLogService",
                "pending_agent_turns_waiting",
                $"{waitingAgentInputs.Length} agent pending input(s) are waiting for the game agent turn runner.",
                Details: new Dictionary<string, string?>
                {
                    ["participantIds"] = string.Join(",", waitingAgentInputs.Select(input => input.ParticipantId.Value).Distinct().Order(StringComparer.Ordinal)),
                    ["pendingInputIds"] = string.Join(",", waitingAgentInputs.Select(input => input.PendingInputId.Value).Order(StringComparer.Ordinal)),
                    ["promptEnvelopeCount"] = runtime.PromptEnvelopes.Count.ToString(),
                }));
        }
    }

    private static void AddHostRecords(List<EventDraft> events, GameRuntimeState runtime)
    {
        foreach (var record in runtime.HostRecords)
        {
            var isRejected = record.Kind == GameRuntimeHostRecordKind.CommunicationRejected
                || record.ReasonCode.Contains("reject", StringComparison.OrdinalIgnoreCase)
                || record.Summary.Contains("rejected", StringComparison.OrdinalIgnoreCase);
            events.Add(new EventDraft(
                record.OccurredAt,
                isRejected ? GameDiagnosticLogLevel.Warning : GameDiagnosticLogLevel.Info,
                isRejected ? GameDiagnosticLogCategory.Rejection : GameDiagnosticLogCategory.Service,
                "QuillForge.Core.Services.GameRuntimeService",
                record.Kind.ToString(),
                record.Summary,
                SortSequence: record.Sequence,
                ReasonCode: record.ReasonCode,
                Details: new Dictionary<string, string?>
                {
                    ["recordId"] = record.RecordId.ToString(),
                    ["hostRecordSequence"] = record.Sequence.ToString(),
                    ["gameInstanceId"] = runtime.GameInstanceId,
                    ["sourceSessionId"] = record.SourceSessionId?.ToString(),
                    ["targetSessionId"] = record.TargetSessionId?.ToString(),
                }));

            events.Add(new EventDraft(
                record.OccurredAt,
                isRejected ? GameDiagnosticLogLevel.Warning : GameDiagnosticLogLevel.Info,
                GameDiagnosticLogCategory.Persistence,
                "QuillForge.Storage.FileSystem.FileSystemSessionRuntimeStore",
                "session_state_persisted",
                $"Session runtime state persisted after {record.Kind}.",
                SortSequence: record.Sequence + 0.1,
                ReasonCode: record.ReasonCode,
                Details: new Dictionary<string, string?>
                {
                    ["hostRecordSequence"] = record.Sequence.ToString(),
                    ["persistenceBoundary"] = "ISessionStateStore.SaveAsync",
                }));
        }
    }

    private static void AddEngineEvents(List<EventDraft> events, GameRuntimeState runtime)
    {
        var liveState = runtime.EngineSnapshot?.ToState();
        if (liveState is null)
        {
            events.Add(new EventDraft(
                runtime.LastUpdatedAt ?? runtime.StartedAt ?? DateTimeOffset.UtcNow,
                GameDiagnosticLogLevel.Warning,
                GameDiagnosticLogCategory.RulesEngine,
                "Den.RulesEngine",
                "engine_snapshot_missing",
                "No rules-engine snapshot is available in the persisted runtime.",
                Details: new Dictionary<string, string?> { ["runtimeStatus"] = runtime.Status.ToString() }));
            return;
        }

        foreach (var gameEvent in liveState.EventJournal.Events)
        {
            var facts = GameEventIntrospection.Inspect(gameEvent);
            var isRejection = IsRejectionEvent(gameEvent, facts);
            events.Add(new EventDraft(
                gameEvent.OccurredAt,
                isRejection ? GameDiagnosticLogLevel.Warning : GameDiagnosticLogLevel.Info,
                isRejection ? GameDiagnosticLogCategory.Rejection : GameDiagnosticLogCategory.RulesEngine,
                "Den.RulesEngine.GameEventJournal",
                gameEvent.GetType().Name,
                BuildEngineSummary(gameEvent, facts),
                SortSequence: gameEvent.Sequence,
                ReasonCode: facts.ReasonCode,
                ParticipantId: facts.ParticipantId,
                Details: new Dictionary<string, string?>
                {
                    ["eventId"] = gameEvent.EventId.ToString(),
                    ["engineSequence"] = gameEvent.Sequence.ToString(),
                    ["visibility"] = gameEvent.Visibility.Kind.ToString(),
                    ["pendingInputId"] = facts.PendingInputId,
                    ["outcomeName"] = facts.OutcomeName,
                }));
        }

        foreach (var input in liveState.PendingInputs.OrderBy(item => item.PendingInputId.Value, StringComparer.Ordinal))
        {
            events.Add(new EventDraft(
                runtime.LastUpdatedAt ?? runtime.StartedAt ?? DateTimeOffset.UtcNow,
                input.Status == PendingInputStatus.Waiting ? GameDiagnosticLogLevel.Info : GameDiagnosticLogLevel.Warning,
                GameDiagnosticLogCategory.RuntimeMutation,
                "Den.RulesEngine.PendingInputState",
                $"pending_input_{input.Status.ToString().ToLowerInvariant()}",
                $"Pending input {input.PendingInputId.Value} for {input.ParticipantId.Value} is {input.Status}.",
                ParticipantId: input.ParticipantId.Value,
                Details: new Dictionary<string, string?>
                {
                    ["pendingInputId"] = input.PendingInputId.Value,
                    ["stageId"] = input.StageId.Value,
                    ["intentName"] = input.IntentName,
                    ["legalChoices"] = string.Join(",", input.LegalOptions.Select(option => option.IntentName)),
                }));
        }
    }

    private static void AddCommunicationEvents(List<EventDraft> events, GameRuntimeState runtime)
    {
        foreach (var message in runtime.Communication.ChannelMessages)
        {
            events.Add(new EventDraft(
                message.CreatedAt,
                GameDiagnosticLogLevel.Info,
                GameDiagnosticLogCategory.Communication,
                "QuillForge.Core.Services.ParticipantChannelService",
                "public_message_posted",
                $"Public message posted by {message.Author.ParticipantId.Value}.",
                SortSequence: message.Sequence,
                ParticipantId: message.Author.ParticipantId.Value,
                Details: new Dictionary<string, string?>
                {
                    ["messageId"] = message.MessageId.ToString(),
                    ["communicationSequence"] = message.Sequence.ToString(),
                    ["authorKind"] = message.Author.Kind.ToString(),
                    ["textPreview"] = Preview(message.Text, 300),
                }));
        }

        foreach (var message in runtime.Communication.DirectMessages)
        {
            events.Add(new EventDraft(
                message.CreatedAt,
                GameDiagnosticLogLevel.Info,
                GameDiagnosticLogCategory.Communication,
                "QuillForge.Core.Services.ParticipantChannelService",
                "direct_message_sent",
                $"Direct message sent by {message.Author.ParticipantId.Value}.",
                SortSequence: message.Sequence,
                ParticipantId: message.Author.ParticipantId.Value,
                Details: new Dictionary<string, string?>
                {
                    ["messageId"] = message.MessageId.ToString(),
                    ["communicationSequence"] = message.Sequence.ToString(),
                    ["recipientParticipantIds"] = string.Join(",", message.RecipientParticipantIds.Select(id => id.Value)),
                    ["authorKind"] = message.Author.Kind.ToString(),
                    ["textPreview"] = Preview(message.Text, 300),
                }));
        }

        foreach (var link in runtime.Communication.GameEventLinks)
        {
            events.Add(new EventDraft(
                link.CreatedAt,
                GameDiagnosticLogLevel.Info,
                GameDiagnosticLogCategory.Communication,
                "QuillForge.Core.Services.ParticipantChannelService",
                "game_event_linked_to_feed",
                link.Summary,
                SortSequence: link.Sequence,
                Details: new Dictionary<string, string?>
                {
                    ["linkId"] = link.LinkId.ToString(),
                    ["gameEventId"] = link.GameEventId,
                    ["gameEventSequence"] = link.GameEventSequence?.ToString(),
                    ["communicationSequence"] = link.Sequence.ToString(),
                    ["visibility"] = link.Visibility.ToString(),
                    ["visibleToParticipantIds"] = string.Join(",", link.VisibleToParticipantIds.Select(id => id.Value)),
                }));
        }
    }

    private static void AddPromptEnvelopeEvents(
        List<EventDraft> events,
        GameRuntimeState runtime,
        int promptPreviewCharacters)
    {
        var maxPromptPreview = Math.Clamp(promptPreviewCharacters, 0, 8000);
        foreach (var envelope in runtime.PromptEnvelopes)
        {
            events.Add(new EventDraft(
                envelope.CreatedAt,
                GameDiagnosticLogLevel.Info,
                GameDiagnosticLogCategory.LlmProvider,
                "QuillForge.Core.Services.GameAgentTurnService",
                "llm_request_response_recorded",
                $"Agent prompt envelope recorded for {envelope.ParticipantId}.",
                ParticipantId: envelope.ParticipantId,
                ProviderAlias: envelope.ProviderAlias,
                Model: envelope.Model,
                PromptTokens: envelope.PromptTokens,
                ResponseTokens: envelope.ResponseTokens,
                PromptPreview: Preview(envelope.PromptText, maxPromptPreview),
                ResponsePreview: Preview(envelope.ResponseText, 1200),
                Details: new Dictionary<string, string?>
                {
                    ["envelopeId"] = envelope.EnvelopeId,
                    ["engineCursorSequence"] = envelope.EngineCursorSequence.ToString(),
                    ["communicationCursorSequence"] = envelope.CommunicationCursorSequence.ToString(),
                    ["memoryRevision"] = envelope.MemoryRevision.ToString(),
                    ["promptContentHash"] = envelope.PromptContentHash,
                    ["responseContentHash"] = envelope.ResponseContentHash,
                    ["streamEvents"] = "Game agent turns use non-streaming completion; chat SSE stream events are outside this game runtime path.",
                }));
        }
    }

    private static void AddPromptCursorEvents(List<EventDraft> events, GameRuntimeState runtime)
    {
        foreach (var cursor in runtime.PromptCursors)
        {
            events.Add(new EventDraft(
                runtime.LastUpdatedAt ?? runtime.StartedAt ?? DateTimeOffset.UtcNow,
                GameDiagnosticLogLevel.Info,
                GameDiagnosticLogCategory.AgentPrompt,
                "QuillForge.Core.Models.GameRuntimeAgentPromptDeliveryCursor",
                "agent_prompt_cursor",
                $"Agent prompt cursor for {cursor.ParticipantId} delivered through engine #{cursor.LastDeliveredPublicEngineEventSequence} and communication #{cursor.CommunicationDeliveredThroughSequence}.",
                ParticipantId: cursor.ParticipantId,
                Details: new Dictionary<string, string?>
                {
                    ["lastDeliveredPublicEngineEventSequence"] = cursor.LastDeliveredPublicEngineEventSequence.ToString(),
                    ["deliveredPrivateEventIds"] = string.Join(",", cursor.DeliveredPrivateEventIds),
                    ["communicationDeliveredThroughSequence"] = cursor.CommunicationDeliveredThroughSequence.ToString(),
                    ["memoryRevision"] = cursor.MemoryRevision.ToString(),
                    ["lastPromptEnvelopeId"] = cursor.LastPromptEnvelopeId,
                }));
        }

        foreach (var cursor in runtime.EventDeliveryCursors)
        {
            events.Add(new EventDraft(
                runtime.LastUpdatedAt ?? runtime.StartedAt ?? DateTimeOffset.UtcNow,
                GameDiagnosticLogLevel.Info,
                GameDiagnosticLogCategory.AgentPrompt,
                "QuillForge.Core.Models.GameRuntimeEventDeliveryCursor",
                "participant_event_delivery_cursor",
                $"Participant cursor for {cursor.ParticipantId} delivered through engine #{cursor.DeliveredThroughEngineEventSequence} and communication #{cursor.DeliveredThroughCommunicationSequence}.",
                ParticipantId: cursor.ParticipantId,
                Details: new Dictionary<string, string?>
                {
                    ["deliveredThroughEngineEventSequence"] = cursor.DeliveredThroughEngineEventSequence.ToString(),
                    ["deliveredThroughCommunicationSequence"] = cursor.DeliveredThroughCommunicationSequence.ToString(),
                    ["memoryRevision"] = cursor.MemoryRevision.ToString(),
                    ["lastPromptEnvelopeId"] = cursor.LastPromptEnvelopeId,
                }));
        }
    }

    private static void AddMemoryEvents(List<EventDraft> events, GameRuntimeState runtime, DateTimeOffset now)
    {
        foreach (var memory in runtime.AgentMemories)
        {
            events.Add(new EventDraft(
                memory.UpdatedAt ?? runtime.LastUpdatedAt ?? now,
                GameDiagnosticLogLevel.Info,
                GameDiagnosticLogCategory.AgentPrompt,
                "QuillForge.Core.Models.GameRuntimeAgentMemoryState",
                "agent_memory_state",
                $"Agent memory for {memory.ParticipantId} is at revision {memory.Revision}.",
                ParticipantId: memory.ParticipantId,
                Details: new Dictionary<string, string?>
                {
                    ["revision"] = memory.Revision.ToString(),
                    ["tokenBudget"] = memory.TokenBudget.ToString(),
                    ["contentHash"] = memory.ContentHash,
                    ["summaryPreview"] = Preview(memory.Summary, 600),
                    ["lastSummarizedRoundNumber"] = memory.LastSummarizedRoundNumber.ToString(),
                    ["lastSummarizedPublicEngineEventSequence"] = memory.LastSummarizedPublicEngineEventSequence.ToString(),
                    ["lastSummarizedPrivateEventIds"] = string.Join(",", memory.LastSummarizedPrivateEventIds),
                    ["lastSummarizedCommunicationSequence"] = memory.LastSummarizedCommunicationSequence.ToString(),
                }));
        }

        foreach (var decision in runtime.MemorySummaryDecisions)
        {
            events.Add(new EventDraft(
                decision.CreatedAt,
                decision.RejectionReason is null ? GameDiagnosticLogLevel.Info : GameDiagnosticLogLevel.Warning,
                decision.RejectionReason is null ? GameDiagnosticLogCategory.AgentPrompt : GameDiagnosticLogCategory.Rejection,
                "QuillForge.Core.Services.GameAgentMemoryService",
                decision.RejectionReason is null ? "agent_memory_summary_decision" : "agent_memory_summary_rejected",
                decision.RejectionReason is null
                    ? $"Agent memory summary decision recorded for {decision.ParticipantId}."
                    : $"Agent memory summary rejected for {decision.ParticipantId}: {decision.RejectionReason}.",
                ReasonCode: decision.RejectionReason,
                ParticipantId: decision.ParticipantId,
                ProviderAlias: decision.ProviderAlias,
                Model: decision.Model,
                PromptTokens: decision.PromptTokens,
                ResponseTokens: decision.ResponseTokens,
                Details: new Dictionary<string, string?>
                {
                    ["decisionId"] = decision.DecisionId,
                    ["roundNumber"] = decision.RoundNumber.ToString(),
                    ["summaryContentHash"] = decision.SummaryContentHash,
                    ["exceededTokenBudget"] = decision.ExceededTokenBudget.ToString(),
                    ["trimmed"] = decision.Trimmed.ToString(),
                    ["retried"] = decision.Retried.ToString(),
                    ["snapshotId"] = decision.SnapshotId,
                }));
        }
    }

    private static EventDraft TokenUsageDraft(DateTimeOffset timestamp, SessionUsageSummary usage) =>
        new(
            timestamp,
            GameDiagnosticLogLevel.Info,
            GameDiagnosticLogCategory.TokenUsage,
            "QuillForge.Core.Services.ITokenUsageTracker",
            "session_token_usage",
            $"Tracked {usage.TotalRequests} LLM request(s), {usage.TotalInputTokens} input tokens, {usage.TotalOutputTokens} output tokens.",
            Details: new Dictionary<string, string?>
            {
                ["totalRequests"] = usage.TotalRequests.ToString(),
                ["totalInputTokens"] = usage.TotalInputTokens.ToString(),
                ["totalOutputTokens"] = usage.TotalOutputTokens.ToString(),
                ["byAgent"] = string.Join(";", usage.ByAgent.Select(item => $"{item.AgentName}:{item.RequestCount}/{item.InputTokens}in/{item.OutputTokens}out")),
            });

    private static string BuildEngineSummary(IGameEvent gameEvent, GameEventIntrospectionFacts facts)
    {
        if (!string.IsNullOrWhiteSpace(facts.ReasonCode))
        {
            return $"{gameEvent.GetType().Name} committed with reason '{facts.ReasonCode}'.";
        }

        if (!string.IsNullOrWhiteSpace(facts.OutcomeName))
        {
            return $"{gameEvent.GetType().Name} committed with outcome '{facts.OutcomeName}'.";
        }

        return $"{gameEvent.GetType().Name} committed to the rules-engine journal.";
    }

    private static bool IsRejectionEvent(IGameEvent gameEvent, GameEventIntrospectionFacts facts) =>
        !string.IsNullOrWhiteSpace(facts.ReasonCode)
        || gameEvent.GetType().Name.Contains("Rejected", StringComparison.Ordinal)
        || gameEvent.GetType().Name.Contains("NoAction", StringComparison.Ordinal);

    private static string? Preview(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Replace("\r", string.Empty, StringComparison.Ordinal).Trim();
        if (maxLength <= 0)
        {
            return null;
        }

        return normalized.Length <= maxLength ? normalized : normalized[..maxLength] + "…";
    }

    private static GameDiagnosticLogQuery NormalizeQuery(GameDiagnosticLogQuery? query)
    {
        query ??= new GameDiagnosticLogQuery();
        var limit = query.Limit is null
            ? (int?)null
            : Math.Clamp(query.Limit.Value, 1, GameDiagnosticLogQuery.MaxLimit);
        var beforeSequence = query.BeforeSequence is > 0 ? query.BeforeSequence : null;
        var promptPreviewCharacters = Math.Clamp(query.PromptPreviewCharacters, 0, 10000);
        var categories = query.Categories
            .Distinct()
            .OrderBy(item => item.ToString(), StringComparer.Ordinal)
            .ToArray();

        return query with
        {
            PromptPreviewCharacters = promptPreviewCharacters,
            Limit = limit,
            BeforeSequence = beforeSequence,
            Categories = categories,
            RequestedGameInstanceId = Normalize(query.RequestedGameInstanceId),
        };
    }

    private static GameDiagnosticLogEvent[] ApplyLimit(GameDiagnosticLogEvent[] events, int? limit)
    {
        if (limit is null || events.Length <= limit.Value)
        {
            return events;
        }

        return events.Skip(events.Length - limit.Value).ToArray();
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed record EventDraft(
        DateTimeOffset Timestamp,
        GameDiagnosticLogLevel Level,
        GameDiagnosticLogCategory Category,
        string Source,
        string Operation,
        string Summary,
        double SortSequence = 0,
        string? ReasonCode = null,
        string? ParticipantId = null,
        string? ProviderAlias = null,
        string? Model = null,
        int? PromptTokens = null,
        int? ResponseTokens = null,
        string? PromptPreview = null,
        string? ResponsePreview = null,
        IReadOnlyDictionary<string, string?>? Details = null);
}
