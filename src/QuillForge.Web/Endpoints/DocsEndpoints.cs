using QuillForge.Core.Services;

namespace QuillForge.Web.Endpoints;

public static class DocsEndpoints
{
    public static void MapDocsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/docs", async (IDocsService docsService, CancellationToken ct) =>
        {
            var topics = await docsService.ListTopicsAsync(ct);
            return Results.Ok(new { Topics = topics });
        });

        app.MapGet("/api/docs/search", async (string q, IDocsService docsService, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(q))
            {
                return Results.BadRequest(new { Error = "Query parameter 'q' is required" });
            }

            var results = await docsService.SearchAsync(q, ct);
            return Results.Ok(new { Results = results });
        });

        // This must come after /api/docs/search to avoid route conflicts
        app.MapGet("/api/docs/{slug}", async (string slug, IDocsService docsService, CancellationToken ct) =>
        {
            var entry = await docsService.GetTopicAsync(slug, ct);
            if (entry is null)
            {
                return Results.NotFound(new { Error = $"Doc topic '{slug}' not found" });
            }

            return Results.Ok(entry);
        });
    }
}
