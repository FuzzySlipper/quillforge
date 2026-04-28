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
            return ToMutationResult(result);
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
                return ToMutationResult(textResult);
            }

            if (string.IsNullOrWhiteSpace(request.PendingInputId) || string.IsNullOrWhiteSpace(request.ChoiceName))
            {
                return Results.BadRequest(new { Error = "Either text or pendingInputId plus choiceName is required." });
            }

            var typedResult = await bridge.SubmitTypedActionAsync(
                sessionId,
                new SubmitGameTypedActionCommand(
                    request.ParticipantId,
                    request.PendingInputId,
                    request.ChoiceName,
                    DateTimeOffset.UtcNow),
                ct);
            return ToMutationResult(typedResult);
        });

        group.MapPost("/messages", async (
            Guid sessionId,
            PostGamePublicMessageRequest request,
            IGameBridgeService bridge,
            CancellationToken ct) =>
        {
            var result = await bridge.PostPublicMessageAsync(
                sessionId,
                new PostGameRuntimePublicMessageCommand(
                    Guid.CreateVersion7(),
                    request.ParticipantId,
                    request.AuthorKind,
                    request.Text,
                    DateTimeOffset.UtcNow),
                ct);
            return ToMutationResult(result);
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
                    request.ParticipantId,
                    request.AuthorKind,
                    request.RecipientParticipantIds,
                    request.Text,
                    DateTimeOffset.UtcNow),
                ct);
            return ToMutationResult(result);
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
            return ToMutationResult(result);
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
            return ToMutationResult(result);
        });
    }

    private static IResult ToMutationResult(SessionMutationResult<GameBridgeMutationResult> result)
    {
        if (result.Status == SessionMutationStatus.Busy)
        {
            return Results.Conflict(new { Error = result.Error });
        }

        if (result.Status == SessionMutationStatus.Invalid || result.Value is null)
        {
            return Results.BadRequest(new { Error = result.Error ?? "Game mutation failed." });
        }

        return Results.Ok(new GameMutationResponse
        {
            View = result.Value.View,
            RuntimeEventTypes = result.Value.RuntimeEvents.Select(item => item.EventName).ToArray(),
            EngineEventTypes = result.Value.EngineEvents.Select(item => item.GetType().Name).ToArray(),
            CommunicationEventTypes = result.Value.CommunicationEvents.Select(item => item.GetType().Name).ToArray(),
        });
    }
}
