using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface IGameDiagnosticLogService
{
    Task<GameDiagnosticLogProjection> GetLogAsync(
        Guid sessionId,
        int promptPreviewCharacters = 1200,
        CancellationToken ct = default);
}
