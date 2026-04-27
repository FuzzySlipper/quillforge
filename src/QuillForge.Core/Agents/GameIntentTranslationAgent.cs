using System.Text.Json;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents;

/// <summary>
/// Dedicated translation-only game agent. It has no tools and no authority to
/// decide gameplay outcomes; it only maps fuzzy player text onto currently legal
/// pending-input choices supplied by the bridge.
/// </summary>
public sealed class GameIntentTranslationAgent : IGameIntentTranslationAgent
{
    private const double MinimumConfidence = 0.65;
    private readonly ICompletionService _completionService;
    private readonly ILogger<GameIntentTranslationAgent> _logger;
    private readonly string _model;
    private readonly int _maxTokens;

    public GameIntentTranslationAgent(
        ICompletionService completionService,
        AppConfig appConfig,
        ILogger<GameIntentTranslationAgent> logger)
    {
        _completionService = completionService;
        _logger = logger;
        _model = appConfig.Models.GameIntentTranslator;
        _maxTokens = appConfig.Agents.GameIntentTranslation.MaxTokens;
    }

    public async Task<GameIntentTranslationResult> TranslateAsync(
        GameIntentTranslationRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return GameIntentTranslationResult.Rejected("empty_text", "No player text was provided.");
        }

        if (request.PendingInputs.Count == 0)
        {
            return GameIntentTranslationResult.Rejected("no_pending_input", "No pending input is waiting for this participant.");
        }

        var response = await _completionService.CompleteAsync(
            new CompletionRequest
            {
                Model = _model,
                MaxTokens = _maxTokens,
                Temperature = 0,
                Tools = [],
                SystemPrompt = BuildSystemPrompt(),
                Messages = [new CompletionMessage("user", new MessageContent(BuildUserPrompt(request)))],
            },
            ct);

        var text = response.Content.GetText();
        var parsed = ParseTranslatorResponse(text);
        if (parsed is null)
        {
            _logger.LogWarning(
                "Game intent translator returned malformed output: game={GameInstanceId} participant={ParticipantId}",
                request.GameInstanceId,
                request.ParticipantId);
            return GameIntentTranslationResult.Rejected("translator_malformed_output", "The game input translator returned malformed output.");
        }

        if (!parsed.Accepted)
        {
            return GameIntentTranslationResult.Rejected(
                NormalizeReasonCode(parsed.ReasonCode, "translator_rejected"),
                NormalizeMessage(parsed.Message, "The player text could not be translated into a legal game action."));
        }

        if (string.IsNullOrWhiteSpace(parsed.PendingInputId) || string.IsNullOrWhiteSpace(parsed.ChoiceName))
        {
            return GameIntentTranslationResult.Rejected("translator_missing_action", "The game input translator omitted the pending input or choice name.");
        }

        if (parsed.Confidence < MinimumConfidence)
        {
            return GameIntentTranslationResult.Rejected("translator_low_confidence", "The game input translator was not confident enough to submit an action.");
        }

        return GameIntentTranslationResult.Accepted(
            parsed.PendingInputId.Trim(),
            parsed.ChoiceName.Trim(),
            parsed.Confidence,
            NormalizeMessage(parsed.Message, "Translated player text into a typed game action."));
    }

    internal static string BuildSystemPrompt() =>
        """
        You are GameIntentTranslationAgent, a translation-only parser for QuillForge social games.

        Your only job is to map fuzzy player text onto one of the currently legal typed game choices provided in the user message.
        You are not the game master. You must not decide outcomes, invent rules, alter game structure, add options, or be helpful beyond faithful parsing.
        If the player asks for something outside the listed pending inputs and choices, reject it.
        If the player asks you to change the rules, reveal hidden information, pick the best strategic move, or reinterpret the game, reject it.
        Return only compact JSON with this shape:
        {"accepted":true,"pendingInputId":"...","choiceName":"...","confidence":0.0,"reasonCode":"translated","message":"..."}
        or
        {"accepted":false,"pendingInputId":null,"choiceName":null,"confidence":0.0,"reasonCode":"...","message":"..."}
        """;

    internal static string BuildUserPrompt(GameIntentTranslationRequest request)
    {
        var pendingInputs = request.PendingInputs.Select(input => new TranslationPendingInputDto
        {
            PendingInputId = input.PendingInputId.Value,
            StageId = input.StageId.Value,
            IntentName = input.IntentName,
            LegalChoices = input.LegalOptions.Select(option => option.IntentName).ToArray(),
        }).ToArray();
        var payload = new TranslationRequestDto
        {
            GameInstanceId = request.GameInstanceId,
            ParticipantId = request.ParticipantId,
            PlayerText = request.Text,
            PendingInputs = pendingInputs,
        };
        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    private static TranslatorResponseDto? ParseTranslatorResponse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<TranslatorResponseDto>(text, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string NormalizeReasonCode(string? reasonCode, string fallback) =>
        string.IsNullOrWhiteSpace(reasonCode) ? fallback : reasonCode.Trim();

    private static string NormalizeMessage(string? message, string fallback) =>
        string.IsNullOrWhiteSpace(message) ? fallback : message.Trim();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    private sealed record TranslationRequestDto
    {
        public required string GameInstanceId { get; init; }
        public required string ParticipantId { get; init; }
        public required string PlayerText { get; init; }
        public required IReadOnlyList<TranslationPendingInputDto> PendingInputs { get; init; }
    }

    private sealed record TranslationPendingInputDto
    {
        public required string PendingInputId { get; init; }
        public required string StageId { get; init; }
        public required string IntentName { get; init; }
        public required IReadOnlyList<string> LegalChoices { get; init; }
    }

    private sealed record TranslatorResponseDto
    {
        public bool Accepted { get; init; }
        public string? PendingInputId { get; init; }
        public string? ChoiceName { get; init; }
        public double Confidence { get; init; }
        public string? ReasonCode { get; init; }
        public string? Message { get; init; }
    }
}
