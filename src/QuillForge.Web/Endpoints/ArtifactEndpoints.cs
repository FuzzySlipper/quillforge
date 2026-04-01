using QuillForge.Core.Services;

namespace QuillForge.Web.Endpoints;

public static class ArtifactEndpoints
{
    public static void MapArtifactEndpoints(this WebApplication app)
    {
        // Artifact endpoints
        app.MapGet("/api/artifact/current", (IArtifactService artifacts) =>
        {
            var current = artifacts.GetCurrent();
            return Results.Ok(new { Artifact = current });
        });

        app.MapGet("/api/artifact/formats", (IArtifactService artifacts) =>
        {
            var formats = artifacts.GetFormatInstructions()
                .Select(kvp => new { Name = kvp.Key.ToString().ToLowerInvariant(), Description = kvp.Value })
                .ToList();
            return Results.Ok(new { Formats = formats });
        });

        app.MapGet("/api/artifacts", async (IArtifactService artifacts, CancellationToken ct) =>
        {
            var list = await artifacts.ListAsync(ct);
            return Results.Ok(new { Artifacts = list });
        });

        app.MapPost("/api/artifact/clear", (IArtifactService artifacts) =>
        {
            artifacts.ClearCurrent();
            return Results.Ok(new { Status = "cleared" });
        });
    }
}
