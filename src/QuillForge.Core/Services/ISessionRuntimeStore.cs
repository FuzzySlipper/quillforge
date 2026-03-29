using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// Persistence for per-session runtime state. Keyed by session ID.
/// Implementations should use atomic writes for crash safety.
///
/// Persistence plan:
///   - ModeSelectionState: persisted (survives restart)
///   - ProfileState: persisted (survives restart)
///   - WriterRuntimeState: persisted (pending content survives restart)
///   - SessionId, LastModified: persisted (metadata)
///
/// The global/default state (SessionId == null) is stored as "default.json"
/// for backward compatibility with the single-session model.
/// </summary>
public interface ISessionRuntimeStore
{
    /// <summary>
    /// Loads runtime state for the given session. Returns a fresh default state
    /// if no persisted state exists.
    /// </summary>
    Task<SessionRuntimeState> LoadAsync(Guid? sessionId, CancellationToken ct = default);

    /// <summary>
    /// Persists runtime state for the given session. Atomic write.
    /// </summary>
    Task SaveAsync(SessionRuntimeState state, CancellationToken ct = default);

    /// <summary>
    /// Deletes persisted runtime state for a session (e.g., when session is deleted).
    /// </summary>
    Task DeleteAsync(Guid sessionId, CancellationToken ct = default);
}
