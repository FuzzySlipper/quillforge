using System.Text.Json;
using QuillForge.Providers.Registry;

namespace QuillForge.Web.Endpoints;

public static class ProviderEndpoints
{
    public static void MapProviderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/providers");

        group.MapGet("/", (ProviderRegistry registry) =>
        {
            var providers = registry.ListProviders()
                .Select(p => new { p.Alias, type = p.Type.ToString() });
            return Results.Ok(providers);
        });

        group.MapPost("/", async (HttpContext httpContext, ProviderRegistry registry) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body);
            var root = body.RootElement;

            var config = new ProviderConfig
            {
                Alias = root.GetProperty("alias").GetString()!,
                Type = Enum.Parse<ProviderType>(root.GetProperty("type").GetString()!, ignoreCase: true),
                ApiKey = root.GetProperty("apiKey").GetString()!,
                BaseUrl = root.TryGetProperty("baseUrl", out var bu) ? bu.GetString() : null,
                DefaultModel = root.TryGetProperty("defaultModel", out var dm) ? dm.GetString() : null,
            };

            registry.Register(config);
            return Results.Ok(new { registered = config.Alias });
        });

        group.MapDelete("/{alias}", (string alias, ProviderRegistry registry) =>
        {
            var removed = registry.Remove(alias);
            return removed ? Results.Ok(new { deleted = alias }) : Results.NotFound();
        });

        group.MapPost("/test", async (HttpContext httpContext, ProviderRegistry registry, CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var alias = body.RootElement.GetProperty("alias").GetString()!;

            var success = await registry.TestConnectionAsync(alias, ct);
            return Results.Ok(new { alias, success });
        });
    }
}
