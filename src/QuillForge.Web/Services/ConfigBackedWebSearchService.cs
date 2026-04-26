using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Providers.WebSearch;

namespace QuillForge.Web.Services;

/// <summary>
/// Resolves the currently configured web-search provider at call time so App
/// Settings changes take effect without editing config.yaml or rebuilding the
/// service provider.
/// </summary>
public sealed class ConfigBackedWebSearchService : IWebSearchService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly AppConfig _appConfig;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ConfigBackedWebSearchService> _logger;

    public ConfigBackedWebSearchService(
        IHttpClientFactory httpClientFactory,
        AppConfig appConfig,
        ILoggerFactory loggerFactory,
        ILogger<ConfigBackedWebSearchService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _appConfig = appConfig;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    public Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        var cfg = _appConfig.WebSearch;
        if (!cfg.Enabled)
        {
            throw new WebSearchProviderException(
                "configured",
                "Web search is disabled in App Settings. Enable web_search before calling the web_search tool.",
                canRetrySameRequest: false);
        }

        var provider = CreateProvider(cfg);
        return provider.SearchAsync(query, ct);
    }

    private IWebSearchService CreateProvider(WebSearchConfig cfg)
    {
        var provider = cfg.Provider.Trim().ToLowerInvariant();
        _logger.LogDebug("Resolving web search provider {Provider}", provider);

        return provider switch
        {
            "tavily" => new TavilySearchProvider(
                _httpClientFactory.CreateClient("WebSearch"),
                Required(cfg.TavilyApiKey, "tavily", "TavilyApiKey"),
                cfg.MaxResults,
                _loggerFactory.CreateLogger<TavilySearchProvider>()),

            "brave" => new BraveSearchProvider(
                _httpClientFactory.CreateClient("WebSearch"),
                Required(cfg.BraveApiKey, "brave", "BraveApiKey"),
                cfg.MaxResults,
                _loggerFactory.CreateLogger<BraveSearchProvider>()),

            "google" => new GoogleSearchProvider(
                _httpClientFactory.CreateClient("WebSearch"),
                Required(cfg.GoogleApiKey, "google", "GoogleApiKey"),
                Required(cfg.GoogleCxId, "google", "GoogleCxId"),
                cfg.MaxResults,
                _loggerFactory.CreateLogger<GoogleSearchProvider>()),

            "zai" or "z_ai" or "z.ai" or "z-ai" => new ZaiSearchProvider(
                _httpClientFactory.CreateClient("WebSearch"),
                Required(cfg.ZaiApiKey, "zai", "ZaiApiKey"),
                cfg.ZaiMcpEndpoint,
                cfg.ZaiMcpToolName,
                cfg.MaxResults,
                _loggerFactory.CreateLogger<ZaiSearchProvider>()),

            "searxng" or "searx" => new SearxngSearchProvider(
                _httpClientFactory.CreateClient("WebSearch"),
                Required(cfg.SearxngUrl, "searxng", "SearxngUrl"),
                cfg.MaxResults,
                _loggerFactory.CreateLogger<SearxngSearchProvider>()),

            _ => throw new WebSearchProviderException(
                provider,
                $"Unsupported web_search provider '{cfg.Provider}'. Choose searxng, tavily, brave, google, or zai in App Settings.",
                canRetrySameRequest: false),
        };
    }

    private static string Required(string? value, string provider, string configField)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new WebSearchProviderException(
            provider,
            $"WebSearch provider '{provider}' requires {configField}. Update App Settings before calling web_search.",
            canRetrySameRequest: false);
    }
}
