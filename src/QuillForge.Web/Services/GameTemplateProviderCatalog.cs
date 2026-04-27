using QuillForge.Core.Services;
using QuillForge.Providers.Registry;

namespace QuillForge.Web.Services;

public sealed class GameTemplateProviderCatalog : IGameTemplateProviderCatalog
{
    private readonly ProviderRegistry _providerRegistry;

    public GameTemplateProviderCatalog(ProviderRegistry providerRegistry)
    {
        _providerRegistry = providerRegistry;
    }

    public Task<IReadOnlySet<string>> ListProviderAliasesAsync(CancellationToken ct = default)
    {
        var aliases = _providerRegistry.GetAllConfigs()
            .Select(config => config.Alias)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return Task.FromResult<IReadOnlySet<string>>(aliases);
    }
}
