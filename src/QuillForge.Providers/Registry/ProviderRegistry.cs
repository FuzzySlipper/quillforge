using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Providers.Adapters;

namespace QuillForge.Providers.Registry;

/// <summary>
/// Manages multiple configured LLM providers. Resolves aliases to ICompletionService instances.
/// Thread-safe for concurrent access.
/// </summary>
public sealed class ProviderRegistry : IDiagnosticSource
{
    private readonly ProviderFactory _factory;
    private readonly ILogger<ProviderRegistry> _logger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly TimeSpan _completionTimeout;
    private readonly Lock _lock = new();

    private readonly Dictionary<string, ProviderConfig> _configs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IChatClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public ProviderRegistry(
        ProviderFactory factory,
        AppConfig appConfig,
        ILogger<ProviderRegistry> logger,
        ILoggerFactory loggerFactory)
    {
        _factory = factory;
        _logger = logger;
        _loggerFactory = loggerFactory;
        _completionTimeout = TimeSpan.FromSeconds(appConfig.Timeouts.CompletionTimeoutSeconds);
    }

    public string Category => "providers";

    /// <summary>
    /// Resolves a configured provider alias to a registered provider alias.
    /// Exact aliases win; otherwise common provider type names such as
    /// "anthropic"/"claude" and exact default model names are accepted when
    /// they identify exactly one registered provider.
    /// </summary>
    public ProviderAliasResolution ResolveProviderAlias(string requestedAlias)
    {
        if (string.IsNullOrWhiteSpace(requestedAlias))
        {
            return ProviderAliasResolution.Failed(
                requestedAlias,
                "Provider alias is required.");
        }

        lock (_lock)
        {
            if (_configs.Count == 0)
            {
                return ProviderAliasResolution.Failed(
                    requestedAlias,
                    "No LLM providers are configured. Add a provider in Provider settings or via POST /api/providers before running delegated agents.");
            }

            if (_configs.TryGetValue(requestedAlias, out var exactConfig))
            {
                return ProviderAliasResolution.Resolved(requestedAlias, exactConfig.Alias);
            }

            if (IsDefaultProviderAlias(requestedAlias))
            {
                var defaultConfig = _configs.Values.First();
                return ProviderAliasResolution.Resolved(requestedAlias, defaultConfig.Alias);
            }

            var typeMatches = FindProviderTypeAliasMatches(requestedAlias);
            if (typeMatches.Count == 1)
            {
                return ProviderAliasResolution.Resolved(requestedAlias, typeMatches[0].Alias);
            }

            if (typeMatches.Count > 1)
            {
                return ProviderAliasResolution.Failed(
                    requestedAlias,
                    $"Provider alias '{requestedAlias}' names provider type '{typeMatches[0].Type}', but multiple registered providers have that type: {FormatProviderAliases(typeMatches)}. Set the council member provider to one of these aliases.");
            }

            var modelMatches = FindDefaultModelMatches(requestedAlias);
            if (modelMatches.Count == 1)
            {
                return ProviderAliasResolution.Resolved(requestedAlias, modelMatches[0].Alias);
            }

            if (modelMatches.Count > 1)
            {
                return ProviderAliasResolution.Failed(
                    requestedAlias,
                    $"Provider alias '{requestedAlias}' matches the default model for multiple registered providers: {FormatProviderAliases(modelMatches)}. Set the council member provider to one of these aliases.");
            }

            return ProviderAliasResolution.Failed(
                requestedAlias,
                $"No provider registered with alias '{requestedAlias}'. Registered aliases: {FormatProviderAliases(_configs.Values)}. If this value is a provider type or model name, configure exactly one matching provider or set the council member provider to a registered alias.");
        }
    }

    /// <summary>
    /// Registers a provider configuration. Creates the client lazily on first use.
    /// </summary>
    public void Register(ProviderConfig config)
    {
        lock (_lock)
        {
            _configs[config.Alias] = config;
            _clients.Remove(config.Alias); // Force recreation on next access
            _logger.LogInformation("Registered provider {Alias} (type={Type})", config.Alias, config.Type);
        }
    }

    /// <summary>
    /// Removes a provider.
    /// </summary>
    public bool Remove(string alias)
    {
        lock (_lock)
        {
            var removed = _configs.Remove(alias);
            if (_clients.Remove(alias, out var client))
            {
                (client as IDisposable)?.Dispose();
            }
            if (removed) _logger.LogInformation("Removed provider {Alias}", alias);
            return removed;
        }
    }

    /// <summary>
    /// Gets an ICompletionService for a provider alias.
    /// Uses ReasoningCompletionService for reasoning-enabled models that require
    /// provider-specific field preservation during tool loop round-trips.
    /// All services are wrapped with RetryCompletionService for transient error handling.
    /// </summary>
    public ICompletionService GetCompletionService(string alias)
    {
        ICompletionService inner;

        lock (_lock)
        {
            if (!_configs.TryGetValue(alias, out var config))
            {
                throw new KeyNotFoundException($"No provider registered with alias '{alias}'.");
            }

            if (config.RequiresReasoning ?? ProviderFactory.IsReasoningModel(config.DefaultModel ?? ""))
            {
                _logger.LogDebug("Using ReasoningCompletionService for {Alias} (model={Model})", alias, config.DefaultModel);
                inner = new ReasoningCompletionService(
                    new HttpClient { Timeout = _completionTimeout },
                    config.BaseUrl ?? "https://api.openai.com/v1",
                    config.ApiKey,
                    config.DefaultModel ?? "default",
                    _loggerFactory.CreateLogger<ReasoningCompletionService>());

                return new RetryCompletionService(inner,
                    _loggerFactory.CreateLogger<RetryCompletionService>());
            }
        }

        var client = GetOrCreateClient(alias);
        inner = new ChatClientCompletionService(
            client,
            _loggerFactory.CreateLogger<ChatClientCompletionService>());

        return new RetryCompletionService(inner,
            _loggerFactory.CreateLogger<RetryCompletionService>());
    }

