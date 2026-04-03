using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// Owns lifecycle operations for the persisted session unit: conversation tree
/// plus session runtime state.
/// </summary>
public interface ISessionLifecycleService
{
    Task<ConversationTree> ForkAsync(Guid sourceSessionId, Guid? messageId = null, CancellationToken ct = default);

    Task DeleteAsync(Guid sessionId, CancellationToken ct = default);
}
