using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;

namespace QuillForge.Providers.Tts;

public sealed class FallbackTtsGenerator : ITtsGenerator
{
    private readonly IReadOnlyList<ITtsGenerator> _providers;
    private readonly ILogger<FallbackTtsGenerator> _logger;

    public FallbackTtsGenerator(IEnumerable<ITtsGenerator> providers, ILogger<FallbackTtsGenerator> logger)
    {
        _providers = providers.ToList();
        _logger = logger;
    }

    public IReadOnlyList<ITtsGenerator> Providers => _providers;

    public async Task<TtsResult> GenerateAsync(string text, TtsOptions? options = null, CancellationToken ct = default)
    {
        if (_providers.Count == 0)
            throw new InvalidOperationException("No TTS providers configured.");

        Exception? lastError = null;
        foreach (var provider in _providers)
        {
            try
            {
                return await provider.GenerateAsync(text, options, ct);
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogWarning(ex, "TTS provider {Provider} failed, trying next", provider.GetType().Name);
            }
        }

        throw new InvalidOperationException("All TTS providers failed.", lastError);
    }
}
