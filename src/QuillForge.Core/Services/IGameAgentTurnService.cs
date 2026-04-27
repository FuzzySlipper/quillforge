using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface IGameAgentTurnService
{
    Task<SessionMutationResult<GameAgentTurnRunResult>> RunPendingAgentTurnsAsync(
        Guid sessionId,
        RunGameAgentTurnsCommand command,
        CancellationToken ct = default);
}
