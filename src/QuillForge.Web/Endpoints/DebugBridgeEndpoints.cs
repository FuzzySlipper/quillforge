using System.Text.Json;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Contracts;
using QuillForge.Web.Services;

namespace QuillForge.Web.Endpoints;

/// <summary>
/// Debug bridge endpoints for integration testing against a live build.
/// Only registered in Development environment.
/// </summary>
public static class DebugBridgeEndpoints
{
    public static void MapDebugBridgeEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/debug/bridge");

        group.MapPost("/chat", async (
            HttpContext httpContext,
            OrchestratorAgent orchestrator,
            ISessionStateService runtimeService,
            ISessionBootstrapService bootstrapService,
            ISessionProfileReadService profileReadService,
            ISessionStore sessionStore,
            IEnumerable<IToolHandler> toolHandlers,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var sessionId = root.GetOptionalGuid("sessionId") ?? Guid.CreateVersion7();
            var message = root.GetProperty("message").GetString() ?? "";
            var model = root.GetStringOrDefault("model", "default");
            var requestedConductor = root.GetOptionalString("conductor");
            var maxTokens = root.GetIntOrDefault("maxTokens", 4096);

            ConversationTree tree;
            try
            {
                tree = await sessionStore.LoadAsync(sessionId, ct);
            }
            catch (FileNotFoundException)
            {
                tree = await bootstrapService.CreateAsync(
                    new CreateSessionCommand
                    {
                        SessionId = sessionId,
                        Name = "Debug Session",
                    },
                    ct);
            }

            tree.Append(tree.ActiveLeafId, "user", new MessageContent(message));

            var thread = tree.ToFlatThread();
            var messages = thread
                .Select(n => new CompletionMessage(n.Role, n.Content))
                .ToList();
            var lastAssistantResponse = thread
                .LastOrDefault(n => string.Equals(n.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                ?.Content
                .GetText();

            var prepared = await profileReadService.PrepareInteractiveRequestAsync(
                sessionId,
                new PrepareInteractiveRequestOptions
                {
                    RequestedConductor = requestedConductor,
                    LastAssistantResponse = lastAssistantResponse,
                },
                ct);
            var sessionState = prepared.ProfileView.SessionState;
            var context = prepared.AgentContext;
            var conductor = prepared.Conductor;

            var tools = toolHandlers.ToList();
            var response = await orchestrator.HandleAsync(
                sessionState, conductor, model, maxTokens, tools, messages, context, ct: ct);

            tree.Append(tree.ActiveLeafId, "assistant", response.Content, new MessageMetadata
            {
                Model = model,
                InputTokens = response.Usage.InputTokens,
                OutputTokens = response.Usage.OutputTokens,
                StopReason = response.StopReason,
            });

            await sessionStore.SaveAsync(tree, ct);
            var pendingCapture = await runtimeService.CaptureWriterPendingAsync(
                sessionId,
                new CaptureWriterPendingCommand(response.Content.GetText(), sessionState.Mode.ActiveMode),
                ct);
            if (pendingCapture.Status == SessionMutationStatus.Busy)
            {
                app.Logger.LogWarning(
                    "Writer pending capture skipped because the session was busy: session={SessionId}",
                    sessionId);
            }

            return Results.Ok(new DebugBridgeChatResponse
            {
                SessionId = sessionId,
                ResponseText = response.Content.GetText(),
                StopReason = response.StopReason.ToWireString(),
                ToolRoundsUsed = response.ToolRoundsUsed,
                Usage = new DebugBridgeUsageDto
                {
                    InputTokens = response.Usage.InputTokens,
                    OutputTokens = response.Usage.OutputTokens,
                },
                Mode = sessionState.Mode.ActiveMode.ToWireString(),
                MessageCount = tree.ToFlatThread().Count,
            });
        });

        group.MapPost("/chat/stream", async (
            HttpContext httpContext,
            OrchestratorAgent orchestrator,
            ISessionStateService runtimeService,
            ISessionBootstrapService bootstrapService,
            ISessionProfileReadService profileReadService,
            ISessionStore sessionStore,
            IEnumerable<IToolHandler> toolHandlers,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var sessionId = root.GetOptionalGuid("sessionId") ?? Guid.CreateVersion7();
            var message = root.GetProperty("message").GetString() ?? "";
            var model = root.GetStringOrDefault("model", "default");
            var requestedConductor = root.GetOptionalString("conductor");
            var maxTokens = root.GetIntOrDefault("maxTokens", 4096);

            ConversationTree tree;
            try
            {
                tree = await sessionStore.LoadAsync(sessionId, ct);
            }
            catch (FileNotFoundException)
            {
                tree = await bootstrapService.CreateAsync(
                    new CreateSessionCommand { SessionId = sessionId, Name = "Debug Session" }, ct);
            }

            tree.Append(tree.ActiveLeafId, "user", new MessageContent(message));
            var appendParentId = tree.ActiveLeafId;

            var thread = tree.ToFlatThread();
            var messages = thread.Select(n => new CompletionMessage(n.Role, n.Content)).ToList();
            var lastAssistantResponse = thread
                .LastOrDefault(n => string.Equals(n.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                ?.Content.GetText();

            var prepared = await profileReadService.PrepareInteractiveRequestAsync(
                sessionId,
                new PrepareInteractiveRequestOptions
                {
                    RequestedConductor = requestedConductor,
                    LastAssistantResponse = lastAssistantResponse,
                },
                ct);
            var sessionState = prepared.ProfileView.SessionState;
            var context = prepared.AgentContext;
            var conductor = prepared.Conductor;

            var assistantText = new System.Text.StringBuilder();
            StopReason? stopReason = null;
            int inputTokens = 0, outputTokens = 0;
            int toolRounds = 0;
            var collectedEvents = new List<DebugBridgeStreamEventDto>();

            var tools = toolHandlers.ToList();
            await foreach (var evt in orchestrator.HandleStreamAsync(
                sessionState, conductor, model, maxTokens, tools, messages, context, ct: ct))
            {
                switch (evt)
                {
                    case TextDeltaEvent text:
                        assistantText.Append(text.Text);
                        collectedEvents.Add(new DebugBridgeStreamEventDto
                        {
                            Type = "text_delta",
                            Text = text.Text,
                        });
                        break;
                    case ToolCallValidatedEvent tool:
                        toolRounds++;
                        collectedEvents.Add(new DebugBridgeStreamEventDto
                        {
                            Type = "tool",
                            ToolName = tool.ToolName,
                            ToolId = tool.ToolId,
                        });
                        break;
                    case DoneEvent done:
                        stopReason = done.StopReason;
                        inputTokens = done.Usage.InputTokens;
                        outputTokens = done.Usage.OutputTokens;
                        collectedEvents.Add(new DebugBridgeStreamEventDto
                        {
                            Type = "done",
                            StopReason = done.StopReason.ToWireString(),
                            Usage = new DebugBridgeUsageDto
                            {
                                InputTokens = done.Usage.InputTokens,
                                OutputTokens = done.Usage.OutputTokens,
                            },
                        });
                        break;
                    case ReasoningDeltaEvent reasoning:
                        collectedEvents.Add(new DebugBridgeStreamEventDto
                        {
                            Type = "reasoning_delta",
                            Text = reasoning.Text,
                        });
                        break;
                    case DiagnosticEvent diag:
                        collectedEvents.Add(new DebugBridgeStreamEventDto
                        {
                            Type = "diagnostic",
                            Category = diag.Category.ToString().ToLowerInvariant(),
                            Message = diag.Message,
                            Level = diag.Level.ToString().ToLowerInvariant(),
                        });
                        break;
                }
            }

            // Persist the assistant reply
            Guid? assistantNodeId = null;
            if (assistantText.Length > 0)
            {
                var assistantNode = tree.Append(appendParentId, "assistant",
                    new MessageContent(assistantText.ToString()),
                    new MessageMetadata
                    {
                        Model = model,
                        InputTokens = inputTokens,
                        OutputTokens = outputTokens,
                        StopReason = stopReason,
                    });
                assistantNodeId = assistantNode.Id;
            }

            await sessionStore.SaveAsync(tree, ct);

            // Writer pending capture
            string? writerState = null;
            if (assistantText.Length > 0)
            {
                await runtimeService.CaptureWriterPendingAsync(
                    sessionId,
                    new CaptureWriterPendingCommand(assistantText.ToString(), sessionState.Mode.ActiveMode),
                    ct);
            }

            var reloadedState = await runtimeService.LoadViewAsync(sessionId, ct);
            writerState = reloadedState.Writer.State.ToString().ToLowerInvariant();

            var userNodeId = appendParentId;

            return Results.Ok(new DebugBridgeStreamResponse
            {
                SessionId = sessionId,
                Events = collectedEvents,
                FinalContent = assistantText.ToString(),
                NodeIds = new DebugBridgeNodeIds
                {
                    User = userNodeId,
                    Assistant = assistantNodeId,
                },
                Mode = sessionState.Mode.ActiveMode.ToWireString(),
                MessageCount = tree.ToFlatThread().Count,
                ToolRounds = toolRounds,
                StopReason = (stopReason ?? StopReason.EndTurn).ToWireString(),
                Usage = new DebugBridgeUsageDto
                {
                    InputTokens = inputTokens,
                    OutputTokens = outputTokens,
                },
                WriterState = writerState,
            });
        });

        group.MapPost("/mode", async (
            HttpContext httpContext,
            ISessionStateService runtimeService,
            ISessionBootstrapService bootstrapService,
            ISessionLifecycleService lifecycleService,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var mode = root.TryGetProperty("mode", out var modeEl) ? modeEl.GetString() ?? "general" : "general";
            var project = root.TryGetProperty("project", out var proj) ? proj.GetString() : null;
            var file = root.TryGetProperty("file", out var f) ? f.GetString() : null;
            var sessionId = root.GetOptionalGuid("sessionId");
            Guid? createdSessionId = null;

            if (!sessionId.HasValue)
            {
                var tree = await bootstrapService.CreateAsync(
                    new CreateSessionCommand
                    {
                        Name = "Debug Session",
                    },
                    ct);
                sessionId = tree.SessionId;
                createdSessionId = tree.SessionId;
            }

            var result = await runtimeService.SetModeAsync(
                sessionId,
                new SetSessionModeCommand(mode, project, file, null),
                ct);

            if (result.Status == SessionMutationStatus.Busy)
            {
                if (createdSessionId.HasValue)
                {
                    await lifecycleService.DeleteAsync(createdSessionId.Value, ct);
                }

                return Results.Conflict(new
                {
                    error = "session_busy",
                    message = result.Error,
                });
            }

            if (result.Status == SessionMutationStatus.Invalid)
            {
                if (createdSessionId.HasValue)
                {
                    await lifecycleService.DeleteAsync(createdSessionId.Value, ct);
                }

                return Results.BadRequest(new
                {
                    error = "invalid_session_mutation",
                    message = result.Error,
                });
            }

            var sessionState = result.Value!;

            return Results.Ok(new DebugBridgeModeResponse
            {
                SessionId = sessionState.SessionId,
                Mode = sessionState.Mode.ActiveMode.ToWireString(),
                Project = sessionState.Mode.ProjectName,
                File = sessionState.Mode.CurrentFile,
            });
        });

        group.MapGet("/session/{id:guid}", async (
            Guid id,
            ISessionStore sessionStore,
            CancellationToken ct) =>
        {
            try
            {
                var tree = await sessionStore.LoadAsync(id, ct);
                var thread = tree.ToFlatThread();
                return Results.Ok(new DebugBridgeSessionResponse
                {
                    SessionId = tree.SessionId,
                    Name = tree.Name,
                    MessageCount = thread.Count,
                    Messages = thread.Select(n => new DebugBridgeMessageDto
                    {
                        Id = n.Id,
                        Role = n.Role,
                        Content = n.Content.GetText(),
                        CreatedAt = n.CreatedAt,
                    }),
                });
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { error = $"Session {id} not found" });
            }
        });

        group.MapPost("/session/new", async (
            ISessionBootstrapService bootstrapService,
            CancellationToken ct) =>
        {
            var tree = await bootstrapService.CreateAsync(
                new CreateSessionCommand
                {
                    Name = "Debug Session",
                },
                ct);

            return Results.Ok(new
            {
                sessionId = tree.SessionId,
                name = tree.Name,
            });
        });

        group.MapGet("/state", async (ISessionStateService runtimeService, CancellationToken ct) =>
        {
            var state = await runtimeService.LoadViewAsync(null, ct);
            return Results.Ok(new DebugBridgeStateResponse
            {
                Mode = state.Mode.ActiveMode.ToWireString(),
                Project = state.Mode.ProjectName,
                File = state.Mode.CurrentFile,
            });
        });
    }
}
