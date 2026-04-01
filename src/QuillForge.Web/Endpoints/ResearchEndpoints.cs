using QuillForge.Core;
using QuillForge.Core.Services;

namespace QuillForge.Web.Endpoints;

public static class ResearchEndpoints
{
    public static void MapResearchEndpoints(this WebApplication app, string contentRoot)
    {
        var group = app.MapGroup("/api/research");

        // List research projects (subdirectories of research/)
        group.MapGet("/projects", (IContentFileService fileService, CancellationToken ct) =>
        {
            var researchDir = Path.Combine(contentRoot, ContentPaths.Research);
            if (!Directory.Exists(researchDir))
            {
                return Results.Ok(new { Projects = Array.Empty<string>() });
            }

            var projects = Directory.GetDirectories(researchDir)
                .Select(Path.GetFileName)
                .Where(name => !string.IsNullOrEmpty(name))
                .OrderBy(name => name)
                .ToList();

            return Results.Ok(new { Projects = projects });
        });

        // List files in a research project
        group.MapGet("/projects/{project}", async (string project, IContentFileService fileService, CancellationToken ct) =>
        {
            var files = await fileService.ListAsync($"research/{project}", "*.md", ct);
            var items = files.Select(f =>
            {
                var name = Path.GetFileName(f);
                return new { Name = name, Path = f };
            }).ToList();

            return Results.Ok(new { Files = items });
        });

        // Read a research file
        group.MapGet("/projects/{project}/{file}", async (string project, string file, IContentFileService fileService, CancellationToken ct) =>
        {
            var path = $"research/{project}/{file}";
            if (!await fileService.ExistsAsync(path, ct))
                return Results.NotFound(new { Error = "File not found" });

            var content = await fileService.ReadAsync(path, ct);
            return Results.Ok(new { Content = content });
        });

        // Delete a research file
        group.MapDelete("/projects/{project}/{file}", async (string project, string file, IContentFileService fileService, CancellationToken ct) =>
        {
            var path = $"research/{project}/{file}";
            if (!await fileService.ExistsAsync(path, ct))
                return Results.NotFound(new { Error = "File not found" });

            await fileService.DeleteAsync(path, ct);
            return Results.Ok(new { Deleted = path });
        });

        // Delete an entire research project
        group.MapDelete("/projects/{project}", (string project) =>
        {
            var dir = Path.Combine(contentRoot, ContentPaths.Research, project);
            if (!Directory.Exists(dir))
                return Results.NotFound(new { Error = "Project not found" });

            Directory.Delete(dir, recursive: true);
            return Results.Ok(new { Deleted = project });
        });
    }
}
