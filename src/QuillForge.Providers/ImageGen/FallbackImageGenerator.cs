using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;

namespace QuillForge.Providers.ImageGen;

public sealed class FallbackImageGenerator : IImageGenerator
{
    private readonly IReadOnlyList<IImageGenerator> _providers;
    private readonly ILogger<FallbackImageGenerator> _logger;

    public FallbackImageGenerator(IEnumerable<IImageGenerator> providers, ILogger<FallbackImageGenerator> logger)
    {
        _providers = providers.ToList();
        _logger = logger;
    }

    public async Task<ImageResult> GenerateAsync(string prompt, ImageOptions? options = null, CancellationToken ct = default)
    {
        if (_providers.Count == 0)
            throw new InvalidOperationException("No image generation providers configured.");

        Exception? lastError = null;
        foreach (var provider in _providers)
        {
            try
            {
                return await provider.GenerateAsync(prompt, options, ct);
            }
            catch (Exception ex)
            {
                lastError = ex;
                _logger.LogWarning(ex, "Image provider {Provider} failed, trying next", provider.GetType().Name);
            }
        }

        throw new InvalidOperationException("All image generation providers failed.", lastError);
    }
}
