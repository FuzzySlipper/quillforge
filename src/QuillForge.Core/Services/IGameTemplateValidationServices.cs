using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// Read-only catalog used by game-template validation to check that template
/// players select existing configured providers. Implementations may wrap the
/// provider registry, but templates never create provider/model slots.
/// </summary>
public interface IGameTemplateProviderCatalog
{
    Task<IReadOnlySet<string>> ListProviderAliasesAsync(CancellationToken ct = default);
}

/// <summary>
/// Adapter boundary between QuillForge-owned templates and the rules-engine
/// module registry. This keeps Core independent from Den.RulesEngine types while
/// still making module load/setup validation explicit.
/// </summary>
public interface IGameTemplateModuleValidator
{
    Task<GameTemplateValidationResult> ValidateAsync(GameTemplate template, CancellationToken ct = default);
}
