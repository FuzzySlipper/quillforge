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
            return Results.Ok(new { Sessions = sessions });
        });

        group.MapPost("/new", async (ISessionStore store, ILoggerFactory loggerFactory, CancellationToken ct) =>
        {
            var tree = new ConversationTree(
                Guid.CreateVersion7(),
                "New Session",
                loggerFactory.CreateLogger<ConversationTree>());
            await store.SaveAsync(tree, ct);
            return Results.Ok(new { SessionId = tree.SessionId, Name = tree.Name });
        });

        group.MapPost("/{id}/load", async (Guid id, ISessionStore store, CancellationToken ct) =>
        {
            try
            {
                var tree = await store.LoadAsync(id, ct);
                var thread = tree.ToFlatThread();
                return Results.Ok(new
                {
                    SessionId = tree.SessionId,
                    Name = tree.Name,
                    Messages = thread.Select(n => new
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
                return Results.NotFound(new { Error = $"Session {id} not found" });
            }
        });

        group.MapDelete("/{id}", async (Guid id, ISessionStore store, CancellationToken ct) =>
        {
            await store.DeleteAsync(id, ct);
            return Results.Ok(new { Deleted = id });
        });

        group.MapDelete("/{id}/messages/{messageId}", async (
            Guid id, Guid messageId,
            ISessionStore store, ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var tree = await store.LoadAsync(id, ct);
            var removed = tree.Delete(messageId);
            await store.SaveAsync(tree, ct);
            return Results.Ok(new { Removed = removed });
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
                return Results.NotFound(new { Error = $"Message {messageId} not found" });
            }

            // Create a variant — the actual regeneration (calling the LLM) happens via chat/stream
            // This endpoint just prepares the tree for a new variant
            return Results.Ok(new
            {
                ParentId = node.ParentId,
                Message = "Use chat/stream with parentId to generate a new variant",
            });
        });
    }
}
