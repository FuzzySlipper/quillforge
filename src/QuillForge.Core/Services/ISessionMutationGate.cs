namespace QuillForge.Core.Services;

public interface ISessionMutationGate
{
    Task<SessionGateLease?> TryAcquireAsync(
        Guid? sessionId,
        string operationName,
        CancellationToken ct = default);
}

public interface SessionGateLease : IAsyncDisposable
{
    Guid? SessionId { get; }
    string OperationName { get; }
}
