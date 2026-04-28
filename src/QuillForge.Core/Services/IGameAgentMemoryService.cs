using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public interface IGameAgentMemoryService
{
    Task<SessionMutationResult<GameAgentMemorySummaryRunResult>> RunRoundEndMemorySummariesAsync(
        Guid sessionId,
        RunGameAgentMemorySummariesCommand command,
        CancellationToken ct = default);
}
