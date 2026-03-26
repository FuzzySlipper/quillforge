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

            // Build conversation messages for the tool loop
            var messages = tree.ToFlatThread()
                .Select(n => new CompletionMessage(n.Role, n.Content))
                .ToList();

            var context = new AgentContext
            {
                SessionId = sessionId,
                ActiveMode = orchestrator.ActiveModeName,
            };

            // Stream SSE response
            httpContext.Response.ContentType = "text/event-stream";
            httpContext.Response.Headers.CacheControl = "no-cache";

            await foreach (var evt in orchestrator.HandleStreamAsync(
                persona, model, maxTokens, [], messages, context, ct: ct))
            {
                var eventData = evt switch
                {
                    TextDeltaEvent text => $"data: {JsonSerializer.Serialize(new { Type = "text_delta", Text = text.Text }, s_jsonOptions)}\n\n",
                    ToolCallEvent tool => $"data: {JsonSerializer.Serialize(new { Type = "tool_call", Name = tool.ToolName, Id = tool.ToolId }, s_jsonOptions)}\n\n",
                    DoneEvent done => $"data: {JsonSerializer.Serialize(new { Type = "done", StopReason = done.StopReason, Usage = new { Input = done.Usage.InputTokens, Output = done.Usage.OutputTokens } }, s_jsonOptions)}\n\n",
                    ReasoningDeltaEvent reasoning => $"data: {JsonSerializer.Serialize(new { Type = "reasoning_delta", Text = reasoning.Text }, s_jsonOptions)}\n\n",
                    _ => null,
                };

                if (eventData is not null)
                {
                    await httpContext.Response.WriteAsync(eventData, ct);
                    await httpContext.Response.Body.FlushAsync(ct);
                }
            }

            // Save session after completion
            await sessionStore.SaveAsync(tree, ct);

            logger.LogInformation("Chat completed for session {SessionId}", sessionId);
        });
    }
}
