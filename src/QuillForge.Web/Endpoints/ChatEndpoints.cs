using System.Text.Json;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Contracts;

namespace QuillForge.Web.Endpoints;

public static class ChatEndpoints
{
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static void MapChatEndpoints(this WebApplication app)
    {
        app.MapPost("/api/chat/stream", async (
            HttpContext httpContext,
            OrchestratorAgent orchestrator,
            ISessionRuntimeService runtimeService,
            IInteractiveSessionContextService sessionContextService,
            ISessionStore sessionStore,
            IEnumerable<IToolHandler> toolHandlers,
            ICharacterCardStore cardStore,
            AppConfig appConfig,
            ILoggerFactory loggerFactory,
            ILogger<Program> logger,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var sessionId = root.GetOptionalGuid("sessionId") ?? Guid.CreateVersion7();
            var message = root.TryGetProperty("message", out var msgEl) ? msgEl.GetString() ?? "" : "";
            var model = root.TryGetProperty("model", out var m) ? m.GetString() ?? "default" : "default";
            // Resolve "default" to the configured orchestrator model
            if (string.Equals(model, "default", StringComparison.OrdinalIgnoreCase))
                model = appConfig.Models.Orchestrator;
            var requestedConductor = root.GetOptionalString("persona");
            var maxTokens = root.TryGetProperty("maxTokens", out var mt) ? mt.GetInt32() : 4096;
            var parentId = root.GetOptionalGuid("parentId");

            logger.LogInformation(
                "Chat request: session={SessionId}, model={Model}, message length={Length}",
                sessionId, model, message.Length);

            // Load or create session
            ConversationTree tree;
            try
            {
                tree = await sessionStore.LoadAsync(sessionId, ct);
            }
            catch (FileNotFoundException)
            {
                tree = new ConversationTree(sessionId, "Chat Session",
                    loggerFactory.CreateLogger<ConversationTree>());
            }

            // Regeneration mode: parentId means "generate a new variant as a child of this node"
            // Normal mode: append the user message first, then generate
            Guid appendParentId;
            if (parentId.HasValue)
            {
                // Variant/regenerate: don't append a user message.
                // Build the thread up to (and including) parentId, then generate a new assistant sibling.
                appendParentId = parentId.Value;
                tree.ActiveLeafId = parentId.Value;
            }
            else
            {
                // Normal: append user message to the current active leaf
                tree.Append(tree.ActiveLeafId, "user", new MessageContent(message));
                appendParentId = tree.ActiveLeafId;

                // Auto-name session from first user message
                if (tree.Name == "Chat Session" || tree.Name == "New Session")
                {
                    var autoName = message.Length <= 50
                        ? message
                        : message.LastIndexOf(' ', 50) is var idx and > 0 ? message[..idx] + "…" : message[..50] + "…";
                    autoName = autoName.ReplaceLineEndings(" ").Trim();
                    if (!string.IsNullOrWhiteSpace(autoName))
                    {
                        tree.Name = autoName;
                    }
                }
            }

            // Build conversation messages from the thread up to the current position
            var thread = tree.ToFlatThread();
            var messages = thread
                .Select(n => new CompletionMessage(n.Role, n.Content))
                .ToList();
            var lastAssistantResponse = thread
                .LastOrDefault(n => string.Equals(n.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                ?.Content
                .GetText();

            // Load per-session runtime state
            var sessionState = await runtimeService.LoadViewAsync(sessionId, ct);
            var sessionContext = await sessionContextService.BuildAsync(sessionState, ct);

            var context = new AgentContext
            {
                SessionId = sessionId,
                ActiveMode = sessionState.Mode.ActiveModeName,
                ActiveLoreSet = sessionState.Profile.ActiveLoreSet ?? "default",
                ActiveNarrativeRules = sessionState.Profile.ActiveNarrativeRules ?? "default",
                ActiveWritingStyle = sessionState.Profile.ActiveWritingStyle ?? "default",
                SessionContext = sessionContext,
                LastAssistantResponse = lastAssistantResponse,
            };
            var conductor = string.IsNullOrWhiteSpace(requestedConductor)
                ? sessionState.Profile.ActivePersona ?? "default"
                : requestedConductor;

            // Stream SSE response, collecting assistant text for persistence
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";

            var assistantText = new System.Text.StringBuilder();
            string? stopReason = null;
            int inputTokens = 0, outputTokens = 0;

            var tools = toolHandlers.ToList();
            await foreach (var evt in orchestrator.HandleStreamAsync(
                sessionState, conductor, model, maxTokens, tools, messages, context, ct: ct))
            {
                switch (evt)
                {
                    case TextDeltaEvent text:
                        assistantText.Append(text.Text);
                        break;
                    case DoneEvent done:
                        stopReason = done.StopReason;
                        inputTokens = done.Usage.InputTokens;
                        outputTokens = done.Usage.OutputTokens;
                        break;
                }

                var eventData = evt switch
                {
                    TextDeltaEvent text => $"data: {JsonSerializer.Serialize(new ChatTextDeltaDto { Text = text.Text }, s_jsonOptions)}\n\n",
                    ToolCallEvent tool => $"data: {JsonSerializer.Serialize(new ChatToolDto { Name = tool.ToolName, Id = tool.ToolId }, s_jsonOptions)}\n\n",
                    DoneEvent done => $"data: {JsonSerializer.Serialize(new ChatDoneDto { SessionId = sessionId, ParentId = appendParentId, Content = assistantText.ToString(), StopReason = done.StopReason, ResponseType = done.ResponseType.ToString(), Usage = new ChatUsageDto { Input = done.Usage.InputTokens, Output = done.Usage.OutputTokens }, Portrait = GetPortraitUrl(appConfig.Roleplay.AiCharacter, cardStore), UserPortrait = GetPortraitUrl(appConfig.Roleplay.UserCharacter, cardStore) }, s_jsonOptions)}\n\n",
                    ReasoningDeltaEvent reasoning => $"data: {JsonSerializer.Serialize(new ChatReasoningDeltaDto { Text = reasoning.Text }, s_jsonOptions)}\n\n",
                    DiagnosticEvent diag => $"data: {JsonSerializer.Serialize(new ChatDiagnosticDto { Category = diag.Category, Message = diag.Message, Level = diag.Level.ToString().ToLowerInvariant() }, s_jsonOptions)}\n\n",
                    _ => null,
                };

                if (eventData is not null)
                {
                    await httpContext.Response.WriteAsync(eventData, ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                }
            }

            // Guard: if stream completed with no visible text, surface a fallback
            if (assistantText.Length == 0)
            {
                if (appConfig.Diagnostics.LivePanel)
                {
                    var diagWarning = $"data: {JsonSerializer.Serialize(new ChatDiagnosticDto { Category = "warning", Message = "Response completed with empty content — model returned no visible text", Level = "warning" }, s_jsonOptions)}\n\n";
                    await httpContext.Response.WriteAsync(diagWarning, ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                }

                logger.LogWarning("Chat stream completed with empty assistant text for session {SessionId}", sessionId);
            }

            // Persist the assistant reply into the conversation tree
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
            if (assistantText.Length > 0)
            {
                var pendingCapture = await runtimeService.CaptureWriterPendingAsync(
                    sessionId,
                    new CaptureWriterPendingCommand(assistantText.ToString(), sessionState.Mode.ActiveModeName),
                    ct);
                if (pendingCapture.Status == SessionMutationStatus.Busy)
                {
                    logger.LogWarning(
                        "Writer pending capture skipped because the session was busy: session={SessionId}",
                        sessionId);
                }
            }

            // Send persisted event with backend node IDs so the frontend can update message identity
            var userNodeId = parentId.HasValue ? (Guid?)null : appendParentId;
            var persistedData = $"data: {JsonSerializer.Serialize(new ChatPersistedDto { NodeId = assistantNodeId, UserNodeId = userNodeId }, s_jsonOptions)}\n\n";
            await httpContext.Response.WriteAsync(persistedData, ct);
            await httpContext.Response.Body.FlushAsync(ct);

            logger.LogInformation("Chat completed for session {SessionId}", sessionId);
        });

        // POST /api/council — run council advisors and stream results as SSE
        app.MapPost("/api/council", async (
            HttpContext httpContext,
            ICouncilService councilService,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var query = body.RootElement.TryGetProperty("query", out var q)
                ? q.GetString() ?? ""
                : "";

            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";

            var result = await councilService.RunCouncilAsync(query, ct);

            // Stream each member's response as a text_delta, then done
            foreach (var member in result.Members)
            {
                var memberEvent = JsonSerializer.Serialize(new
                {
                    Type = "text_delta",
                    Text = $"**{member.Name}** ({member.Model}):\n{member.Content}\n\n",
                }, s_jsonOptions);
                await httpContext.Response.WriteAsync($"data: {memberEvent}\n\n", ct);
                await httpContext.Response.Body.FlushAsync(ct);
            }

            var fullContent = string.Join("\n\n", result.Members.Select(m =>
                $"**{m.Name}** ({m.Model}):\n{m.Content}"));
            var doneEvent = JsonSerializer.Serialize(new
            {
                Type = "done",
                Content = fullContent,
                StopReason = "end_turn",
            }, s_jsonOptions);
            await httpContext.Response.WriteAsync($"data: {doneEvent}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
        });

        // POST /api/artifact — generate an artifact via SSE streaming
        app.MapPost("/api/artifact", async (
            HttpContext httpContext,
            ICompletionService completionService,
            IArtifactService artifactService,
            AppConfig appConfig,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var prompt = root.TryGetProperty("prompt", out var pEl) ? pEl.GetString() ?? "" : "";
            var formatStr = root.TryGetProperty("format", out var fEl) ? fEl.GetString() ?? "prose" : "prose";

            if (!Enum.TryParse<ArtifactFormat>(formatStr, ignoreCase: true, out var format))
            {
                format = ArtifactFormat.Prose;
            }

            var systemPrompt = artifactService.BuildPrompt(prompt, format);

            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";

            var artifactText = new System.Text.StringBuilder();

            var request = new CompletionRequest
            {
                Model = appConfig.Models.Artifact,
                MaxTokens = appConfig.Agents.Artifact.MaxTokens,
                SystemPrompt = systemPrompt,
                Messages = [new CompletionMessage("user", new MessageContent(prompt))],
            };

            await foreach (var evt in completionService.StreamAsync(request, ct))
            {
                switch (evt)
                {
                    case TextDeltaEvent text:
                        artifactText.Append(text.Text);
                        var textEvent = JsonSerializer.Serialize(new { Type = "text_delta", Text = text.Text }, s_jsonOptions);
                        await httpContext.Response.WriteAsync($"data: {textEvent}\n\n", ct);
                        await httpContext.Response.Body.FlushAsync(ct);
                        break;
                }
            }

            // Save the artifact
            var content = artifactText.ToString();
            if (!string.IsNullOrWhiteSpace(content))
            {
                var artifact = new Artifact
                {
                    Format = format,
                    Content = content,
                    CreatedAt = DateTimeOffset.UtcNow,
                };
                artifactService.SetCurrent(artifact);
                await artifactService.SaveAsync(artifact, ct);
            }

            var done = JsonSerializer.Serialize(new
            {
                Type = "done",
                Content = content,
                StopReason = "end_turn",
            }, s_jsonOptions);
            await httpContext.Response.WriteAsync($"data: {done}\n\n", ct);
            await httpContext.Response.Body.FlushAsync(ct);
        });
    }

    private static string? GetPortraitUrl(string? characterName, ICharacterCardStore cardStore)
    {
        if (string.IsNullOrEmpty(characterName)) return null;
        var card = cardStore.LoadAsync(characterName).GetAwaiter().GetResult();
        return card?.Portrait is not null ? $"/content/character-cards/{card.Portrait}" : null;
    }
}
