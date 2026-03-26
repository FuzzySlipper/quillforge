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
                .Select(p =>
                {
                    var config = registry.GetConfig(p.Alias);
                    return new
                    {
                        alias = p.Alias,
                        name = p.Alias,
                        type = p.Type.ToString(),
                        defaultModel = config?.DefaultModel,
                        baseUrl = config?.BaseUrl,
                        used_by = Array.Empty<string>(),
                    };
                });
            return Results.Ok(new { providers });
        });

        group.MapPost("/", async (HttpContext httpContext, ProviderRegistry registry, ILogger<ProviderRegistry> logger) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body);
            var root = body.RootElement;

            // Accept both Python-style and C#-style field names
            var alias = GetString(root, "alias", "name") ?? "unnamed";
            var typeStr = GetString(root, "type", "provider_type") ?? "Custom";
            var apiKey = GetString(root, "apiKey", "api_key", "key") ?? "";
            var baseUrl = GetString(root, "baseUrl", "base_url", "url");
            var defaultModel = GetString(root, "defaultModel", "default_model", "model");

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
            return Results.Ok(new { registered = config.Alias });
        });

        group.MapPut("/{alias}", async (string alias, HttpContext httpContext, ProviderRegistry registry) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body);
            var root = body.RootElement;

            var existing = registry.GetConfig(alias);
            if (existing is null)
            {
                return Results.NotFound(new { error = $"Provider '{alias}' not found" });
            }

            var config = existing with
            {
                ApiKey = GetString(root, "apiKey", "api_key", "key") ?? existing.ApiKey,
                BaseUrl = GetString(root, "baseUrl", "base_url") ?? existing.BaseUrl,
                DefaultModel = GetString(root, "defaultModel", "default_model", "model") ?? existing.DefaultModel,
            };

            registry.Register(config);
            return Results.Ok(new { updated = alias });
        });

        group.MapDelete("/{alias}", (string alias, ProviderRegistry registry) =>
        {
            var removed = registry.Remove(alias);
            return removed ? Results.Ok(new { deleted = alias }) : Results.NotFound();
        });

        group.MapPost("/test", async (HttpContext httpContext, ProviderRegistry registry, CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var alias = GetString(body.RootElement, "alias", "name") ?? "";

            try
            {
                var success = await registry.TestConnectionAsync(alias, ct);
                return Results.Ok(new { alias, success });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { alias, success = false, error = ex.Message });
            }
        });

        group.MapGet("/{alias}/models", async (string alias, ProviderRegistry registry, ILogger<ProviderRegistry> logger, CancellationToken ct) =>
        {
            var config = registry.GetConfig(alias);
            if (config is null)
            {
                return Results.NotFound(new { error = $"Provider '{alias}' not found" });
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
                        return Results.Ok(new { models = Array.Empty<object>(), error = $"Ollama returned {response.StatusCode}" });
                    }

                    var json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    var models = doc.RootElement.GetProperty("models").EnumerateArray()
                        .Select(m => new
                        {
                            id = m.GetProperty("name").GetString(),
                            name = m.GetProperty("name").GetString(),
                        })
                        .ToList();

                    return Results.Ok(new { models });
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
                        return Results.Ok(new { models = Array.Empty<object>(), error = $"API returned {response.StatusCode}" });
                    }

                    var json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    var models = doc.RootElement.GetProperty("data").EnumerateArray()
                        .Select(m => new
                        {
                            id = m.GetProperty("id").GetString(),
                            name = m.GetProperty("id").GetString(),
                        })
                        .ToList();

                    return Results.Ok(new { models });
                }

                // For Anthropic, return a static list (no list models API)
                if (config.Type == ProviderType.Anthropic)
                {
                    var models = new[]
                    {
                        new { id = "claude-opus-4-20250514", name = "Claude Opus 4" },
                        new { id = "claude-sonnet-4-20250514", name = "Claude Sonnet 4" },
                        new { id = "claude-haiku-4-20250414", name = "Claude Haiku 4" },
                    };
                    return Results.Ok(new { models });
                }

                return Results.Ok(new { models = Array.Empty<object>() });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to fetch models for provider {Alias}", alias);
                return Results.Ok(new { models = Array.Empty<object>(), error = ex.Message });
            }
        });
    }

    /// <summary>
    /// Tries multiple property names and returns the first match found.
    /// </summary>
    private static string? GetString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String)
            {
                return el.GetString();
            }
        }
        return null;
    }
}
