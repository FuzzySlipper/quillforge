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

        group.MapPost("/chat", async (
            HttpContext httpContext,
            OrchestratorAgent orchestrator,
            ISessionRuntimeStore runtimeStore,
            ISessionStore sessionStore,
            IEnumerable<IToolHandler> toolHandlers,
            AppConfig appConfig,
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

            tree.Append(tree.ActiveLeafId, "user", new MessageContent(message));

            var messages = tree.ToFlatThread()
                .Select(n => new CompletionMessage(n.Role, n.Content))
                .ToList();

            var sessionState = await runtimeStore.LoadAsync(sessionId, ct);
            var context = new AgentContext
            {
                SessionId = sessionId,
                ActiveMode = sessionState.Mode.ActiveModeName,
                ActiveLoreSet = sessionState.Profile.ActiveLoreSet ?? appConfig.Lore.Active,
                ActiveWritingStyle = sessionState.Profile.ActiveWritingStyle ?? appConfig.WritingStyle.Active,
            };

            var tools = toolHandlers.ToList();
            var response = await orchestrator.HandleAsync(
                sessionState, persona, model, maxTokens, tools, messages, context, ct: ct);

            tree.Append(tree.ActiveLeafId, "assistant", response.Content, new MessageMetadata
            {
                Model = model,
                InputTokens = response.Usage.InputTokens,
                OutputTokens = response.Usage.OutputTokens,
                StopReason = response.StopReason,
            });

            await sessionStore.SaveAsync(tree, ct);
            await runtimeStore.SaveAsync(sessionState, ct);

            return Results.Ok(new
            {
                sessionId,
                responseText = response.Content.GetText(),
                response.StopReason,
                response.ToolRoundsUsed,
                usage = new { response.Usage.InputTokens, response.Usage.OutputTokens },
                mode = sessionState.Mode.ActiveModeName,
                messageCount = tree.ToFlatThread().Count,
            });
        });

        group.MapPost("/mode", async (
            HttpContext httpContext,
            OrchestratorAgent orchestrator,
            ISessionRuntimeStore runtimeStore,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var mode = root.GetProperty("mode").GetString()!;
            var project = root.TryGetProperty("project", out var proj) ? proj.GetString() : null;
            var file = root.TryGetProperty("file", out var f) ? f.GetString() : null;
            var sessionId = root.TryGetProperty("sessionId", out var sid)
                ? Guid.Parse(sid.GetString()!) : (Guid?)null;

            var sessionState = await runtimeStore.LoadAsync(sessionId, ct);
            orchestrator.SetMode(sessionState, mode, project, file);
            await runtimeStore.SaveAsync(sessionState, ct);

            return Results.Ok(new
            {
                mode = sessionState.Mode.ActiveModeName,
                project = sessionState.Mode.ProjectName,
                file = sessionState.Mode.CurrentFile,
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
                return Results.Ok(new
                {
                    sessionId = tree.SessionId,
                    name = tree.Name,
                    messageCount = thread.Count,
                    messages = thread.Select(n => new
                    {
                        id = n.Id,
                        role = n.Role,
                        content = n.Content.GetText(),
                        createdAt = n.CreatedAt,
                    }),
                });
            }
            catch (FileNotFoundException)
            {
                return Results.NotFound(new { error = $"Session {id} not found" });
            }
        });

        group.MapPost("/session/new", async (
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

        group.MapGet("/state", async (ISessionRuntimeStore runtimeStore, CancellationToken ct) =>
        {
            var state = await runtimeStore.LoadAsync(null, ct);
            return Results.Ok(new
            {
                mode = state.Mode.ActiveModeName,
                project = state.Mode.ProjectName,
                file = state.Mode.CurrentFile,
            });
        });
    }
}
