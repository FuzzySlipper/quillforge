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
                        ModelsUrl = config?.ModelsUrl ?? DefaultModelsUrl(config),
                        ContextLimit = config?.ContextLimit,
                        ApiKeySet = !string.IsNullOrEmpty(config?.ApiKey),
                        UsedBy = Array.Empty<string>(),
                        Options = config?.Options is not null ? new
                        {
                            config.Options.Temperature,
                            config.Options.TopP,
                            config.Options.TopK,
                            config.Options.FrequencyPenalty,
                            config.Options.PresencePenalty,
                        } : null,
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
            var modelsUrl = root.TryGetProperty("modelsUrl", out var muEl) ? muEl.GetString() : null;
            var contextLimit = root.TryGetProperty("contextLimit", out var clEl) && clEl.TryGetInt32(out var clVal) ? clVal : (int?)null;

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

            var options = ParseProviderOptions(root);

            var config = new ProviderConfig
            {
                Alias = alias,
                Type = providerType,
                ApiKey = apiKey,
                BaseUrl = baseUrl,
                ModelsUrl = modelsUrl,
                DefaultModel = defaultModel,
                ContextLimit = contextLimit,
                Options = options,
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

            var newOptions = root.TryGetProperty("options", out _) ? ParseProviderOptions(root) : existing.Options;

            var config = existing with
            {
                ApiKey = !string.IsNullOrEmpty(newApiKey) ? newApiKey : existing.ApiKey,
                BaseUrl = root.TryGetProperty("baseUrl", out var urlEl) ? urlEl.GetString() ?? existing.BaseUrl : existing.BaseUrl,
                DefaultModel = root.TryGetProperty("defaultModel", out var modelEl) ? modelEl.GetString() ?? existing.DefaultModel : existing.DefaultModel,
                ModelsUrl = root.TryGetProperty("modelsUrl", out var muEl) ? muEl.GetString() ?? existing.ModelsUrl : existing.ModelsUrl,
                ContextLimit = root.TryGetProperty("contextLimit", out var clEl) && clEl.TryGetInt32(out var clVal) ? clVal : existing.ContextLimit,
                Options = newOptions,
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
                    // Use explicit modelsUrl if stored, otherwise derive from baseUrl
                    var modelsUrl = config.ModelsUrl;
                    if (string.IsNullOrEmpty(modelsUrl))
                    {
                        var baseUrl = config.Type == ProviderType.OpenRouter
                            ? "https://openrouter.ai/api"
                            : config.BaseUrl ?? "https://api.openai.com";

                        modelsUrl = baseUrl.TrimEnd('/');
                        // Avoid doubling /v1 when baseUrl already ends with it
                        if (!modelsUrl.EndsWith("/v1/models", StringComparison.OrdinalIgnoreCase))
                        {
                            modelsUrl = modelsUrl.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
                                ? modelsUrl + "/models"
                                : modelsUrl + "/v1/models";
                        }
                    }

                    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
                    httpClient.DefaultRequestHeaders.Authorization = new("Bearer", config.ApiKey);

                    var response = await httpClient.GetAsync(modelsUrl, ct);

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
            ModelsUrl = c.ModelsUrl,
            DefaultModel = c.DefaultModel,
            ContextLimit = c.ContextLimit,
            Options = c.Options is not null ? new ProviderOptionsDto
            {
                Temperature = c.Options.Temperature,
                TopP = c.Options.TopP,
                TopK = c.Options.TopK,
                FrequencyPenalty = c.Options.FrequencyPenalty,
                PresencePenalty = c.Options.PresencePenalty,
            } : null,
        }).ToList();
        await store.SaveAsync(dtos);
    }

    private static ProviderOptions? ParseProviderOptions(JsonElement root)
    {
        if (!root.TryGetProperty("options", out var optEl) || optEl.ValueKind != JsonValueKind.Object)
            return null;

        float? temperature = optEl.TryGetProperty("temperature", out var tEl) && tEl.ValueKind == JsonValueKind.Number ? tEl.GetSingle() : null;
        float? topP = optEl.TryGetProperty("topP", out var tpEl) && tpEl.ValueKind == JsonValueKind.Number ? tpEl.GetSingle() : null;
        int? topK = optEl.TryGetProperty("topK", out var tkEl) && tkEl.TryGetInt32(out var tkVal) ? tkVal : null;
        float? frequencyPenalty = optEl.TryGetProperty("frequencyPenalty", out var fpEl) && fpEl.ValueKind == JsonValueKind.Number ? fpEl.GetSingle() : null;
        float? presencePenalty = optEl.TryGetProperty("presencePenalty", out var ppEl) && ppEl.ValueKind == JsonValueKind.Number ? ppEl.GetSingle() : null;

        // Return null if all values are null (no options provided)
        if (temperature is null && topP is null && topK is null && frequencyPenalty is null && presencePenalty is null)
            return null;

        return new ProviderOptions
        {
            Temperature = temperature,
            TopP = topP,
            TopK = topK,
            FrequencyPenalty = frequencyPenalty,
            PresencePenalty = presencePenalty,
        };
    }

    private static string? DefaultModelsUrl(ProviderConfig? config)
    {
        if (config is null) return null;
        return config.Type switch
        {
            ProviderType.Ollama => (config.BaseUrl ?? "http://localhost:11434").TrimEnd('/') + "/api/tags",
            ProviderType.OpenAI => "https://api.openai.com/v1/models",
            ProviderType.OpenRouter => "https://openrouter.ai/api/v1/models",
            ProviderType.Anthropic => null, // Anthropic has no list models API
            ProviderType.AzureOpenAI => config.BaseUrl is not null ? config.BaseUrl.TrimEnd('/') + "/v1/models" : null,
            ProviderType.Custom => config.BaseUrl is not null ? config.BaseUrl.TrimEnd('/') + "/v1/models" : null,
            _ => null,
        };
    }
}
