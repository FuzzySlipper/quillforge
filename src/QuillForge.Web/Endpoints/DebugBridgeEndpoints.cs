using System.Text.Json;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

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

        // POST /api/debug/bridge/chat — send a message, get full response as JSON (non-streaming)
        group.MapPost("/chat", async (
            HttpContext httpContext,
            OrchestratorAgent orchestrator,
            ISessionStore sessionStore,
            IEnumerable<IToolHandler> toolHandlers,
            ILoggerFactory loggerFactory,
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

            // Load or create session
            ConversationTree tree;
            try
            {
                tree = await sessionStore.LoadAsync(sessionId, ct);
            }
            catch (FileNotFoundException)
            {
                tree = new ConversationTree(sessionId, "Debug Session",
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

            var tools = toolHandlers.ToList();
            var response = await orchestrator.HandleAsync(
                persona, model, maxTokens, tools, messages, context, ct: ct);

            // Append assistant response to the tree
            tree.Append(tree.ActiveLeafId, "assistant", response.Content, new MessageMetadata
            {
                Model = model,
                InputTokens = response.Usage.InputTokens,
                OutputTokens = response.Usage.OutputTokens,
                StopReason = response.StopReason,
            });

            await sessionStore.SaveAsync(tree, ct);

            return Results.Ok(new
            {
                sessionId,
                responseText = response.Content.GetText(),
                response.StopReason,
                response.ToolRoundsUsed,
                usage = new { response.Usage.InputTokens, response.Usage.OutputTokens },
                mode = orchestrator.ActiveModeName,
                messageCount = tree.ToFlatThread().Count,
            });
        });

        // POST /api/debug/bridge/mode — switch orchestrator mode
        group.MapPost("/mode", async (
            HttpContext httpContext,
            OrchestratorAgent orchestrator,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var mode = root.GetProperty("mode").GetString()!;
            var project = root.TryGetProperty("project", out var proj) ? proj.GetString() : null;
            var file = root.TryGetProperty("file", out var f) ? f.GetString() : null;

            orchestrator.SetMode(mode, project, file);

            return Results.Ok(new
            {
                mode = orchestrator.ActiveModeName,
                project = orchestrator.ProjectName,
                file = orchestrator.CurrentFile,
            });
        });

        // GET /api/debug/bridge/session/{id} — inspect full session state
        group.MapGet("/session/{id:guid}", async (
            Guid id,
            ISessionStore sessionStore,
            CancellationToken ct) =>
        {
            ConversationTree tree;
            try
            {
                tree = await sessionStore.LoadAsync(id, ct);
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { error = "Session not found" });
            }

            var thread = tree.ToFlatThread();
            return Results.Ok(new
            {
                sessionId = tree.SessionId,
                name = tree.Name,
                messageCount = thread.Count,
                messages = thread.Select(n => new
                {
                    id = n.Id,
                    n.Role,
                    text = n.Content.GetText(),
                    n.CreatedAt,
                    n.Metadata,
                }),
                activeLeafId = tree.ActiveLeafId,
            });
        });

        // POST /api/debug/bridge/session/reset — create a fresh session
        group.MapPost("/session/reset", async (
            ISessionStore sessionStore,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var sessionId = Guid.CreateVersion7();
            var tree = new ConversationTree(sessionId, "Debug Session",
                loggerFactory.CreateLogger<ConversationTree>());
            await sessionStore.SaveAsync(tree, ct);

            return Results.Ok(new
            {
                sessionId,
                name = tree.Name,
            });
        });

        // GET /api/debug/bridge/state — current orchestrator state
        group.MapGet("/state", (OrchestratorAgent orchestrator) =>
        {
            return Results.Ok(new
            {
                mode = orchestrator.ActiveModeName,
                project = orchestrator.ProjectName,
                file = orchestrator.CurrentFile,
            });
        });
    }
}
