using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Web.Endpoints;

public static class SessionEndpoints
{
    public static void MapSessionEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/sessions");

        group.MapGet("/", async (ISessionStore store, CancellationToken ct) =>
        {
            var sessions = await store.ListAsync(ct);
            return Results.Ok(sessions);
        });

        group.MapPost("/new", async (ISessionStore store, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var tree = new ConversationTree(
                Guid.CreateVersion7(),
                "New Session",
                loggerFactory.CreateLogger<ConversationTree>());
            await store.SaveAsync(tree, ct);
            return Results.Ok(new { sessionId = tree.SessionId, name = tree.Name });
        });

        group.MapPost("/{id}/load", async (Guid id, ISessionStore store, CancellationToken ct) =>
        {
            try
            {
                var tree = await store.LoadAsync(id, ct);
                var thread = tree.ToFlatThread();
                return Results.Ok(new
                {
                    sessionId = tree.SessionId,
                    name = tree.Name,
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

        group.MapDelete("/{id}", async (Guid id, ISessionStore store, CancellationToken ct) =>
        {
            await store.DeleteAsync(id, ct);
            return Results.Ok(new { deleted = id });
        });

        group.MapDelete("/{id}/messages/{messageId}", async (
            Guid id, Guid messageId,
            ISessionStore store, ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var tree = await store.LoadAsync(id, ct);
            var removed = tree.Delete(messageId);
            await store.SaveAsync(tree, ct);
            return Results.Ok(new { removed });
        });

        group.MapPost("/{id}/messages/{messageId}/regenerate", async (
            Guid id, Guid messageId,
            ISessionStore store,
            CancellationToken ct) =>
        {
            var tree = await store.LoadAsync(id, ct);
            var node = tree.GetNode(messageId);
            if (node is null)
            {
                return Results.NotFound(new { error = $"Message {messageId} not found" });
            }

            // Create a variant — the actual regeneration (calling the LLM) happens via chat/stream
            // This endpoint just prepares the tree for a new variant
            return Results.Ok(new
            {
                parentId = node.ParentId,
                message = "Use chat/stream with parentId to generate a new variant",
            });
        });
    }
}
