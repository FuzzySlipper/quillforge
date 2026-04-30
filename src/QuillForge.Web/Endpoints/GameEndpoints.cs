using Den.RulesEngine;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Contracts;

namespace QuillForge.Web.Endpoints;

public static class GameEndpoints
{
    public static void MapGameEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sessions/{sessionId:guid}/game");

        group.MapGet("/", async (
            Guid sessionId,
            string? participantId,
            IGameBridgeService bridge,
            CancellationToken ct) =>
        {
            var view = await bridge.GetViewAsync(sessionId, participantId, ct);
            return Results.Ok(new GameViewResponse { View = view });
        });

        group.MapGet("/inspector", async (
            Guid sessionId,
            int? promptEnvelopeLimit,
            IGameInspectorService inspector,
            CancellationToken ct) =>
        {
            var projection = await inspector.GetProjectionAsync(
                sessionId,
                promptEnvelopeLimit ?? 10,
                ct);
            return Results.Ok(new GameInspectorResponse { Inspector = projection });
        });

        group.MapGet("/diagnostics", async (
            Guid sessionId,
            int? promptPreviewCharacters,
            string? gameInstanceId,
            int? limit,
            long? beforeSequence,
            string? category,
            string? categories,
            IGameDiagnosticLogService diagnosticLog,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var categoryResult = TryParseDiagnosticCategories(category, categories);
            if (!categoryResult.IsValid)
            {
                return Results.BadRequest(new GameMutationErrorResponse
                {
                    Error = "game_diagnostic_query_invalid",
                    Message = categoryResult.ErrorMessage ?? "Diagnostic category filter is invalid.",
                    ReasonCode = "invalid_diagnostic_category",
                    Operation = "get_game_diagnostics",
                    DiagnosticHint = "Use category names such as Rejection, Error, Communication, LlmProvider, AgentPrompt, or comma-separated category lists.",
                });
            }

            logger.LogInformation(
                "Game diagnostic log requested: session={SessionId} game={GameInstanceId} promptPreviewCharacters={PromptPreviewCharacters} limit={Limit} beforeSequence={BeforeSequence} categories={Categories}",
                sessionId,
                gameInstanceId,
                promptPreviewCharacters,
                limit,
                beforeSequence,
                string.Join(",", categoryResult.Categories));
            var projection = await diagnosticLog.GetLogAsync(
                sessionId,
                new GameDiagnosticLogQuery
                {
                    PromptPreviewCharacters = promptPreviewCharacters ?? GameDiagnosticLogQuery.DefaultPromptPreviewCharacters,
                    RequestedGameInstanceId = gameInstanceId,
                    Limit = limit,
                    BeforeSequence = beforeSequence,
                    Categories = categoryResult.Categories,
                },
                ct);
            return Results.Ok(new GameDiagnosticLogResponse { Log = projection });
        });

        group.MapPost("/start", async (
            Guid sessionId,
            StartGameRequest request,
            IGameBridgeService bridge,
            CancellationToken ct) =>
        {
            var result = await bridge.StartFromTemplateAsync(
                sessionId,
                new StartGameFromTemplateCommand(
                    request.TemplateId,
                    request.UserDisplayName,
                    request.Seed,
                    DateTimeOffset.UtcNow),
                ct);
            return ToMutationResult(result, "start_game_from_template");
        });

