using System.Runtime.CompilerServices;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Providers.Registry;

namespace QuillForge.Web.Services;

/// <summary>
/// An ICompletionService that lazily resolves through the ProviderRegistry.
/// Uses the first registered provider as the default. This allows the app to
/// start without any providers configured — requests will fail with a clear
/// message until one is registered.
/// </summary>
public sealed class DefaultCompletionService : ICompletionService
{
    private readonly ProviderRegistry _registry;
    private readonly ILogger<DefaultCompletionService> _logger;

    public DefaultCompletionService(ProviderRegistry registry, ILogger<DefaultCompletionService> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    public async Task<CompletionResponse> CompleteAsync(CompletionRequest request, CancellationToken ct = default)
    {
        var service = ResolveService(request.Model);
        return await service.CompleteAsync(request, ct);
    }

    public IAsyncEnumerable<StreamEvent> StreamAsync(CompletionRequest request, CancellationToken ct = default)
    {
        var service = ResolveService(request.Model);
        return service.StreamAsync(request, ct);
    }

    private ICompletionService ResolveService(string model)
    {
        // If model is a registered provider alias, use that directly
        if (_registry.GetConfig(model) is not null)
        {
            _logger.LogDebug("Resolving completion service for provider alias: {Model}", model);
            return _registry.GetCompletionService(model);
        }

        // Otherwise, use the first registered provider
        var providers = _registry.ListProviders();
        if (providers.Count == 0)
        {
            throw new InvalidOperationException(
                "No LLM providers are configured. Add a provider via POST /api/providers first.");
        }

        var defaultAlias = providers[0].Alias;
        _logger.LogDebug("Resolving completion service via default provider: {Alias}", defaultAlias);
        return _registry.GetCompletionService(defaultAlias);
    }
}
