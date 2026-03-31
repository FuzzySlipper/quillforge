using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
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
    private readonly Lock _lock = new();

    private readonly Dictionary<string, ProviderConfig> _configs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IChatClient> _clients = new(StringComparer.OrdinalIgnoreCase);

    public ProviderRegistry(
        ProviderFactory factory,
        ILogger<ProviderRegistry> logger,
        ILoggerFactory loggerFactory)
    {
        _factory = factory;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public string Category => "providers";

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
                    new HttpClient(),
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
