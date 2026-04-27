using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface IGameIntentTranslationAgent
{
    Task<GameIntentTranslationResult> TranslateAsync(
        GameIntentTranslationRequest request,
        CancellationToken ct = default);
}
