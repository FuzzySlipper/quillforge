using System.Text.Json;
using QuillForge.Providers.Registry;
using QuillForge.Storage.FileSystem;

namespace QuillForge.Web.Endpoints;

public static class ProviderEndpoints
{
    public static void MapProviderEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/providers");

        group.MapGet("/", (ProviderRegistry registry) =>
        {
            var providers = registry.ListProviders()
                .Select(p =>
                {
                    var config = registry.GetConfig(p.Alias);
                    return new
                    {
                        Alias = p.Alias,
                        Name = p.Alias,
                        Type = p.Type.ToString(),
                        DefaultModel = config?.DefaultModel,
                        BaseUrl = config?.BaseUrl,
                        ApiKeySet = !string.IsNullOrEmpty(config?.ApiKey),
                        UsedBy = Array.Empty<string>(),
                    };
                });
            return Results.Ok(new { Providers = providers });
        });

        group.MapPost("/", async (HttpContext httpContext, ProviderRegistry registry, ProviderConfigStore store, ILogger<ProviderRegistry> logger) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body);
            var root = body.RootElement;

            var alias = root.TryGetProperty("alias", out var aliasEl) ? aliasEl.GetString() ?? "unnamed" : "unnamed";
            var typeStr = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "Custom" : "Custom";
            var apiKey = root.TryGetProperty("apiKey", out var keyEl) ? keyEl.GetString() ?? "" : "";
            var baseUrl = root.TryGetProperty("baseUrl", out var urlEl) ? urlEl.GetString() : null;
            var defaultModel = root.TryGetProperty("defaultModel", out var modelEl) ? modelEl.GetString() : null;

            if (!Enum.TryParse<ProviderType>(typeStr, ignoreCase: true, out var providerType))
            {
                // Map common names to ProviderType
                providerType = typeStr.ToLowerInvariant() switch
                {
                    "openai-compatible" or "openai_compatible" or "custom" => ProviderType.Custom,
                    "openai" => ProviderType.OpenAI,
                    "anthropic" or "claude" => ProviderType.Anthropic,
                    "ollama" => ProviderType.Ollama,
                    "openrouter" => ProviderType.OpenRouter,
                    "azure" or "azure_openai" or "azureopenai" => ProviderType.AzureOpenAI,
                    _ => ProviderType.Custom,
                };
            }

            var config = new ProviderConfig
            {
                Alias = alias,
                Type = providerType,
                ApiKey = apiKey,
                BaseUrl = baseUrl,
                DefaultModel = defaultModel,
            };

            logger.LogInformation(
                "Registering provider: alias={Alias}, type={Type}, baseUrl={BaseUrl}, model={Model}",
                alias, providerType, baseUrl, defaultModel);

            registry.Register(config);
            await SaveProvidersToDisk(registry, store);
            return Results.Ok(new { Registered = config.Alias });
        });

        group.MapPut("/{alias}", async (string alias, HttpContext httpContext, ProviderRegistry registry, ProviderConfigStore store) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body);
            var root = body.RootElement;

            var existing = registry.GetConfig(alias);
            if (existing is null)
            {
                return Results.NotFound(new { Error = $"Provider '{alias}' not found" });
            }

            // Keep existing API key if not provided (don't overwrite with blank)
            var newApiKey = root.TryGetProperty("apiKey", out var keyEl) ? keyEl.GetString() : null;

            var config = existing with
            {
                ApiKey = !string.IsNullOrEmpty(newApiKey) ? newApiKey : existing.ApiKey,
                BaseUrl = root.TryGetProperty("baseUrl", out var urlEl) ? urlEl.GetString() ?? existing.BaseUrl : existing.BaseUrl,
                DefaultModel = root.TryGetProperty("defaultModel", out var modelEl) ? modelEl.GetString() ?? existing.DefaultModel : existing.DefaultModel,
            };

            registry.Register(config);
            await SaveProvidersToDisk(registry, store);
            return Results.Ok(new { Updated = alias });
        });

        group.MapDelete("/{alias}", async (string alias, ProviderRegistry registry, ProviderConfigStore store) =>
        {
            var removed = registry.Remove(alias);
            if (removed)
            {
                await SaveProvidersToDisk(registry, store);
            }
            return removed ? Results.Ok(new { Deleted = alias }) : Results.NotFound();
        });

        group.MapPost("/test", async (HttpContext httpContext, ProviderRegistry registry, CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var alias = body.RootElement.TryGetProperty("alias", out var aliasEl) ? aliasEl.GetString() ?? "" : "";

            try
            {
                var success = await registry.TestConnectionAsync(alias, ct);
                return Results.Ok(new { Alias = alias, Success = success });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { Alias = alias, Success = false, Error = ex.Message });
            }
        });

        group.MapGet("/{alias}/models", async (string alias, ProviderRegistry registry, ILogger<ProviderRegistry> logger, CancellationToken ct) =>
        {
            var config = registry.GetConfig(alias);
            if (config is null)
            {
                return Results.NotFound(new { Error = $"Provider '{alias}' not found" });
            }

            try
            {
                // For Ollama, fetch models from the API directly
                if (config.Type == ProviderType.Ollama)
                {
                    var baseUrl = config.BaseUrl ?? "http://localhost:11434";
                    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                    var response = await httpClient.GetAsync($"{baseUrl}/api/tags", ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        return Results.Ok(new { Models = Array.Empty<string>(), Error = $"Ollama returned {response.StatusCode}" });
                    }

                    var json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    var models = doc.RootElement.GetProperty("models").EnumerateArray()
                        .Select(m => m.GetProperty("name").GetString())
                        .ToList();

                    return Results.Ok(new { Models = models });
                }

                // For OpenAI-compatible providers, try the /v1/models endpoint
                if (config.Type is ProviderType.OpenAI or ProviderType.OpenRouter or ProviderType.Custom or ProviderType.AzureOpenAI)
                {
                    var baseUrl = config.Type == ProviderType.OpenRouter
                        ? "https://openrouter.ai/api"
                        : config.BaseUrl ?? "https://api.openai.com";

                    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                    httpClient.DefaultRequestHeaders.Authorization = new("Bearer", config.ApiKey);

                    var response = await httpClient.GetAsync($"{baseUrl.TrimEnd('/')}/v1/models", ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        return Results.Ok(new { Models = Array.Empty<string>(), Error = $"API returned {response.StatusCode}" });
                    }

                    var json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    var models = doc.RootElement.GetProperty("data").EnumerateArray()
                        .Select(m => m.GetProperty("id").GetString())
                        .ToList();

                    return Results.Ok(new { Models = models });
                }

                // For Anthropic, return a static list (no list models API)
                if (config.Type == ProviderType.Anthropic)
                {
                    var models = new[]
                    {
                        "claude-opus-4-20250514",
                        "claude-sonnet-4-20250514",
                        "claude-haiku-4-20250414",
                    };
                    return Results.Ok(new { Models = models });
                }

                return Results.Ok(new { Models = Array.Empty<string>() });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch models for provider {Alias}", alias);
                return Results.Ok(new { Models = Array.Empty<object>(), Error = ex.Message });
            }
        });
    }

    private static async Task SaveProvidersToDisk(ProviderRegistry registry, ProviderConfigStore store)
    {
        var configs = registry.GetAllConfigs();
        var dtos = configs.Select(c => new ProviderConfigDto
        {
            Alias = c.Alias,
            Type = c.Type.ToString(),
            ApiKey = c.ApiKey,
            BaseUrl = c.BaseUrl,
            DefaultModel = c.DefaultModel,
        }).ToList();
        await store.SaveAsync(dtos);
    }
}
