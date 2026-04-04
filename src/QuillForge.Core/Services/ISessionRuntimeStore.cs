using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// Persistence for per-session state. Keyed by session ID.
/// Implementations should use atomic writes for crash safety.
///
/// Persistence plan:
///   - ModeSelectionState: persisted (survives restart)
///   - ProfileState: persisted (survives restart)
///   - WriterRuntimeState: persisted (pending content survives restart)
///   - SessionId, LastModified: persisted (metadata)
///
/// SessionId == null is a transient pre-session view used by read paths before a
/// real session has been created. It is not persisted.
/// </summary>
public interface ISessionStateStore
{
    /// <summary>
    /// Loads session state for the given session. Returns a fresh default state
    /// if no persisted state exists. Passing null returns the transient
    /// pre-session default view.
    /// </summary>
    Task<SessionState> LoadAsync(Guid? sessionId, CancellationToken ct = default);

    /// <summary>
    /// Persists session state for the given session. Atomic write.
    /// </summary>
    Task SaveAsync(SessionState state, CancellationToken ct = default);

    /// <summary>
    /// Deletes persisted runtime state for a session (e.g., when session is deleted).
    /// </summary>
    Task DeleteAsync(Guid sessionId, CancellationToken ct = default);

    /// <summary>
    /// Returns persisted session IDs whose stored profile binding references the
    /// given durable profile ID.
    /// </summary>
    Task<IReadOnlyList<Guid>> FindSessionIdsByProfileIdAsync(string profileId, CancellationToken ct = default);
}

public interface ISessionRuntimeStore : ISessionStateStore
{
}
