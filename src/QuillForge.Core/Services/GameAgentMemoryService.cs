using System.Text;
using System.Text.Json;
using Den.RulesEngine;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class GameAgentMemoryService : IGameAgentMemoryService
{
    private const string DefaultModelName = "default";
    private readonly IGameRuntimeService _runtimeService;
    private readonly GameModuleRegistry _moduleRegistry;
    private readonly ICompletionService _completionService;
    private readonly AgentVisibleEventsService _visibleEventsService;
    private readonly AppConfig _appConfig;
    private readonly ILogger<GameAgentMemoryService> _logger;

    public GameAgentMemoryService(
        IGameRuntimeService runtimeService,
        GameModuleRegistry moduleRegistry,
        ICompletionService completionService,
        AgentVisibleEventsService visibleEventsService,
        AppConfig appConfig,
        ILogger<GameAgentMemoryService> logger)
    {
        _runtimeService = runtimeService;
        _moduleRegistry = moduleRegistry;
        _completionService = completionService;
        _visibleEventsService = visibleEventsService;
        _appConfig = appConfig;
        _logger = logger;
    }

    public async Task<SessionMutationResult<GameAgentMemorySummaryRunResult>> RunRoundEndMemorySummariesAsync(
        Guid sessionId,
        RunGameAgentMemorySummariesCommand command,
        CancellationToken ct = default)
    {
        var runtime = await _runtimeService.LoadViewAsync(sessionId, ct);
        if (runtime?.EngineSnapshot is null || string.IsNullOrWhiteSpace(runtime.GameInstanceId))
        {
            return SessionMutationResult<GameAgentMemorySummaryRunResult>.Invalid("No game runtime is available for this session.");
        }

        var liveState = runtime.EngineSnapshot.ToState();
        var module = _moduleRegistry.Find(liveState.ModuleId, liveState.ModuleVersion);
        if (module is null)
        {
            return SessionMutationResult<GameAgentMemorySummaryRunResult>.Invalid("Registered game module is not available for this runtime.");
        }

        var jobs = FindMemoryJobs(runtime, liveState, module);
        if (command.MaxSummaries is { } maxSummaries)
        {
            jobs = jobs.Take(Math.Max(0, maxSummaries)).ToArray();
        }

        if (jobs.Count == 0)
        {
            return SessionMutationResult<GameAgentMemorySummaryRunResult>.Success(
                new GameAgentMemorySummaryRunResult(runtime, [], []));
        }

        var participantResults = new List<GameAgentMemorySummaryParticipantResult>();
        var runtimeEvents = new List<IGameRuntimeEvent>();
        foreach (var job in jobs)
        {
            var completion = await CompleteMemoryJobAsync(sessionId, runtime, liveState, module, job, command.OccurredAt, ct);
            var record = await RecordMemoryCompletionAsync(sessionId, completion, command.OccurredAt, ct);
            participantResults.Add(record.ParticipantResult);
            runtimeEvents.AddRange(record.RuntimeEvents);
        }

        var updatedRuntime = await _runtimeService.LoadViewAsync(sessionId, ct);
        return SessionMutationResult<GameAgentMemorySummaryRunResult>.Success(
            new GameAgentMemorySummaryRunResult(updatedRuntime, participantResults, runtimeEvents));
    }

    internal static GameAgentMemorySummaryPromptAssembly BuildMemorySummaryPrompt(GameAgentMemorySummaryPromptContext context)
    {
        var rules = context.PromptAssets
            .Where(asset => asset.Kind == GamePromptAssetKind.RulesText)
            .Select(asset => asset.Content)
            .ToArray();
        var instructions = context.PromptAssets
            .Where(asset => asset.Kind == GamePromptAssetKind.ParticipantInstructions)
            .Select(asset => asset.Content)
            .ToArray();

        var system = new StringBuilder();
        system.AppendLine("You update imperfect private memory for one autonomous QuillForge game participant.");
        system.AppendLine("Summarize only facts visible in the provided AgentVisibleEvents snapshot plus prior memory.");
        system.AppendLine("Do not infer hidden roles, secret actions, outcomes, or facts that are not visible to this participant.");
        system.AppendLine("Return compact JSON only: {\"summary\":\"...\"}.");
        foreach (var text in instructions)
        {
            system.AppendLine();
            system.AppendLine(text);
        }

        var user = new StringBuilder();
        user.AppendLine($"Game: {context.ModuleDisplayName} ({context.GameInstanceId})");
        user.AppendLine($"Participant: {context.DisplayName} ({context.ParticipantId})");
        user.AppendLine($"Round ended: {context.RoundNumber}");
        user.AppendLine($"Memory token budget: {context.TokenBudget}");
        if (!string.IsNullOrWhiteSpace(context.Binding.Personality))
        {
            user.AppendLine($"Personality: {context.Binding.Personality}");
        }
        if (!string.IsNullOrWhiteSpace(context.Binding.CharacterPrompt))
        {
            user.AppendLine($"Character prompt: {context.Binding.CharacterPrompt}");
        }

        user.AppendLine();
        user.AppendLine("Rules reference:");
        user.AppendLine(rules.Length == 0 ? "- No module rules reference was provided." : string.Join("\n", rules.Select(item => $"- {item}")));
        user.AppendLine();
        user.AppendLine("Prior memory summary:");
        user.AppendLine(string.IsNullOrWhiteSpace(context.PriorMemorySummary) ? "- No prior memory summary." : context.PriorMemorySummary);
        user.AppendLine();
        user.AppendLine("New visible engine facts since the prior memory cursor:");
        AppendVisibleEvents(user, context.VisibleEvents.EngineEvents);
        user.AppendLine();
        user.AppendLine("New visible channel and direct-message feed since the prior memory cursor:");
        AppendFeed(user, context.VisibleEvents.FeedEntries);
        user.AppendLine();
        user.AppendLine("Write an updated memory summary in the participant's voice/perspective. Keep it concise and within the token budget.");

        var systemText = system.ToString().Trim();
        var userText = user.ToString().Trim();
        return new GameAgentMemorySummaryPromptAssembly(
            systemText,
            userText,
            context.VisibleEvents.PriorCursor,
            context.VisibleEvents.NewCursor,
            context.VisibleEvents.PriorCursor.MemoryRevision,
            StableContentHash(systemText + "\n---\n" + userText));
    }

    internal static GameAgentMemorySummaryParseResult ParseMemorySummaryResponse(CompletionResponse response)
    {
        if (response.StopReason == StopReason.ContentFilter)
        {
            return GameAgentMemorySummaryParseResult.Rejected("model-refusal", "Memory summary response was blocked by provider safety filtering.");
        }

        if (response.StopReason == StopReason.ToolUse)
        {
            return GameAgentMemorySummaryParseResult.Rejected("schema-fail", "Memory summary responses must not request tools.");
        }

        var text = response.Content.GetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            return GameAgentMemorySummaryParseResult.Rejected("parse-fail", "Memory summary response was empty.");
        }

        try
        {
            var dto = JsonSerializer.Deserialize<MemorySummaryDto>(text, JsonOptions);
            if (dto is null || string.IsNullOrWhiteSpace(dto.Summary))
            {
                return GameAgentMemorySummaryParseResult.Rejected("schema-fail", "Memory summary response must include a non-empty summary field.");
            }

            return GameAgentMemorySummaryParseResult.Accepted(dto.Summary.Trim());
        }
        catch (JsonException)
        {
            return GameAgentMemorySummaryParseResult.Rejected("parse-fail", "Memory summary response was not valid JSON.");
        }
    }

    private async Task<MemoryJobCompletion> CompleteMemoryJobAsync(
        Guid sessionId,
        GameRuntimeState runtime,
        RulesGameState liveState,
        IGameModule module,
        MemorySummaryJob job,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        var context = BuildPromptContext(runtime, liveState, module, job.Binding, job.Memory, job.RoundEnded);
        var prompt = BuildMemorySummaryPrompt(context);
        var model = Normalize(job.Binding.ModelOverride) ?? DefaultModelName;
        var providerAlias = Normalize(job.Binding.ProviderAlias);
        var envelopeId = $"game-memory-{Guid.CreateVersion7():N}";
        var decisionId = $"memory-decision-{Guid.CreateVersion7():N}";
        if (providerAlias is null)
        {
            var empty = new CompletionResponse
            {
                Content = new MessageContent(""),
                StopReason = StopReason.Error,
                Usage = new TokenUsage(0, 0),
            };
            return new MemoryJobCompletion(job, context, prompt, envelopeId, decisionId, providerAlias, model, empty,
                GameAgentMemorySummaryParseResult.Rejected("provider-level-failure", "Agent participant has no configured provider alias."));
        }

        try
        {
            using var trackingScope = TokenTrackingScope.Begin(sessionId, $"game-memory:{job.Binding.ParticipantId}");
            var response = await _completionService.CompleteAsync(
                new CompletionRequest
                {
                    ProviderAlias = providerAlias,
                    Model = model,
                    MaxTokens = Math.Max(1, context.TokenBudget),
                    Temperature = _appConfig.Agents.GameAgentMemory.Temperature,
                    Tools = [],
                    CacheSystemPrompt = true,
                    SystemPrompt = prompt.SystemPrompt,
                    Messages = [new CompletionMessage("user", new MessageContent(prompt.UserPrompt))],
                },
                ct);
            return new MemoryJobCompletion(job, context, prompt, envelopeId, decisionId, providerAlias, model, response,
                ParseMemorySummaryResponse(response));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(
                ex,
                "Game agent memory summary provider call failed: session={SessionId} participant={ParticipantId} provider={ProviderAlias}",
                sessionId,
                job.Binding.ParticipantId,
                providerAlias);
            var empty = new CompletionResponse
            {
                Content = new MessageContent(""),
                StopReason = StopReason.Error,
                Usage = new TokenUsage(0, 0),
            };
            return new MemoryJobCompletion(job, context, prompt, envelopeId, decisionId, providerAlias, model, empty,
                GameAgentMemorySummaryParseResult.Rejected("provider-level-failure", ex.Message));
        }
    }

    private async Task<MemoryRecordResult> RecordMemoryCompletionAsync(
        Guid sessionId,
        MemoryJobCompletion completion,
        DateTimeOffset occurredAt,
        CancellationToken ct)
    {
        if (!completion.ParseResult.IsAccepted || string.IsNullOrWhiteSpace(completion.ParseResult.Summary))
        {
            var rejectionDecision = new MemorySummaryDecision(
                completion.DecisionId,
                completion.Job.Binding.ParticipantId,
                completion.Job.RoundEnded.RoundNumber,
                occurredAt,
                completion.Prompt.PriorCursor,
                completion.Prompt.NewCursor,
                completion.Response.Usage.InputTokens,
                completion.Response.Usage.OutputTokens,
                false,
                false,
                false,
                completion.ProviderAlias,
                completion.Model,
                completion.Job.RoundEnded.EventId.ToString(),
                completion.ParseResult.ReasonCode,
                null);
            var rejectionRecord = await _runtimeService.RecordAgentMemorySummaryAsync(
                sessionId,
                new RecordGameRuntimeAgentMemorySummaryCommand(
                    completion.EnvelopeId,
                    completion.Job.Binding.ParticipantId,
                    occurredAt,
                    null,
                    null,
                    rejectionDecision,
                    completion.ProviderAlias,
                    completion.Model,
                    completion.Response.Usage.InputTokens,
                    completion.Response.Usage.OutputTokens,
                    completion.Prompt.PromptContentHash,
                    StableContentHash(completion.Response.Content.GetText()),
                    completion.Prompt.SystemPrompt + "\n---\n" + completion.Prompt.UserPrompt,
                    completion.Response.Content.GetText(),
                    Math.Max(1, _appConfig.Agents.GameAgentMemory.MaxPromptEnvelopesPerAgent)),
                ct);
            return new MemoryRecordResult(
                new GameAgentMemorySummaryParticipantResult(
                    completion.Job.Binding.ParticipantId,
                    completion.Job.RoundEnded.RoundNumber,
                    GameAgentMemorySummaryOutcome.Rejected,
                    completion.ParseResult.ReasonCode,
                    completion.ParseResult.Message,
                    completion.ProviderAlias,
                    completion.Model,
                    completion.Response.Usage),
                rejectionRecord.Status == SessionMutationStatus.Success && rejectionRecord.Value is not null
                    ? rejectionRecord.Value.RuntimeEvents
                    : []);
        }

        var rawSummary = completion.ParseResult.Summary.Trim();
        var budget = Math.Max(1, completion.Context.TokenBudget);
        var (summary, exceeded, trimmed) = EnforceSummaryBudget(
            rawSummary,
            budget,
            completion.Response.Usage.OutputTokens,
            ResolveFallbackCharactersPerToken(completion.ProviderAlias));
        var summaryHash = StableContentHash(summary);
        var decision = new MemorySummaryDecision(
            completion.DecisionId,
            completion.Job.Binding.ParticipantId,
            completion.Job.RoundEnded.RoundNumber,
            occurredAt,
            completion.Prompt.PriorCursor,
            completion.Prompt.NewCursor,
            completion.Response.Usage.InputTokens,
            completion.Response.Usage.OutputTokens,
            exceeded,
            trimmed,
            false,
            completion.ProviderAlias,
            completion.Model,
            completion.Job.RoundEnded.EventId.ToString(),
            null,
            summaryHash);
        var result = await _runtimeService.RecordAgentMemorySummaryAsync(
            sessionId,
            new RecordGameRuntimeAgentMemorySummaryCommand(
                completion.EnvelopeId,
                completion.Job.Binding.ParticipantId,
                occurredAt,
                summary,
                summaryHash,
                decision,
                completion.ProviderAlias,
                completion.Model,
                completion.Response.Usage.InputTokens,
                completion.Response.Usage.OutputTokens,
                completion.Prompt.PromptContentHash,
                StableContentHash(completion.Response.Content.GetText()),
                completion.Prompt.SystemPrompt + "\n---\n" + completion.Prompt.UserPrompt,
                completion.Response.Content.GetText(),
                Math.Max(1, _appConfig.Agents.GameAgentMemory.MaxPromptEnvelopesPerAgent)),
            ct);
        if (result.Status != SessionMutationStatus.Success || result.Value is null)
        {
            return new MemoryRecordResult(
                new GameAgentMemorySummaryParticipantResult(
                    completion.Job.Binding.ParticipantId,
                    completion.Job.RoundEnded.RoundNumber,
                    GameAgentMemorySummaryOutcome.Rejected,
                    "cursor-inconsistency",
                    result.Error ?? "Memory summary state update failed.",
                    completion.ProviderAlias,
                    completion.Model,
                    completion.Response.Usage),
                []);
        }

        return new MemoryRecordResult(
            new GameAgentMemorySummaryParticipantResult(
                completion.Job.Binding.ParticipantId,
                completion.Job.RoundEnded.RoundNumber,
                GameAgentMemorySummaryOutcome.Recorded,
                trimmed ? "summary-trimmed" : "recorded",
                trimmed ? "Memory summary exceeded the configured token budget and was trimmed." : "Memory summary recorded.",
                completion.ProviderAlias,
                completion.Model,
                completion.Response.Usage),
            result.Value.RuntimeEvents);
    }

    private GameAgentMemorySummaryPromptContext BuildPromptContext(
        GameRuntimeState runtime,
        RulesGameState liveState,
        IGameModule module,
        GameRuntimeParticipantBinding binding,
        GameRuntimeAgentMemoryState? memory,
        RoundEndedEvent roundEnded)
    {
        var visibleEvents = _visibleEventsService.BuildForMemorySummary(runtime, liveState, memory, binding.ParticipantId);
        return new GameAgentMemorySummaryPromptContext(
            runtime.GameInstanceId!,
            binding.ParticipantId,
            binding.DisplayName,
            roundEnded.RoundNumber,
            module.Descriptor.DisplayName,
            module.GetPromptAssets(),
            memory?.Summary,
            Math.Max(1, memory?.TokenBudget ?? module.Descriptor.MemoryExpectations.SuggestedSummaryTokenBudget),
            visibleEvents,
            binding);
    }

    private static IReadOnlyList<MemorySummaryJob> FindMemoryJobs(
        GameRuntimeState runtime,
        RulesGameState liveState,
        IGameModule module)
    {
        if (!module.Descriptor.MemoryExpectations.UsesRoundSummaries)
        {
            return [];
        }

        var roundEndedEvents = liveState.EventJournal.Events
            .OfType<RoundEndedEvent>()
            .OrderBy(item => item.Sequence)
            .ToArray();
        if (roundEndedEvents.Length == 0)
        {
            return [];
        }

        var agents = runtime.ParticipantBindings
            .Where(binding => binding.Kind == GameRuntimeParticipantKind.Agent)
            .OrderBy(binding => binding.ParticipantId, StringComparer.Ordinal)
            .ToArray();
        var jobs = new List<MemorySummaryJob>();
        foreach (var roundEnded in roundEndedEvents)
        {
            foreach (var binding in agents)
            {
                if (runtime.MemorySummaryDecisions.Any(item =>
                        string.Equals(item.ParticipantId, binding.ParticipantId, StringComparison.Ordinal)
                        && item.RoundNumber == roundEnded.RoundNumber))
                {
                    continue;
                }

                var memory = runtime.AgentMemories.FirstOrDefault(item =>
                    string.Equals(item.ParticipantId, binding.ParticipantId, StringComparison.Ordinal));
                jobs.Add(new MemorySummaryJob(binding, memory, roundEnded));
            }
        }

        return jobs;
    }

    private (string Summary, bool Exceeded, bool Trimmed) EnforceSummaryBudget(
        string summary,
        int tokenBudget,
        int responseTokens,
        double fallbackCharactersPerToken)
    {
        var measuredTokens = responseTokens > 0
            ? responseTokens
            : EstimateSummaryTokens(summary, fallbackCharactersPerToken);
        var exceeded = measuredTokens > tokenBudget;
        if (!exceeded)
        {
            return (summary, false, false);
        }

        var trimmed = TrimSummaryToTokenBudget(summary, tokenBudget, measuredTokens);
        return (trimmed, true, !string.Equals(trimmed, summary, StringComparison.Ordinal));
    }

    private double ResolveFallbackCharactersPerToken(string? providerAlias)
    {
        if (!string.IsNullOrWhiteSpace(providerAlias)
            && TryGetConfiguredCharactersPerToken(
                _appConfig.Agents.GameAgentMemory.FallbackCharactersPerTokenByProvider,
                providerAlias,
                out var providerCharactersPerToken))
        {
            return providerCharactersPerToken;
        }

        return GameAgentMemoryBudget.NormalizeFallbackCharactersPerToken(
            _appConfig.Agents.GameAgentMemory.FallbackCharactersPerToken);
    }

    private static bool TryGetConfiguredCharactersPerToken(
        IReadOnlyDictionary<string, double> values,
        string key,
        out double charactersPerToken)
    {
        if (values.TryGetValue(key, out var configuredCharactersPerToken))
        {
            charactersPerToken = GameAgentMemoryBudget.NormalizeFallbackCharactersPerToken(configuredCharactersPerToken);
            return true;
        }

        charactersPerToken = 0;
        return false;
    }

    private static int EstimateSummaryTokens(string summary, double charactersPerToken)
    {
        var normalizedLength = summary.ReplaceLineEndings("\n").Length;
        return Math.Max(1, (int)Math.Ceiling(normalizedLength / charactersPerToken));
    }

    private static string TrimSummaryToTokenBudget(
        string summary,
        int tokenBudget,
        int measuredTokens)
    {
        if (string.IsNullOrWhiteSpace(summary) || measuredTokens <= tokenBudget)
        {
            return summary;
        }

        var targetLength = Math.Max(1, (int)Math.Floor(summary.Length * (tokenBudget / (double)measuredTokens)));
        if (targetLength >= summary.Length)
        {
            return summary;
        }

        var candidate = summary[..targetLength].TrimEnd();
        if (targetLength < summary.Length && char.IsWhiteSpace(summary[targetLength]))
        {
            return candidate;
        }

        var boundary = candidate.LastIndexOfAny([' ', '\t', '\r', '\n']);
        if (boundary > 0)
        {
            candidate = candidate[..boundary].TrimEnd();
        }

        return string.IsNullOrWhiteSpace(candidate) ? summary[..targetLength].TrimEnd() : candidate;
    }

    private static void AppendVisibleEvents(StringBuilder builder, IReadOnlyList<VisibleGameEvent> events)
    {
        if (events.Count == 0)
        {
            builder.AppendLine("- No newly visible engine facts.");
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
            builder.AppendLine("- No newly visible channel or direct-message entries.");
            return;
        }

        foreach (var item in feed.OrderBy(item => item.Sequence))
        {
            var author = item.Author?.ParticipantId.Value ?? "system";
            var text = item.Text ?? item.Summary ?? item.GameEventId ?? item.Kind.ToString();
            builder.AppendLine($"- #{item.Sequence} {item.Kind} from {author}: {text}");
        }
    }

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

    private sealed record MemorySummaryDto(string? Summary);

    private sealed record MemorySummaryJob(
        GameRuntimeParticipantBinding Binding,
        GameRuntimeAgentMemoryState? Memory,
        RoundEndedEvent RoundEnded);

    private sealed record MemoryJobCompletion(
        MemorySummaryJob Job,
        GameAgentMemorySummaryPromptContext Context,
        GameAgentMemorySummaryPromptAssembly Prompt,
        string EnvelopeId,
        string DecisionId,
        string? ProviderAlias,
        string? Model,
        CompletionResponse Response,
        GameAgentMemorySummaryParseResult ParseResult);

    private sealed record MemoryRecordResult(
        GameAgentMemorySummaryParticipantResult ParticipantResult,
        IReadOnlyList<IGameRuntimeEvent> RuntimeEvents);
}
