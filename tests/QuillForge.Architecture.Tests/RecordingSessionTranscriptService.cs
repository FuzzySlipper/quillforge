using QuillForge.Core.Services;

namespace QuillForge.Architecture.Tests;

internal sealed class RecordingSessionTranscriptService : ISessionTranscriptService
{
    public List<Guid> SyncedSessionIds { get; } = [];

    public Task SyncRoleplayTranscriptAsync(Guid sessionId, CancellationToken ct = default)
    {
        SyncedSessionIds.Add(sessionId);
        return Task.CompletedTask;
    }
}
