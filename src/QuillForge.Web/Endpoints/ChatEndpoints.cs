using System.Text.Json;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

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

            var sessionId = root.TryGetProperty("sessionId", out var sid)
                ? Guid.Parse(sid.GetString()!)
                : Guid.CreateVersion7();
            var message = root.GetProperty("message").GetString() ?? "";
            var model = root.TryGetProperty("model", out var m) ? m.GetString() ?? "default" : "default";
            var persona = root.TryGetProperty("persona", out var p) ? p.GetString() ?? "default" : "default";
            var maxTokens = root.TryGetProperty("maxTokens", out var mt) ? mt.GetInt32() : 4096;

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

            // Append user message
            tree.Append(tree.ActiveLeafId, "user", new MessageContent(message));

            // Auto-name session from first user message
            if (tree.Name == "Chat Session" || tree.Name == "New Session")
            {
                var autoName = message.Length <= 50
                    ? message
                    : message.LastIndexOf(' ', 50) is var idx and > 0 ? message[..idx] + "…" : message[..50] + "…";
                // Clean up: remove newlines, trim
                autoName = autoName.ReplaceLineEndings(" ").Trim();
                if (!string.IsNullOrWhiteSpace(autoName))
                {
                    tree.Name = autoName;
                }
            }

            // Build conversation messages for the tool loop
            var messages = tree.ToFlatThread()
                .Select(n => new CompletionMessage(n.Role, n.Content))
                .ToList();

            var context = new AgentContext
            {
                SessionId = sessionId,
                ActiveMode = orchestrator.ActiveModeName,
            };

            // Stream SSE response, collecting assistant text for persistence
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";

            var assistantText = new System.Text.StringBuilder();
            string? stopReason = null;
            int inputTokens = 0, outputTokens = 0;

            var tools = toolHandlers.ToList();
            await foreach (var evt in orchestrator.HandleStreamAsync(
                persona, model, maxTokens, tools, messages, context, ct: ct))
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
                    TextDeltaEvent text => $"data: {JsonSerializer.Serialize(new { Type = "text_delta", Text = text.Text }, s_jsonOptions)}\n\n",
                    ToolCallEvent tool => $"data: {JsonSerializer.Serialize(new { Type = "tool", Name = tool.ToolName, Id = tool.ToolId }, s_jsonOptions)}\n\n",
                    DoneEvent done => $"data: {JsonSerializer.Serialize(new { Type = "done", SessionId = sessionId, Content = assistantText.ToString(), StopReason = done.StopReason, ResponseType = done.ResponseType.ToString(), Usage = new { Input = done.Usage.InputTokens, Output = done.Usage.OutputTokens }, Portrait = GetPortraitUrl(appConfig.Roleplay.AiCharacter, cardStore), User_portrait = GetPortraitUrl(appConfig.Roleplay.UserCharacter, cardStore) }, s_jsonOptions)}\n\n",
                    ReasoningDeltaEvent reasoning => $"data: {JsonSerializer.Serialize(new { Type = "reasoning_delta", Text = reasoning.Text }, s_jsonOptions)}\n\n",
                    DiagnosticEvent diag => $"data: {JsonSerializer.Serialize(new { Type = "diagnostic", Category = diag.Category, Message = diag.Message, Level = diag.Level.ToString().ToLowerInvariant() }, s_jsonOptions)}\n\n",
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
                    var diagWarning = $"data: {JsonSerializer.Serialize(new { Type = "diagnostic", Category = "warning", Message = "Response completed with empty content — model returned no visible text", Level = "warning" }, s_jsonOptions)}\n\n";
                    await httpContext.Response.WriteAsync(diagWarning, ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                }

                logger.LogWarning("Chat stream completed with empty assistant text for session {SessionId}", sessionId);
            }

            // Persist the assistant reply into the conversation tree
            if (assistantText.Length > 0)
            {
                tree.Append(tree.ActiveLeafId, "assistant",
                    new MessageContent(assistantText.ToString()),
                    new MessageMetadata
                    {
                        Model = model,
                        InputTokens = inputTokens,
                        OutputTokens = outputTokens,
                        StopReason = stopReason,
                    });
            }

            await sessionStore.SaveAsync(tree, ct);

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
