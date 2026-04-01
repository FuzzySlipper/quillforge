using System.Text.Json;
using QuillForge.Core;
using QuillForge.Core.Services;

namespace QuillForge.Web.Endpoints;

public static class CouncilEndpoints
{
    public static void MapCouncilEndpoints(this WebApplication app)
    {
        // Prompts (council advisors) endpoint
        app.MapGet("/api/council", async (IContentFileService fileService, CancellationToken ct) =>
        {
            var files = await fileService.ListAsync(ContentPaths.Council, "*.md", ct);
            var advisors = new List<object>();
            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file.Split('/').Last());
                try
                {
                    var content = await fileService.ReadAsync(file, ct);
                    advisors.Add(new { Name = name, Content = content });
                }
                catch { /* skip unreadable */ }
            }
            return Results.Ok(new { Advisors = advisors });
        });

        // Council member CRUD
        app.MapGet("/api/council/members", async (ICouncilService councilService, CancellationToken ct) =>
        {
            var members = await councilService.LoadMembersAsync(ct);
            return Results.Ok(new
            {
                Members = members.Select(m => new
                {
                    m.Name,
                    m.Model,
                    m.ProviderAlias,
                    m.SystemPrompt,
                })
            });
        });

        app.MapPost("/api/council/members", async (HttpContext httpContext, IContentFileService fileService, CancellationToken ct) =>
        {
            var body = await JsonSerializer.DeserializeAsync<JsonElement>(httpContext.Request.Body, cancellationToken: ct);
            var name = body.GetProperty("name").GetString()?.Trim().ToLowerInvariant() ?? "";

            if (string.IsNullOrEmpty(name) || !System.Text.RegularExpressions.Regex.IsMatch(name, @"^[a-z0-9\-]+$"))
                return Results.BadRequest(new { Error = "Name must be lowercase alphanumeric with hyphens only" });

            var path = $"council/{name}.md";
            if (await fileService.ExistsAsync(path, ct))
                return Results.Conflict(new { Error = $"Member '{name}' already exists" });

            var model = body.TryGetProperty("model", out var m) ? m.GetString() : null;
            var provider = body.TryGetProperty("providerAlias", out var p) ? p.GetString() : null;
            var systemPrompt = body.TryGetProperty("systemPrompt", out var sp) ? sp.GetString() ?? "" : "";

            var content = SerializeMemberFile(model, provider, systemPrompt);
            await fileService.WriteAsync(path, content, ct);
            return Results.Ok(new { Name = name });
        });

        app.MapPut("/api/council/members/{name}", async (string name, HttpContext httpContext, IContentFileService fileService, CancellationToken ct) =>
        {
            var path = $"council/{name}.md";
            if (!await fileService.ExistsAsync(path, ct))
                return Results.NotFound(new { Error = $"Member '{name}' not found" });

            var body = await JsonSerializer.DeserializeAsync<JsonElement>(httpContext.Request.Body, cancellationToken: ct);
            var model = body.TryGetProperty("model", out var m) ? m.GetString() : null;
            var provider = body.TryGetProperty("providerAlias", out var p) ? p.GetString() : null;
            var systemPrompt = body.TryGetProperty("systemPrompt", out var sp) ? sp.GetString() ?? "" : "";

            var content = SerializeMemberFile(model, provider, systemPrompt);
            await fileService.WriteAsync(path, content, ct);
            return Results.Ok(new { Name = name });
        });

        app.MapDelete("/api/council/members/{name}", async (string name, IContentFileService fileService, CancellationToken ct) =>
        {
            var path = $"council/{name}.md";
            if (!await fileService.ExistsAsync(path, ct))
                return Results.NotFound(new { Error = $"Member '{name}' not found" });

            await fileService.DeleteAsync(path, ct);
            return Results.Ok(new { Deleted = name });
        });
    }

    private static string SerializeMemberFile(string? model, string? provider, string systemPrompt)
    {
        var lines = new List<string>();
        if (!string.IsNullOrEmpty(model)) lines.Add($"model: {model}");
        if (!string.IsNullOrEmpty(provider)) lines.Add($"provider: {provider}");
        lines.Add("");
        lines.Add(systemPrompt.Trim());
        return string.Join("\n", lines);
    }
}
