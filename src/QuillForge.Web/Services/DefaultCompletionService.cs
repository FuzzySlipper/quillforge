using System.Text.Json;
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
        var (service, effectiveRequest) = ResolveService(request);
        return await service.CompleteAsync(effectiveRequest, ct);
    }

    public IAsyncEnumerable<StreamEvent> StreamAsync(CompletionRequest request, CancellationToken ct = default)
    {
        var (service, effectiveRequest) = ResolveService(request);
        return service.StreamAsync(effectiveRequest, ct);
    }

    private (ICompletionService Service, CompletionRequest Request) ResolveService(CompletionRequest request)
    {
        var model = request.Model;

        // If model is a registered provider alias, use that provider
        // and normalize the model to "default" so the provider uses its configured model
        // instead of passing the alias as a literal model name to the API.
        if (_registry.GetConfig(model) is { } providerConfig)
        {
            _logger.LogDebug("Resolving completion service for provider alias: {Model}", model);
            var service = _registry.GetCompletionService(model);
            return (service, ApplyProviderOptions(request with { Model = "default" }, providerConfig?.Options));
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
        var defaultConfig = _registry.GetConfig(defaultAlias);
        return (_registry.GetCompletionService(defaultAlias), ApplyProviderOptions(request, defaultConfig?.Options));
    }

    private static CompletionRequest ApplyProviderOptions(CompletionRequest request, ProviderOptions? options)
    {
        if (options is null)
        {
            return request;
        }

        return request with
        {
            Temperature = request.Temperature ?? options.Temperature,
            TopP = request.TopP ?? options.TopP,
            TopK = request.TopK ?? options.TopK,
            FrequencyPenalty = request.FrequencyPenalty ?? options.FrequencyPenalty,
            PresencePenalty = request.PresencePenalty ?? options.PresencePenalty,
            RepetitionPenalty = request.RepetitionPenalty ?? options.RepetitionPenalty,
            MinP = request.MinP ?? options.MinP,
            Seed = request.Seed ?? options.Seed,
            AdditionalOptions = MergeAdditionalOptions(options.Additional, request.AdditionalOptions),
        };
    }

    private static IReadOnlyDictionary<string, JsonElement>? MergeAdditionalOptions(
        IReadOnlyDictionary<string, JsonElement>? providerOptions,
        IReadOnlyDictionary<string, JsonElement>? requestOptions)
    {
        if (providerOptions is null && requestOptions is null)
        {
            return null;
        }

        var merged = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        if (providerOptions is not null)
        {
            foreach (var (key, value) in providerOptions)
            {
                merged[key] = value.Clone();
            }
        }

        if (requestOptions is not null)
        {
            foreach (var (key, value) in requestOptions)
            {
                merged[key] = value.Clone();
            }
        }

        return merged;
    }
}
