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
            IGameDiagnosticLogService diagnosticLog,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            logger.LogInformation(
                "Game diagnostic log requested: session={SessionId} promptPreviewCharacters={PromptPreviewCharacters}",
                sessionId,
                promptPreviewCharacters);
            var projection = await diagnosticLog.GetLogAsync(
                sessionId,
                promptPreviewCharacters ?? 1200,
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
            var result = await bridge.PostPublicMessageAsync(
                sessionId,
                new PostGameRuntimePublicMessageCommand(
                    Guid.CreateVersion7(),
                    request.ParticipantId ?? string.Empty,
                    request.AuthorKind,
                    request.Text ?? string.Empty,
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
            var result = await bridge.SendDirectMessageAsync(
                sessionId,
                new SendGameRuntimeDirectMessageCommand(
                    Guid.CreateVersion7(),
                    request.ParticipantId ?? string.Empty,
                    request.AuthorKind,
                    request.RecipientParticipantIds,
                    request.Text ?? string.Empty,
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
        string? reasonCode = null) =>
        new()
        {
            Error = error,
            Message = string.IsNullOrWhiteSpace(message) ? "Game operation failed." : message,
            ReasonCode = string.IsNullOrWhiteSpace(reasonCode) ? null : reasonCode.Trim(),
            Operation = string.IsNullOrWhiteSpace(operation) ? null : operation,
            DiagnosticHint = "Open the game diagnostic log for persisted runtime, rejection, communication, and provider details.",
        };

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
