using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface IGameInspectorService
{
    Task<GameInspectorProjection> GetProjectionAsync(
        Guid sessionId,
        int promptEnvelopeLimit = 10,
        CancellationToken ct = default);
}
