using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Web.Contracts;

namespace QuillForge.Web.Endpoints;

public static class AppSettingsEndpoints
{
    private static readonly string[] SupportedWebSearchProviders =
    [
        "searxng",
        "tavily",
        "brave",
        "google",
        "zai",
    ];

    public static void MapAppSettingsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/app-settings");

        group.MapGet("/", (AppConfig config) =>
        {
            return Results.Ok(new AppSettingsResponse
            {
                WebSearch = ToWebSearchResponse(config.WebSearch),
            });
        });

        group.MapPut("/web-search", async (
            WebSearchSettingsUpdateRequest request,
            AppConfig runtimeConfig,
            IAppConfigStore configStore,
            ILogger<AppConfig> logger,
            CancellationToken ct) =>
        {
            if (request.Provider is not null && NormalizeProvider(request.Provider) is null)
            {
                return Results.BadRequest(new { Error = $"Unsupported web_search provider '{request.Provider}'." });
            }

            if (request.MaxResults is < 1 or > 100)
            {
                return Results.BadRequest(new { Error = "web_search.max_results must be between 1 and 100." });
            }

            var normalizedEndpoint = NormalizeOptionalString(request.ZaiMcpEndpoint);
            if (normalizedEndpoint is not null && !Uri.TryCreate(normalizedEndpoint, UriKind.Absolute, out _))
            {
                return Results.BadRequest(new { Error = "web_search.zai_mcp_endpoint must be an absolute URI." });
            }

            var normalizedProvider = request.Provider is null ? null : NormalizeProvider(request.Provider);

            var updatedConfig = await configStore.UpdateAsync(current => current with
            {
                WebSearch = current.WebSearch with
                {
                    Enabled = request.Enabled ?? current.WebSearch.Enabled,
                    Provider = normalizedProvider ?? current.WebSearch.Provider,
                    SearxngUrl = UpdateOptionalString(current.WebSearch.SearxngUrl, request.SearxngUrl),
                    TavilyApiKey = UpdateSecret(current.WebSearch.TavilyApiKey, request.TavilyApiKey, request.ClearTavilyApiKey),
                    BraveApiKey = UpdateSecret(current.WebSearch.BraveApiKey, request.BraveApiKey, request.ClearBraveApiKey),
                    GoogleApiKey = UpdateSecret(current.WebSearch.GoogleApiKey, request.GoogleApiKey, request.ClearGoogleApiKey),
                    GoogleCxId = UpdateOptionalString(current.WebSearch.GoogleCxId, request.GoogleCxId),
                    ZaiApiKey = UpdateSecret(current.WebSearch.ZaiApiKey, request.ZaiApiKey, request.ClearZaiApiKey),
                    ZaiMcpEndpoint = UpdateOptionalString(current.WebSearch.ZaiMcpEndpoint, request.ZaiMcpEndpoint),
                    ZaiMcpToolName = UpdateOptionalString(current.WebSearch.ZaiMcpToolName, request.ZaiMcpToolName),
                    MaxResults = request.MaxResults ?? current.WebSearch.MaxResults,
                }
            }, ct);
            AppConfigRuntimeSync.CopyFrom(runtimeConfig, updatedConfig);

            logger.LogInformation(
                "Web search settings updated: enabled={Enabled}, provider={Provider}, max_results={MaxResults}",
                updatedConfig.WebSearch.Enabled,
                updatedConfig.WebSearch.Provider,
                updatedConfig.WebSearch.MaxResults);

            return Results.Ok(new AppSettingsResponse
            {
                WebSearch = ToWebSearchResponse(updatedConfig.WebSearch),
            });
        });
    }

    private static WebSearchSettingsResponse ToWebSearchResponse(WebSearchConfig config)
    {
        return new WebSearchSettingsResponse
        {
            Enabled = config.Enabled,
            Provider = NormalizeProvider(config.Provider) ?? config.Provider,
            SearxngUrl = config.SearxngUrl,
            TavilyApiKeySet = !string.IsNullOrWhiteSpace(config.TavilyApiKey),
            BraveApiKeySet = !string.IsNullOrWhiteSpace(config.BraveApiKey),
            GoogleApiKeySet = !string.IsNullOrWhiteSpace(config.GoogleApiKey),
            GoogleCxId = config.GoogleCxId,
            ZaiApiKeySet = !string.IsNullOrWhiteSpace(config.ZaiApiKey),
            ZaiMcpEndpoint = config.ZaiMcpEndpoint,
            ZaiMcpToolName = config.ZaiMcpToolName,
            MaxResults = config.MaxResults,
            SupportedProviders = SupportedWebSearchProviders,
        };
    }

    private static string? NormalizeProvider(string provider)
    {
        var normalized = provider.Trim().ToLowerInvariant();
        return normalized switch
        {
            "searxng" or "searx" => "searxng",
            "tavily" => "tavily",
            "brave" => "brave",
            "google" => "google",
            "zai" or "z_ai" or "z.ai" or "z-ai" => "zai",
            _ => null,
        };
    }

    private static string? UpdateOptionalString(string? current, string? incoming)
    {
        if (incoming is null)
        {
            return current;
        }

        return NormalizeOptionalString(incoming);
    }

    private static string? NormalizeOptionalString(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        return trimmed.Length == 0 ? null : trimmed;
    }

    private static string? UpdateSecret(string? current, string? incoming, bool clear)
    {
        if (clear)
        {
            return null;
        }

        var trimmed = NormalizeOptionalString(incoming);
        return trimmed ?? current;
    }
}
