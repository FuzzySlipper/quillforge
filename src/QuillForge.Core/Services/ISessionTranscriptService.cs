namespace QuillForge.Core.Services;

/// <summary>
/// Owns synchronization between session conversation history and any
/// authoritative persisted transcript artifacts derived from it.
/// </summary>
public interface ISessionTranscriptService
{
    Task SyncRoleplayTranscriptAsync(Guid sessionId, CancellationToken ct = default);
}
