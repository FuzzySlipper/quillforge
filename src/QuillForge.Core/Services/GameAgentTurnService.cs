using System.Text;
using System.Text.Json;
using Den.RulesEngine;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class GameAgentTurnService : IGameAgentTurnService
{
    private const string DefaultModelName = "default";
    private readonly IGameRuntimeService _runtimeService;
    private readonly GameModuleRegistry _moduleRegistry;
    private readonly ICompletionService _completionService;
    private readonly AgentVisibleEventsService _visibleEventsService;
    private readonly IGamePromptTemplateService _promptTemplateService;
    private readonly IGamePersonaPromptService _personaPromptService;
    private readonly AppConfig _appConfig;
    private readonly ILogger<GameAgentTurnService> _logger;

    public GameAgentTurnService(
        IGameRuntimeService runtimeService,
        GameModuleRegistry moduleRegistry,
        ICompletionService completionService,
        AgentVisibleEventsService visibleEventsService,
        IGamePromptTemplateService promptTemplateService,
        IGamePersonaPromptService personaPromptService,
        AppConfig appConfig,
        ILogger<GameAgentTurnService> logger)
    {
        _runtimeService = runtimeService;
        _moduleRegistry = moduleRegistry;
        _completionService = completionService;
        _visibleEventsService = visibleEventsService;
        _promptTemplateService = promptTemplateService;
        _personaPromptService = personaPromptService;
        _appConfig = appConfig;
        _logger = logger;
    }

    public async Task<SessionMutationResult<GameAgentTurnRunResult>> RunPendingAgentTurnsAsync(
        Guid sessionId,
        RunGameAgentTurnsCommand command,
        CancellationToken ct = default)
    {
        var runtime = await _runtimeService.LoadViewAsync(sessionId, ct);
        if (runtime?.EngineSnapshot is null || string.IsNullOrWhiteSpace(runtime.GameInstanceId))
        {
            return SessionMutationResult<GameAgentTurnRunResult>.Invalid("No game runtime is available for this session.");
        }

        var liveState = runtime.EngineSnapshot.ToState();
        var module = _moduleRegistry.Find(liveState.ModuleId, liveState.ModuleVersion);
        if (module is null)
        {
            return SessionMutationResult<GameAgentTurnRunResult>.Invalid("Registered game module is not available for this runtime.");
        }

        var jobs = FindPendingAgentJobs(runtime, liveState);
        if (jobs.Count == 0)
        {
            return SessionMutationResult<GameAgentTurnRunResult>.Success(
                new GameAgentTurnRunResult(runtime, [], [], []));
        }

        var maxConcurrency = Math.Max(1, command.MaxConcurrency ?? _appConfig.Agents.GameAgentTurns.MaxConcurrency);
        var responseTimeout = command.ResponseTimeout
            ?? TimeSpan.FromSeconds(Math.Max(1, _appConfig.Agents.GameAgentTurns.ResponseTimeoutSeconds));
        var completed = await CompleteAgentJobsAsync(
            sessionId,
            runtime,
            liveState,
            module,
            jobs,
            maxConcurrency,
            responseTimeout,
            command.OccurredAt,
            ct);

        var participantResults = new List<GameAgentTurnParticipantResult>();
        var runtimeEvents = new List<IGameRuntimeEvent>();
        var engineEvents = new List<IGameEvent>();

        foreach (var completion in completed.OrderBy(item => item.Binding.ParticipantId, StringComparer.Ordinal)
                     .ThenBy(item => item.PendingInput.PendingInputId.Value, StringComparer.Ordinal))
        {
            var promptResult = await _runtimeService.RecordAgentPromptAsync(
                sessionId,
                new RecordGameRuntimeAgentPromptCommand(
                    completion.EnvelopeId,
                    completion.Binding.ParticipantId,
                    command.OccurredAt,
                    completion.Prompt.EngineCursorSequence,
                    completion.Prompt.DeliveredPrivateEventIds,
                    completion.Prompt.CommunicationCursorSequence,
                    completion.Prompt.MemoryRevision,
                    completion.ProviderAlias,
                    completion.Model,
                    completion.Response.Usage.InputTokens,
                    completion.Response.Usage.OutputTokens,
                    completion.Prompt.PromptContentHash,
                    StableContentHash(completion.Response.Content.GetText()),
                    completion.Prompt.SystemPrompt + "\n---\n" + completion.Prompt.UserPrompt,
                    completion.Response.Content.GetText(),
                    Math.Max(1, _appConfig.Agents.GameAgentTurns.MaxPromptEnvelopesPerAgent)),
                ct);
            if (promptResult.Status == SessionMutationStatus.Success && promptResult.Value is not null)
            {
                runtimeEvents.AddRange(promptResult.Value.RuntimeEvents);
            }

            var apply = await ApplyAgentCompletionAsync(sessionId, completion, command.OccurredAt, ct);
            participantResults.Add(apply.ParticipantResult);
            runtimeEvents.AddRange(apply.RuntimeEvents);
            engineEvents.AddRange(apply.EngineEvents);
        }

        var updatedRuntime = await _runtimeService.LoadViewAsync(sessionId, ct);
        return SessionMutationResult<GameAgentTurnRunResult>.Success(
            new GameAgentTurnRunResult(updatedRuntime, participantResults, runtimeEvents, engineEvents));
    }

    private async Task<IReadOnlyList<AgentJobCompletion>> CompleteAgentJobsAsync(
        Guid sessionId,
        GameRuntimeState runtime,
        RulesGameState liveState,
        IGameModule module,
        IReadOnlyList<AgentPendingInputJob> jobs,
        int maxConcurrency,
        TimeSpan responseTimeout,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        var tasks = jobs.Select(async job =>
        {
            await semaphore.WaitAsync(ct);
            try
            {
                return await CompleteAgentJobAsync(sessionId, runtime, liveState, module, job, responseTimeout, occurredAt, ct);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToArray();

        return await Task.WhenAll(tasks);
    }

    private async Task<AgentJobCompletion> CompleteAgentJobAsync(
        Guid sessionId,
        GameRuntimeState runtime,
        RulesGameState liveState,
        IGameModule module,
        AgentPendingInputJob job,
        TimeSpan responseTimeout,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var context = await BuildPromptContextAsync(runtime, liveState, module, job.Binding, job.PendingInput, ct);
        var prompt = BuildPrompt(context);
        var model = Normalize(job.Binding.ModelOverride) ?? DefaultModelName;
        var providerAlias = Normalize(job.Binding.ProviderAlias);
        var envelopeId = $"game-agent-{Guid.CreateVersion7():N}";
        if (providerAlias is null)
        {
            var response = new CompletionResponse
            {
                Content = new MessageContent(""),
                StopReason = StopReason.Error,
                Usage = new TokenUsage(0, 0),
            };
            return new AgentJobCompletion(
                job.Binding,
                job.PendingInput,
                prompt,
                envelopeId,
                providerAlias,
                model,
                response,
                GameAgentResponseParseResult.Rejected("provider-level-failure", "Agent participant has no configured provider alias."));
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(responseTimeout);
            using var trackingScope = TokenTrackingScope.Begin(sessionId, $"game-agent:{job.Binding.ParticipantId}");
            var response = await _completionService.CompleteAsync(
                new CompletionRequest
                {
                    ProviderAlias = providerAlias,
                    Model = model,
                    MaxTokens = _appConfig.Agents.GameAgentTurns.MaxTokens,
                    Temperature = 0.2,
                    Tools = [],
                    SystemPrompt = prompt.SystemPrompt,
                    Messages = [new CompletionMessage("user", new MessageContent(prompt.UserPrompt))],
                },
                timeout.Token);
            return new AgentJobCompletion(
                job.Binding,
                job.PendingInput,
                prompt,
                envelopeId,
                providerAlias,
                model,
                response,
                ParseAgentResponse(response));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(
                "Game agent response timed out: session={SessionId} participant={ParticipantId} pendingInput={PendingInputId}",
                sessionId,
                job.Binding.ParticipantId,
                job.PendingInput.PendingInputId.Value);
            var response = new CompletionResponse
            {
                Content = new MessageContent(""),
                StopReason = StopReason.Error,
                Usage = new TokenUsage(0, 0),
            };
            return new AgentJobCompletion(
                job.Binding,
                job.PendingInput,
                prompt,
                envelopeId,
                providerAlias,
                model,
                response,
                GameAgentResponseParseResult.Rejected("retry-exhaustion", "Agent response timed out before a legal action was produced."));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Game agent provider call failed: session={SessionId} participant={ParticipantId} provider={ProviderAlias}",
                sessionId,
                job.Binding.ParticipantId,
                providerAlias);
            var response = new CompletionResponse
            {
                Content = new MessageContent(""),
                StopReason = StopReason.Error,
                Usage = new TokenUsage(0, 0),
            };
            return new AgentJobCompletion(
                job.Binding,
                job.PendingInput,
                prompt,
                envelopeId,
                providerAlias,
                model,
                response,
                GameAgentResponseParseResult.Rejected("provider-level-failure", ex.Message));
        }
    }

    private async Task<AgentApplyResult> ApplyAgentCompletionAsync(
        Guid sessionId,
        AgentJobCompletion completion,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        if (!completion.ParseResult.IsAccepted
            || string.IsNullOrWhiteSpace(completion.ParseResult.PendingInputId)
            || string.IsNullOrWhiteSpace(completion.ParseResult.ChoiceName))
        {
            return await RejectAndNoActionAsync(
                sessionId,
                completion,
                NormalizeReasonCode(completion.ParseResult.ReasonCode),
                completion.ParseResult.Message,
                occurredAt,
                ct);
        }

        if (!string.Equals(
                completion.ParseResult.PendingInputId,
                completion.PendingInput.PendingInputId.Value,
                StringComparison.Ordinal))
        {
            return await RejectAndNoActionAsync(
                sessionId,
                completion,
                "schema-fail",
                "Agent response targeted a different pending input than the one it was prompted to answer.",
                occurredAt,
                ct);
        }

        var currentRuntime = await _runtimeService.LoadViewAsync(sessionId, ct);
        if (currentRuntime?.EngineSnapshot is null)
        {
            return await CreateLocalRejectedResultAsync(completion, "out-of-stage", "Game runtime ended before the agent response could be applied.");
        }

        var engineCommand = new SubmitPlayerChoiceIntentCommand(
            GameIntentCommandId.NewId(),
            currentRuntime.EngineSnapshot.GameInstanceId,
            completion.PendingInput.PendingInputId,
            completion.PendingInput.ParticipantId,
            completion.ParseResult.ChoiceName.Trim());
        var validation = GameIntentCommandValidationService.Validate(currentRuntime.EngineSnapshot.ToState(), engineCommand);
        if (!validation.IsAccepted)
        {
            var issue = validation.Issues[0];
            return await RejectAndNoActionAsync(
                sessionId,
                completion,
                ToAgentRejectionReason(issue.Code),
                issue.Message,
                occurredAt,
                ct);
        }

        var result = await _runtimeService.ApplyEngineCommandAsync(
            sessionId,
            new ApplyGameRuntimeEngineCommand(engineCommand, occurredAt),
            ct);
        if (result.Status != SessionMutationStatus.Success || result.Value is null)
        {
            return await RejectAndNoActionAsync(
                sessionId,
                completion,
                ToAgentRejectionReason(ExtractReasonCode(result.Error)),
                result.Error ?? "Agent action was rejected by the engine.",
                occurredAt,
                ct);
        }

        return new AgentApplyResult(
            new GameAgentTurnParticipantResult(
                completion.Binding.ParticipantId,
                completion.PendingInput.PendingInputId.Value,
                GameAgentTurnOutcome.Applied,
                "applied",
                "Agent response applied as a typed player intent.",
                completion.ProviderAlias,
                completion.Model,
                completion.Response.Usage),
            result.Value.RuntimeEvents,
            result.Value.EngineEvents);
    }

    private async Task<AgentApplyResult> RejectAndNoActionAsync(
        Guid sessionId,
        AgentJobCompletion completion,
        string reasonCode,
        string message,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var runtimeEvents = new List<IGameRuntimeEvent>();
        var engineEvents = new List<IGameEvent>();
        var currentRuntime = await _runtimeService.LoadViewAsync(sessionId, ct);
        if (currentRuntime?.EngineSnapshot is not null)
        {
            var isSensitiveReason = IsSensitiveNoActionReason(reasonCode);
            var visibility = isSensitiveReason
                ? GameEventVisibility.HiddenSystemOnly
                : GameEventVisibility.PrivateToParticipant(completion.PendingInput.ParticipantId);
            var noActionVisibility = isSensitiveReason
                ? GameEventVisibility.HiddenSystemOnly
                : GameEventVisibility.Public;
            var rejection = await _runtimeService.ApplyEngineCommandAsync(
                sessionId,
                new ApplyGameRuntimeEngineCommand(
                    new RecordAgentResponseRejectedIntentCommand(
                        GameIntentCommandId.NewId(),
                        currentRuntime.EngineSnapshot.GameInstanceId,
                        completion.PendingInput.PendingInputId,
                        completion.PendingInput.ParticipantId,
                        reasonCode,
                        message,
                        visibility),
                    occurredAt),
                ct);
            if (rejection.Status == SessionMutationStatus.Success && rejection.Value is not null)
            {
                runtimeEvents.AddRange(rejection.Value.RuntimeEvents);
                engineEvents.AddRange(rejection.Value.EngineEvents);
            }

            var refreshed = await _runtimeService.LoadViewAsync(sessionId, ct);
            if (refreshed?.EngineSnapshot is not null)
            {
                var noAction = await _runtimeService.ApplyEngineCommandAsync(
                    sessionId,
                    new ApplyGameRuntimeEngineCommand(
                        new RecordNoActionTakenIntentCommand(
                            GameIntentCommandId.NewId(),
                            refreshed.EngineSnapshot.GameInstanceId,
                            completion.PendingInput.PendingInputId,
                            completion.PendingInput.ParticipantId,
                            reasonCode,
                            noActionVisibility),
                        occurredAt),
                    ct);
                if (noAction.Status == SessionMutationStatus.Success && noAction.Value is not null)
                {
                    runtimeEvents.AddRange(noAction.Value.RuntimeEvents);
                    engineEvents.AddRange(noAction.Value.EngineEvents);
                }
            }
        }

        return new AgentApplyResult(
            new GameAgentTurnParticipantResult(
                completion.Binding.ParticipantId,
                completion.PendingInput.PendingInputId.Value,
                GameAgentTurnOutcome.Rejected,
                reasonCode,
                message,
                completion.ProviderAlias,
                completion.Model,
                completion.Response.Usage),
            runtimeEvents,
            engineEvents);
    }

    private static Task<AgentApplyResult> CreateLocalRejectedResultAsync(
        AgentJobCompletion completion,
        string reasonCode,
        string message) =>
        Task.FromResult(new AgentApplyResult(
            new GameAgentTurnParticipantResult(
                completion.Binding.ParticipantId,
                completion.PendingInput.PendingInputId.Value,
                GameAgentTurnOutcome.Rejected,
                reasonCode,
                message,
                completion.ProviderAlias,
                completion.Model,
                completion.Response.Usage),
            [],
            []));

    private async Task<GameAgentPromptContext> BuildPromptContextAsync(
        GameRuntimeState runtime,
        RulesGameState liveState,
        IGameModule module,
        GameRuntimeParticipantBinding binding,
        PendingInputState pendingInput,
        CancellationToken ct)
    {
        var memory = runtime.AgentMemories.FirstOrDefault(item =>
            string.Equals(item.ParticipantId, binding.ParticipantId, StringComparison.Ordinal));
        var cursor = runtime.PromptCursors.FirstOrDefault(item =>
            string.Equals(item.ParticipantId, binding.ParticipantId, StringComparison.Ordinal));
        var visibleEvents = _visibleEventsService.BuildForPrompt(runtime, liveState, binding.ParticipantId, cursor);
        var pendingInputs = liveState.PendingInputs
            .Where(input => input.IsWaitingFor(pendingInput.ParticipantId))
            .ToArray();
        var promptTemplate = await _promptTemplateService.ResolveAsync(module, binding.SystemPromptTemplate, ct);
        var personaPrompt = await _personaPromptService.ResolveAsync(binding.PersonaPrompt, ct);
        return new GameAgentPromptContext(
            runtime.GameInstanceId!,
            binding.ParticipantId,
            binding.DisplayName,
            liveState.Stage.StageId.Value,
            liveState.Stage.DisplayName,
            module.Descriptor.DisplayName,
            module.GetPromptAssets(),
            promptTemplate.Content,
            personaPrompt.Content,
            visibleEvents,
            pendingInputs,
            memory,
            cursor,
            binding);
    }

    internal static GameAgentPromptAssembly BuildPrompt(GameAgentPromptContext context)
    {
        var rules = context.PromptAssets.Where(asset => asset.Kind == GamePromptAssetKind.RulesText).Select(asset => asset.Content).ToArray();
        var instructions = string.IsNullOrWhiteSpace(context.SystemPromptTemplateContent)
            ? Array.Empty<string>()
            : [context.SystemPromptTemplateContent];
        var pendingInput = context.PendingInputs.FirstOrDefault();
        var engineCursor = context.VisibleEvents.NewCursor.PublicEngineEventSequence;
        var deliveredPrivateEventIds = context.VisibleEvents.NewCursor.PrivateEngineEventIds;
        var communicationCursor = context.VisibleEvents.NewCursor.CommunicationSequence;
        var memoryRevision = context.Memory?.Revision ?? 0;

        var system = new StringBuilder();
        system.AppendLine("You are an autonomous game participant in QuillForge.");
        system.AppendLine("You are not the game master and must not invent or change rules, outcomes, hidden facts, participant structure, or stage flow.");
        system.AppendLine("Return only compact JSON. Do not include prose outside JSON.");
        system.AppendLine("Accepted response shape: {\"accepted\":true,\"pendingInputId\":\"...\",\"choiceName\":\"...\",\"message\":\"short rationale\"}");
        system.AppendLine("Rejected response shape: {\"accepted\":false,\"reasonCode\":\"hidden-info-attempt|parse-fail|schema-fail|illegal-action|out-of-stage|model-refusal\",\"message\":\"reason\"}");
        foreach (var text in instructions)
        {
            system.AppendLine();
            system.AppendLine(text);
        }

        var user = new StringBuilder();
        user.AppendLine($"Game: {context.ModuleDisplayName} ({context.GameInstanceId})");
        user.AppendLine($"Participant: {context.DisplayName} ({context.ParticipantId})");
        user.AppendLine($"Stage: {context.StageName} ({context.StageId})");
        if (!string.IsNullOrWhiteSpace(context.PersonaPromptContent))
        {
            user.AppendLine("Persona prompt:");
            user.AppendLine(context.PersonaPromptContent.Trim());
        }
        if (!string.IsNullOrWhiteSpace(context.Binding.Personality))
        {
            user.AppendLine($"Legacy personality: {context.Binding.Personality}");
        }
        if (!string.IsNullOrWhiteSpace(context.Binding.CharacterPrompt))
        {
            user.AppendLine($"Legacy character prompt: {context.Binding.CharacterPrompt}");
        }
        user.AppendLine();
        user.AppendLine("Rules reference:");
        user.AppendLine(rules.Length == 0 ? "- No module rules reference was provided." : string.Join("\n", rules.Select(item => $"- {item}")));
        user.AppendLine();
        user.AppendLine("Prior memory:");
        user.AppendLine(string.IsNullOrWhiteSpace(context.Memory?.Summary) ? "- No prior memory summary." : context.Memory!.Summary);
        user.AppendLine();
        user.AppendLine("Visible engine facts:");
        AppendVisibleEvents(user, context.VisibleEvents.EngineEvents);
        user.AppendLine();
        user.AppendLine("Visible channel and direct-message feed:");
        AppendFeed(user, context.VisibleEvents.FeedEntries);
        user.AppendLine();
        user.AppendLine("Pending input to answer:");
        if (pendingInput is null)
        {
            user.AppendLine("- None. Return a rejected response with reasonCode schema-fail.");
        }
        else
        {
            user.AppendLine($"- pendingInputId: {pendingInput.PendingInputId.Value}");
            user.AppendLine($"- intentName: {pendingInput.IntentName}");
            user.AppendLine("- legal choices:");
            foreach (var option in pendingInput.LegalOptions)
            {
                user.AppendLine($"  - {option.IntentName}: {option.DisplayName} — {option.Description}");
            }
        }

        var systemText = system.ToString().Trim();
        var userText = user.ToString().Trim();
        return new GameAgentPromptAssembly(
            systemText,
            userText,
            engineCursor,
            deliveredPrivateEventIds,
            communicationCursor,
            memoryRevision,
            StableContentHash(systemText + "\n---\n" + userText));
    }

    internal static GameAgentResponseParseResult ParseAgentResponse(CompletionResponse response)
    {
        if (response.StopReason == StopReason.ContentFilter)
        {
            return GameAgentResponseParseResult.Rejected("model-refusal", "Agent response was blocked by provider safety filtering.");
        }

        if (response.StopReason == StopReason.ToolUse)
        {
            return GameAgentResponseParseResult.Rejected("schema-fail", "Agent player responses must not request tools.");
        }

        return ParseAgentResponse(response.Content.GetText());
    }

    internal static GameAgentResponseParseResult ParseAgentResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return GameAgentResponseParseResult.Rejected("parse-fail", "Agent response was empty.");
        }

        try
        {
            var dto = JsonSerializer.Deserialize<AgentResponseDto>(text, JsonOptions);
            if (dto is null)
            {
                return GameAgentResponseParseResult.Rejected("parse-fail", "Agent response JSON was empty.");
            }

            if (!dto.Accepted)
            {
                return GameAgentResponseParseResult.Rejected(
                    NormalizeReasonCode(dto.ReasonCode),
                    NormalizeMessage(dto.Message, "Agent rejected the pending input."));
            }

            if (string.IsNullOrWhiteSpace(dto.PendingInputId) || string.IsNullOrWhiteSpace(dto.ChoiceName))
            {
                return GameAgentResponseParseResult.Rejected("schema-fail", "Accepted agent response must include pendingInputId and choiceName.");
            }

            return GameAgentResponseParseResult.Accepted(
                dto.PendingInputId.Trim(),
                dto.ChoiceName.Trim(),
                NormalizeMessage(dto.Message, "Agent selected a legal option."));
        }
        catch (JsonException)
        {
            return GameAgentResponseParseResult.Rejected("parse-fail", "Agent response was not valid JSON.");
        }
    }

    private static IReadOnlyList<AgentPendingInputJob> FindPendingAgentJobs(
        GameRuntimeState runtime,
        RulesGameState liveState)
    {
        var agentBindings = runtime.ParticipantBindings
            .Where(binding => binding.Kind == GameRuntimeParticipantKind.Agent)
            .ToDictionary(binding => binding.ParticipantId, StringComparer.Ordinal);
        return liveState.PendingInputs
            .Where(input => input.Status == PendingInputStatus.Waiting)
            .Where(input => agentBindings.ContainsKey(input.ParticipantId.Value))
            .Select(input => new AgentPendingInputJob(agentBindings[input.ParticipantId.Value], input))
            .OrderBy(job => job.Binding.ParticipantId, StringComparer.Ordinal)
            .ThenBy(job => job.PendingInput.PendingInputId.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static void AppendVisibleEvents(StringBuilder builder, IReadOnlyList<VisibleGameEvent> events)
    {
        if (events.Count == 0)
        {
            builder.AppendLine("- No newly delivered engine facts.");
            return;
        }

        foreach (var item in events.OrderBy(item => item.Sequence))
        {
            builder.AppendLine($"- #{item.Sequence} {item.EventType} ({item.EventId}) at {item.OccurredAt:O}");
        }
    }

    private static void AppendFeed(StringBuilder builder, IReadOnlyList<ParticipantFeedEntry> feed)
    {
        if (feed.Count == 0)
        {
            builder.AppendLine("- No newly delivered channel or direct-message entries.");
            return;
        }

        foreach (var item in feed.OrderBy(item => item.Sequence))
        {
            var author = item.Author?.ParticipantId.Value ?? "system";
            var text = item.Text ?? item.Summary ?? item.GameEventId ?? item.Kind.ToString();
            builder.AppendLine($"- #{item.Sequence} {item.Kind} from {author}: {text}");
        }
    }

    private static string ToAgentRejectionReason(string reasonCode) => reasonCode switch
    {
        "illegal_choice" => "illegal-action",
        "out_of_stage" => "out-of-stage",
        "pending_input_not_available" => "out-of-stage",
        "unknown_pending_input" => "out-of-stage",
        "unknown_participant" => "schema-fail",
        "wrong_game_instance" => "out-of-stage",
        _ => NormalizeReasonCode(reasonCode),
    };

    private static string ExtractReasonCode(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return "illegal-action";
        }

        var index = error.IndexOf(':', StringComparison.Ordinal);
        return index <= 0 ? error.Trim() : error[..index].Trim();
    }

    private static string NormalizeReasonCode(string? reasonCode)
    {
        if (string.IsNullOrWhiteSpace(reasonCode))
        {
            return "parse-fail";
        }

        return reasonCode.Trim().Replace('_', '-');
    }

    private static bool IsSensitiveNoActionReason(string reasonCode) =>
        SensitiveNoActionReasonCodes.Contains(reasonCode);

    private static readonly HashSet<string> SensitiveNoActionReasonCodes = new(StringComparer.Ordinal)
    {
        "hidden-info-attempt",
    };

    private static string NormalizeMessage(string? message, string fallback) =>
        string.IsNullOrWhiteSpace(message) ? fallback : message.Trim();

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string StableContentHash(string content)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        var hash = offset;
        foreach (var ch in content)
        {
            hash ^= ch;
            hash *= prime;
        }

        return hash.ToString("x16");
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private sealed record AgentResponseDto
    {
        public bool Accepted { get; init; }
        public string? PendingInputId { get; init; }
        public string? ChoiceName { get; init; }
        public string? ReasonCode { get; init; }
        public string? Message { get; init; }
    }

    private sealed record AgentPendingInputJob(
        GameRuntimeParticipantBinding Binding,
        PendingInputState PendingInput);

    private sealed record AgentJobCompletion(
        GameRuntimeParticipantBinding Binding,
        PendingInputState PendingInput,
        GameAgentPromptAssembly Prompt,
        string EnvelopeId,
        string? ProviderAlias,
        string? Model,
        CompletionResponse Response,
        GameAgentResponseParseResult ParseResult);

    private sealed record AgentApplyResult(
        GameAgentTurnParticipantResult ParticipantResult,
        IReadOnlyList<IGameRuntimeEvent> RuntimeEvents,
        IReadOnlyList<IGameEvent> EngineEvents);
}