    /// <summary>
    /// Gets an IChatClient for a provider alias. Used when direct chat client access is needed.
    /// </summary>
    public IChatClient GetChatClient(string alias)
    {
        return GetOrCreateClient(alias);
    }

    /// <summary>
    /// Lists all registered provider aliases and their types.
    /// </summary>
    public IReadOnlyList<(string Alias, ProviderType Type)> ListProviders()
    {
        lock (_lock)
        {
            return _configs.Values.Select(c => (c.Alias, c.Type)).ToList();
        }
    }

    /// <summary>
    /// Returns a snapshot of all registered provider configurations.
    /// </summary>
    public IReadOnlyList<ProviderConfig> GetAllConfigs()
    {
        lock (_lock)
        {
            return _configs.Values.ToList();
        }
    }

    /// <summary>
    /// Gets the config for a provider alias.
    /// </summary>
    public ProviderConfig? GetConfig(string alias)
    {
        lock (_lock)
        {
            return _configs.GetValueOrDefault(alias);
        }
    }

    /// <summary>
    /// Tests a provider connection by sending a minimal completion request.
    /// </summary>
    public async Task<bool> TestConnectionAsync(string alias, CancellationToken ct = default)
    {
        try
        {
            var client = GetOrCreateClient(alias);
            var response = await client.GetResponseAsync(
                [new ChatMessage(ChatRole.User, "Hi")],
                new ChatOptions { MaxOutputTokens = 10 },
                ct);
            _logger.LogInformation("Provider {Alias} connection test: OK", alias);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Provider {Alias} connection test: FAILED", alias);
            return false;
        }
    }

    public Task<IReadOnlyDictionary<string, object>> GetDiagnosticsAsync(CancellationToken ct = default)
    {
        lock (_lock)
        {
            var diag = new Dictionary<string, object>
            {
                ["registered_count"] = _configs.Count,
                ["active_clients"] = _clients.Count,
                ["providers"] = _configs.Values.Select(c => new
                {
                    c.Alias,
                    Type = c.Type.ToString(),
                    HasClient = _clients.ContainsKey(c.Alias),
                    c.DefaultModel,
                }).ToList(),
            };
            return Task.FromResult<IReadOnlyDictionary<string, object>>(diag);
        }
    }

    private List<ProviderConfig> FindProviderTypeAliasMatches(string requestedAlias)
    {
        var matches = new List<ProviderConfig>();
        foreach (var config in _configs.Values)
        {
            if (IsProviderTypeAlias(requestedAlias, config.Type))
            {
                matches.Add(config);
            }
        }

        return matches;
    }

    private List<ProviderConfig> FindDefaultModelMatches(string requestedAlias)
    {
        var matches = new List<ProviderConfig>();
        foreach (var config in _configs.Values)
        {
            if (string.Equals(config.DefaultModel, requestedAlias, StringComparison.OrdinalIgnoreCase))
            {
                matches.Add(config);
            }
        }

        return matches;
    }

    private static bool IsDefaultProviderAlias(string requestedAlias)
        => string.Equals(requestedAlias, "default", StringComparison.OrdinalIgnoreCase);

    private static bool IsProviderTypeAlias(string requestedAlias, ProviderType type)
    {
        var normalized = NormalizeProviderAlias(requestedAlias);
        return type switch
        {
            ProviderType.Anthropic => normalized is "anthropic" or "claude",
            ProviderType.OpenAI => normalized is "openai" or "gpt",
            ProviderType.AzureOpenAI => normalized is "azure" or "azureopenai",
            ProviderType.Ollama => normalized is "ollama" or "local",
            ProviderType.OpenRouter => normalized is "openrouter",
            ProviderType.Custom => normalized is "custom" or "openaicompatible",
            _ => false,
        };
    }

    private static string NormalizeProviderAlias(string value)
        => value.Trim()
            .Replace("-", "", StringComparison.Ordinal)
            .Replace("_", "", StringComparison.Ordinal)
            .Replace(" ", "", StringComparison.Ordinal)
            .ToLowerInvariant();

    private static string FormatProviderAliases(IEnumerable<ProviderConfig> configs)
    {
        var aliases = new List<string>();
        foreach (var config in configs)
        {
            aliases.Add(config.Alias);
        }

        return aliases.Count > 0 ? string.Join(", ", aliases) : "(none)";
    }

    private IChatClient GetOrCreateClient(string alias)
    {
        lock (_lock)
        {
            if (_clients.TryGetValue(alias, out var existing))
            {
                return existing;
            }

            if (!_configs.TryGetValue(alias, out var config))
            {
                throw new KeyNotFoundException($"No provider registered with alias '{alias}'.");
            }

            var client = _factory.CreateClient(config);
            _clients[alias] = client;
            _logger.LogDebug("Created chat client for provider {Alias}", alias);
            return client;
        }
    }
}
