using System.Text.Json;
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

        // Fork: create a new session with the thread up to the given message
        group.MapPost("/{id}/fork", async (
            Guid id,
            HttpContext httpContext,
            ISessionStore store,
            ILoggerFactory loggerFactory,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var messageId = body.RootElement.TryGetProperty("messageId", out var mEl)
                ? Guid.Parse(mEl.GetString()!)
                : (Guid?)null;

            var source = await store.LoadAsync(id, ct);

            // Get thread up to the specified message (or full thread if not specified)
            var thread = messageId.HasValue
                ? source.GetThread(messageId.Value)
                : source.GetThread();

            // Create new session and replay messages (skip root node)
            var newTree = new ConversationTree(
                Guid.CreateVersion7(),
                $"Fork of {source.Name}",
                loggerFactory.CreateLogger<ConversationTree>());

            foreach (var node in thread.Skip(1)) // skip synthetic root
            {
                newTree.Append(newTree.ActiveLeafId, node.Role, node.Content, node.Metadata);
            }

            await store.SaveAsync(newTree, ct);

            return Results.Ok(new
            {
                SessionId = newTree.SessionId,
                Name = newTree.Name,
                MessageCount = newTree.ToFlatThread().Count,
            });
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
