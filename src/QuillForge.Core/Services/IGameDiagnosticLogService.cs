using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface IGameDiagnosticLogService
{
    Task<GameDiagnosticLogProjection> GetLogAsync(
        Guid sessionId,
        GameDiagnosticLogQuery? query = null,
        CancellationToken ct = default);
}
