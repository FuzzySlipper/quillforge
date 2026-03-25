using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// Persistence for conversation trees. Implemented with atomic file writes in Storage.
/// </summary>
public interface ISessionStore
{
    Task<ConversationTree> LoadAsync(Guid sessionId, CancellationToken ct = default);
    Task SaveAsync(ConversationTree session, CancellationToken ct = default);
    Task<IReadOnlyList<SessionSummary>> ListAsync(CancellationToken ct = default);
    Task DeleteAsync(Guid sessionId, CancellationToken ct = default);
}