        group.MapPost("/actions", async (
            Guid sessionId,
            SubmitGameActionRequest request,
            IGameBridgeService bridge,
            CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(request.Text))
            {
                var textResult = await bridge.SubmitTextActionAsync(
                    sessionId,
                    new SubmitGameTextActionCommand(
                        request.ParticipantId,
                        request.Text,
                        DateTimeOffset.UtcNow),
                    ct);
                return ToMutationResult(textResult, "submit_game_action");
            }

            if (string.IsNullOrWhiteSpace(request.PendingInputId) || string.IsNullOrWhiteSpace(request.ChoiceName))
            {
                return Results.BadRequest(CreateErrorResponse(
                    "game_action_invalid",
                    "Either text or pendingInputId plus choiceName is required.",
                    operation: "submit_game_action"));
            }

            var typedResult = await bridge.SubmitTypedActionAsync(
                sessionId,
                new SubmitGameTypedActionCommand(
                    request.ParticipantId,
                    request.PendingInputId,
                    request.ChoiceName,
                    DateTimeOffset.UtcNow),
                ct);
            return ToMutationResult(typedResult, "submit_game_action");
        });

        group.MapPost("/messages", async (
            Guid sessionId,
            PostGamePublicMessageRequest request,
            IGameBridgeService bridge,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            logger.LogInformation(
                "Game public message endpoint invoked: session={SessionId} participant={ParticipantId} authorKind={AuthorKind} textLength={TextLength}",
                sessionId,
                request.ParticipantId,
                request.AuthorKind,
                request.Text?.Length ?? 0);
            if (string.IsNullOrWhiteSpace(request.ParticipantId))
            {
                return Results.BadRequest(CreateErrorResponse(
                    "game_request_invalid",
                    "participantId is required.",
                    operation: "post_game_public_message",
                    reasonCode: "missing_participant",
                    diagnosticHint: PreRuntimeDiagnosticHint));
            }

            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return Results.BadRequest(CreateErrorResponse(
                    "game_request_invalid",
                    "text is required.",
                    operation: "post_game_public_message",
                    reasonCode: "empty_message",
                    diagnosticHint: PreRuntimeDiagnosticHint));
            }

            var result = await bridge.PostPublicMessageAsync(
                sessionId,
                new PostGameRuntimePublicMessageCommand(
                    Guid.CreateVersion7(),
                    request.ParticipantId.Trim(),
                    request.AuthorKind,
                    request.Text.Trim(),
                    DateTimeOffset.UtcNow),
                ct);
            return ToMutationResult(result, "post_game_public_message");
        });

        group.MapPost("/direct-messages", async (
            Guid sessionId,
            SendGameDirectMessageRequest request,
            IGameBridgeService bridge,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ParticipantId))
            {
                return Results.BadRequest(CreateErrorResponse(
                    "game_request_invalid",
                    "participantId is required.",
                    operation: "send_game_direct_message",
                    reasonCode: "missing_participant",
                    diagnosticHint: PreRuntimeDiagnosticHint));
            }

            if (string.IsNullOrWhiteSpace(request.Text))
            {
                return Results.BadRequest(CreateErrorResponse(
                    "game_request_invalid",
                    "text is required.",
                    operation: "send_game_direct_message",
                    reasonCode: "empty_message",
                    diagnosticHint: PreRuntimeDiagnosticHint));
            }

            var result = await bridge.SendDirectMessageAsync(
                sessionId,
                new SendGameRuntimeDirectMessageCommand(
                    Guid.CreateVersion7(),
                    request.ParticipantId.Trim(),
                    request.AuthorKind,
                    request.RecipientParticipantIds,
                    request.Text.Trim(),
                    DateTimeOffset.UtcNow),
                ct);
            return ToMutationResult(result, "send_game_direct_message");
        });

        group.MapPost("/end", async (
            Guid sessionId,
            EndGameRequest request,
            IGameBridgeService bridge,
            CancellationToken ct) =>
        {
            var result = await bridge.EndAsync(
                sessionId,
                new EndGameBridgeCommand(
                    GameIntentCommandId.NewId(),
                    request.OutcomeName,
                    DateTimeOffset.UtcNow),
                ct);
            return ToMutationResult(result, "end_game");
        });

        group.MapPost("/abort", async (
            Guid sessionId,
            AbortGameRequest request,
            IGameBridgeService bridge,
            CancellationToken ct) =>
        {
            var result = await bridge.AbortAsync(
                sessionId,
                new AbortGameRuntimeCommand(
                    GameIntentCommandId.NewId(),
                    request.ReasonCode,
                    DateTimeOffset.UtcNow),
                ct);
            return ToMutationResult(result, "abort_game");
        });
    }

    private const string PreRuntimeDiagnosticHint = "This request was rejected before a game runtime mutation was attempted, so it may not appear in the backend diagnostic log.";

    private static DiagnosticCategoryParseResult TryParseDiagnosticCategories(string? category, string? categories)
    {
        var values = new[] { category, categories }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (values.Length == 0)
        {
            return new DiagnosticCategoryParseResult(true, [], null);
        }

        var parsed = new List<GameDiagnosticLogCategory>();
        foreach (var value in values)
        {
            if (!Enum.TryParse<GameDiagnosticLogCategory>(value, ignoreCase: true, out var categoryValue))
            {
                return new DiagnosticCategoryParseResult(false, [], $"Unknown diagnostic category '{value}'.");
            }

            parsed.Add(categoryValue);
        }

        return new DiagnosticCategoryParseResult(true, parsed.Distinct().OrderBy(item => item.ToString(), StringComparer.Ordinal).ToArray(), null);
    }

    private static IResult ToMutationResult(
        SessionMutationResult<GameBridgeMutationResult> result,
        string operation)
    {
        if (result.Status == SessionMutationStatus.Busy)
        {
            return Results.Conflict(CreateErrorResponse(
                "session_busy",
                result.Error ?? "Another mutating operation is already running for this session.",
                operation));
        }

        if (result.Status == SessionMutationStatus.Invalid || result.Value is null)
        {
            var (reasonCode, message) = SplitReasonCode(result.Error ?? "Game mutation failed.");
            return Results.BadRequest(CreateErrorResponse(
                "game_mutation_invalid",
                message,
                operation,
                reasonCode));
        }

        return Results.Ok(new GameMutationResponse
        {
            View = result.Value.View,
            RuntimeEventTypes = result.Value.RuntimeEvents.Select(item => item.EventName).ToArray(),
            EngineEventTypes = result.Value.EngineEvents.Select(item => item.GetType().Name).ToArray(),
            CommunicationEventTypes = result.Value.CommunicationEvents.Select(item => item.GetType().Name).ToArray(),
        });
    }

    private static GameMutationErrorResponse CreateErrorResponse(
        string error,
        string message,
        string? operation,
        string? reasonCode = null,
        string? diagnosticHint = null) =>
        new()
        {
            Error = error,
            Message = string.IsNullOrWhiteSpace(message) ? "Game operation failed." : message,
            ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? null : reasonCode.Trim(),
            Operation = string.IsNullOrWhiteSpace(operation) ? null : operation,
            DiagnosticHint = string.IsNullOrWhiteSpace(diagnosticHint)
                ? "Open the game diagnostic log for persisted runtime, rejection, communication, and provider details."
                : diagnosticHint.Trim(),
        };

    private sealed record DiagnosticCategoryParseResult(
        bool IsValid,
        IReadOnlyList<GameDiagnosticLogCategory> Categories,
        string? ErrorMessage);

    private static (string? ReasonCode, string Message) SplitReasonCode(string error)
    {
        var trimmed = string.IsNullOrWhiteSpace(error) ? "Game mutation failed." : error.Trim();
        var separator = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
        {
            return (null, trimmed);
        }

        var prefix = trimmed[..separator].Trim();
        if (prefix.Length == 0 || prefix.Any(character => !char.IsLetterOrDigit(character) && character != '_' && character != '-'))
        {
            return (null, trimmed);
        }

        var message = trimmed[(separator + 1)..].Trim();
        return (prefix, string.IsNullOrWhiteSpace(message) ? trimmed : message);
    }
}
