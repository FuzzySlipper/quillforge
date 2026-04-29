using System.Text.Json;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
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
                        Model = config?.DefaultModel,
                        DefaultModel = config?.DefaultModel,
                        BaseUrl = config?.BaseUrl,
                        ModelsUrl = config?.ModelsUrl ?? DefaultModelsUrl(config),
                        ContextLimit = config?.ContextLimit,
                        RequiresReasoning = config?.RequiresReasoning,
                        RequiresReasoningEffective = config?.RequiresReasoning ?? ProviderFactory.IsReasoningModel(config?.DefaultModel ?? ""),
                        ApiKeySet = !string.IsNullOrEmpty(config?.ApiKey),
                        UsedBy = Array.Empty<string>(),
                        Options = config?.Options is not null ? ToProviderOptionsDictionary(config.Options) : null,
                    };
                });
            return Results.Ok(new { Providers = providers });
        });

        group.MapPost("/", async (HttpContext httpContext, ProviderRegistry registry, ProviderConfigStore store, IAppConfigStore appConfigStore, AppConfig runtimeConfig, ILogger<ProviderRegistry> logger, CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body);
            var root = body.RootElement;

            var alias = root.TryGetProperty("alias", out var aliasEl) ? aliasEl.GetString() ?? "unnamed" : "unnamed";
            var typeStr = root.TryGetProperty("type", out var typeEl) ? typeEl.GetString() ?? "Custom" : "Custom";
            var apiKey = root.TryGetProperty("apiKey", out var keyEl) ? keyEl.GetString() ?? "" : "";
            var baseUrl = root.TryGetProperty("baseUrl", out var urlEl) ? urlEl.GetString() : null;
            var model = ReadOptionalString(root, "model") ?? ReadOptionalString(root, "defaultModel");
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

            var requiresReasoning = root.TryGetProperty("requiresReasoning", out var rrEl)
                ? ReadNullableBool(rrEl)
                : null;

            var config = new ProviderConfig
            {
                Alias = alias,
                Type = providerType,
                ApiKey = apiKey,
                BaseUrl = baseUrl,
                ModelsUrl = modelsUrl,
                DefaultModel = model,
                ContextLimit = contextLimit,
                RequiresReasoning = requiresReasoning,
                Options = options,
            };

            logger.LogInformation(
                "Registering provider: alias={Alias}, type={Type}, baseUrl={BaseUrl}, model={Model}",
                alias, providerType, baseUrl, model);

            registry.Register(config);
            await SaveProvidersToDisk(registry, store);
            await FillBlankAgentModelAssignmentsAsync(config.Alias, appConfigStore, runtimeConfig, ct);
            return Results.Ok(new { Registered = config.Alias });
        });

        group.MapPut("/{alias}", async (string alias, HttpContext httpContext, ProviderRegistry registry, ProviderConfigStore store, IAppConfigStore appConfigStore, AppConfig runtimeConfig, CancellationToken ct) =>
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

            var newRequiresReasoning = root.TryGetProperty("requiresReasoning", out var rrEl3)
                ? ReadNullableBool(rrEl3)
                : existing.RequiresReasoning;

            var model = ReadOptionalString(root, "model") ?? ReadOptionalString(root, "defaultModel") ?? existing.DefaultModel;

            var config = existing with
            {
                ApiKey = !string.IsNullOrEmpty(newApiKey) ? newApiKey : existing.ApiKey,
                BaseUrl = root.TryGetProperty("baseUrl", out var urlEl) ? urlEl.GetString() ?? existing.BaseUrl : existing.BaseUrl,
                DefaultModel = string.IsNullOrWhiteSpace(model) ? existing.DefaultModel : model,
                ModelsUrl = root.TryGetProperty("modelsUrl", out var muEl) ? muEl.GetString() ?? existing.ModelsUrl : existing.ModelsUrl,
                ContextLimit = root.TryGetProperty("contextLimit", out var clEl) && clEl.TryGetInt32(out var clVal) ? clVal : existing.ContextLimit,
                RequiresReasoning = newRequiresReasoning,
                Options = newOptions,
            };

            registry.Register(config);
            await SaveProvidersToDisk(registry, store);
            await FillBlankAgentModelAssignmentsAsync(config.Alias, appConfigStore, runtimeConfig, ct);
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

        group.MapGet("/{alias}/models", async (string alias, ProviderRegistry registry, AppConfig appConfig, ILogger<ProviderRegistry> logger, CancellationToken ct) =>
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
                    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(appConfig.Timeouts.ProviderHttpSeconds) };
                    var response = await httpClient.GetAsync($"{baseUrl}/api/tags", ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        return Results.Ok(new { Models = Array.Empty<string>(), Error = $"Ollama returned {response.StatusCode}" });
                    }

                    var json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    var models = doc.RootElement.TryGetProperty("models", out var modelsArr)
                        ? modelsArr.EnumerateArray()
                            .Select(m => m.TryGetProperty("name", out var n) ? n.GetString() : null)
                            .Where(n => n is not null)
                            .ToList()
                        : [];

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

                    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(appConfig.Timeouts.ProviderHttpSeconds) };
                    httpClient.DefaultRequestHeaders.Authorization = new("Bearer", config.ApiKey);

                    var response = await httpClient.GetAsync(modelsUrl, ct);

                    if (!response.IsSuccessStatusCode)
                    {
                        return Results.Ok(new { Models = Array.Empty<string>(), Error = $"API returned {response.StatusCode}" });
                    }

                    var json = await response.Content.ReadAsStringAsync(ct);
                    using var doc = JsonDocument.Parse(json);
                    var models = doc.RootElement.TryGetProperty("data", out var dataArr)
                        ? dataArr.EnumerateArray()
                            .Select(m => m.TryGetProperty("id", out var id) ? id.GetString() : null)
                            .Where(id => id is not null)
                            .ToList()
                        : [];

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
            RequiresReasoning = c.RequiresReasoning,
            Options = c.Options is not null ? new ProviderOptionsDto
            {
                Temperature = c.Options.Temperature,
                TopP = c.Options.TopP,
                TopK = c.Options.TopK,
                FrequencyPenalty = c.Options.FrequencyPenalty,
                PresencePenalty = c.Options.PresencePenalty,
                RepetitionPenalty = c.Options.RepetitionPenalty,
                MinP = c.Options.MinP,
                Seed = c.Options.Seed,
                Additional = c.Options.Additional is not null
                    ? c.Options.Additional.ToDictionary(pair => pair.Key, pair => pair.Value.Clone())
                    : null,
            } : null,
        }).ToList();
        await store.SaveAsync(dtos);
    }

    private static async Task FillBlankAgentModelAssignmentsAsync(
        string providerAlias,
        IAppConfigStore appConfigStore,
        AppConfig runtimeConfig,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(providerAlias))
        {
            return;
        }

        var updatedConfig = await appConfigStore.UpdateAsync(current => current with
        {
            Models = current.Models with
            {
                Orchestrator = FillIfBlank(current.Models.Orchestrator, providerAlias),
                NarrativeDirector = FillIfBlank(current.Models.NarrativeDirector, providerAlias),
                ProseWriter = FillIfBlank(current.Models.ProseWriter, providerAlias),
                Librarian = FillIfBlank(current.Models.Librarian, providerAlias),
                Canonizer = FillIfBlank(current.Models.Canonizer, providerAlias),
                ForgeWriter = FillIfBlank(current.Models.ForgeWriter, providerAlias),
                ForgePlanner = FillIfBlank(current.Models.ForgePlanner, providerAlias),
                ForgeReviewer = FillIfBlank(current.Models.ForgeReviewer, providerAlias),
                DelegateTechnical = FillIfBlank(current.Models.DelegateTechnical, providerAlias),
                Artifact = FillIfBlank(current.Models.Artifact, providerAlias),
                Research = FillIfBlank(current.Models.Research, providerAlias),
                GameIntentTranslator = FillIfBlank(current.Models.GameIntentTranslator, providerAlias),
            },
        }, ct);

        AppConfigRuntimeSync.CopyFrom(runtimeConfig, updatedConfig);
    }

    private static string FillIfBlank(string value, string providerAlias) =>
        IsBlankAgentModelAssignment(value) ? providerAlias : value;

    private static bool IsBlankAgentModelAssignment(string value) =>
        string.IsNullOrWhiteSpace(value) || string.Equals(value, "default", StringComparison.OrdinalIgnoreCase);

    private static string? ReadOptionalString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out var element) ? element.GetString() : null;

    private static bool? ReadNullableBool(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => null,
        };
    }

    private static ProviderOptions? ParseProviderOptions(JsonElement root)
    {
        if (!root.TryGetProperty("options", out var optEl) || optEl.ValueKind != JsonValueKind.Object)
            return null;

        float? temperature = TryGetSingle(optEl, "temperature");
        float? topP = TryGetSingle(optEl, "topP", "top_p");
        int? topK = TryGetInt(optEl, "topK", "top_k");
        float? frequencyPenalty = TryGetSingle(optEl, "frequencyPenalty", "frequency_penalty");
        float? presencePenalty = TryGetSingle(optEl, "presencePenalty", "presence_penalty");
        float? repetitionPenalty = TryGetSingle(optEl, "repetitionPenalty", "repetition_penalty");
        float? minP = TryGetSingle(optEl, "minP", "min_p");
        int? seed = TryGetInt(optEl, "seed");
        var additional = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in optEl.EnumerateObject())
        {
            if (!IsKnownOptionProperty(property.Name))
            {
                additional[property.Name] = property.Value.Clone();
            }
        }

        // Return null if all values are null (no options provided)
        if (temperature is null
            && topP is null
            && topK is null
            && frequencyPenalty is null
            && presencePenalty is null
            && repetitionPenalty is null
            && minP is null
            && seed is null
            && additional.Count == 0)
        {
            return null;
        }

        return new ProviderOptions
        {
            Temperature = temperature,
            TopP = topP,
            TopK = topK,
            FrequencyPenalty = frequencyPenalty,
            PresencePenalty = presencePenalty,
            RepetitionPenalty = repetitionPenalty,
            MinP = minP,
            Seed = seed,
            Additional = additional.Count > 0 ? additional : null,
        };
    }

    private static Dictionary<string, object?> ToProviderOptionsDictionary(ProviderOptions options)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        if (options.Temperature is not null) values["temperature"] = options.Temperature;
        if (options.TopP is not null) values["topP"] = options.TopP;
        if (options.TopK is not null) values["topK"] = options.TopK;
        if (options.FrequencyPenalty is not null) values["frequencyPenalty"] = options.FrequencyPenalty;
        if (options.PresencePenalty is not null) values["presencePenalty"] = options.PresencePenalty;
        if (options.RepetitionPenalty is not null) values["repetitionPenalty"] = options.RepetitionPenalty;
        if (options.MinP is not null) values["minP"] = options.MinP;
        if (options.Seed is not null) values["seed"] = options.Seed;

        if (options.Additional is not null)
        {
            foreach (var (key, value) in options.Additional)
            {
                values[key] = value.Clone();
            }
        }

        return values;
    }

    private static float? TryGetSingle(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number)
            {
                return property.GetSingle();
            }
        }

        return null;
    }

    private static int? TryGetInt(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number)
            {
                return property.GetInt32();
            }
        }

        return null;
    }

    private static bool IsKnownOptionProperty(string name)
    {
        return name is "temperature"
            or "topP"
            or "top_p"
            or "topK"
            or "top_k"
            or "frequencyPenalty"
            or "frequency_penalty"
            or "presencePenalty"
            or "presence_penalty"
            or "repetitionPenalty"
            or "repetition_penalty"
            or "minP"
            or "min_p"
            or "seed";
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

    /// <summary>
    /// Fetch models from an arbitrary provider URL (used by provider config UI).
    /// </summary>
    public static void MapProviderFetchModelsEndpoint(this WebApplication app)
    {
        app.MapPost("/api/providers/fetch-models", async (
            HttpContext httpContext,
            AppConfig appConfig,
            CancellationToken ct) =>
        {
            var body = await JsonDocument.ParseAsync(httpContext.Request.Body, cancellationToken: ct);
            var root = body.RootElement;

            var url = root.TryGetProperty("baseUrl", out var urlEl) ? urlEl.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(url))
                url = root.TryGetProperty("url", out var urlEl2) ? urlEl2.GetString() ?? "" : "";
            var apiKey = root.TryGetProperty("apiKey", out var keyEl) ? keyEl.GetString() ?? "" : "";

            if (string.IsNullOrEmpty(url))
            {
                return Results.Ok(new { Models = Array.Empty<object>() });
            }

            try
            {
                using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(appConfig.Timeouts.ProviderHttpSeconds) };
                if (!string.IsNullOrEmpty(apiKey))
                {
                    httpClient.DefaultRequestHeaders.Authorization = new("Bearer", apiKey);
                }

                // Try /v1/models first, then /api/tags (Ollama)
                var modelsUrl = url.TrimEnd('/');
                if (!modelsUrl.EndsWith("/v1/models") && !modelsUrl.EndsWith("/api/tags"))
                {
                    modelsUrl += "/v1/models";
                }

                var response = await httpClient.GetAsync(modelsUrl, ct);
                if (!response.IsSuccessStatusCode)
                {
                    // Try Ollama format
                    modelsUrl = url.TrimEnd('/') + "/api/tags";
                    response = await httpClient.GetAsync(modelsUrl, ct);
                }

                if (!response.IsSuccessStatusCode)
                {
                    return Results.Ok(new { Models = Array.Empty<object>(), Error = $"{response.StatusCode}" });
                }

                var json = await response.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                // OpenAI format
                if (doc.RootElement.TryGetProperty("data", out var data))
                {
                    var models = data.EnumerateArray()
                        .Select(m => m.TryGetProperty("id", out var id) ? id.GetString() : null)
                        .Where(id => id is not null)
                        .ToList();
                    return Results.Ok(new { Models = models });
                }

                // Ollama format
                if (doc.RootElement.TryGetProperty("models", out var ollamaModels))
                {
                    var models = ollamaModels.EnumerateArray()
                        .Select(m => m.TryGetProperty("name", out var n) ? n.GetString() : null)
                        .Where(n => n is not null)
                        .ToList();
                    return Results.Ok(new { Models = models });
                }

                return Results.Ok(new { Models = Array.Empty<object>() });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { Models = Array.Empty<object>(), Error = ex.Message });
            }
        });
    }
}
