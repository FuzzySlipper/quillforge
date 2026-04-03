using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace QuillForge.Core.Services;

public sealed class InMemorySessionMutationGate : ISessionMutationGate
{
    private readonly ConcurrentDictionary<string, byte> _heldSessions = new(StringComparer.Ordinal);
    private readonly ILogger<InMemorySessionMutationGate> _logger;

    public InMemorySessionMutationGate(ILogger<InMemorySessionMutationGate> logger)
    {
        _logger = logger;
    }

    public Task<SessionGateLease?> TryAcquireAsync(
        Guid? sessionId,
        string operationName,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        var key = GetKey(sessionId);
        if (!_heldSessions.TryAdd(key, 0))
        {
            _logger.LogWarning(
                "Session mutation rejected as busy: session={SessionId} operation={Operation}",
                sessionId,
                operationName);
            return Task.FromResult<SessionGateLease?>(null);
        }

        _logger.LogInformation(
            "Session mutation gate acquired: session={SessionId} operation={Operation}",
            sessionId,
            operationName);

        return Task.FromResult<SessionGateLease?>(new Lease(this, key, sessionId, operationName));
    }

    private void Release(string key, Guid? sessionId, string operationName)
    {
        _heldSessions.TryRemove(key, out _);
        _logger.LogInformation(
            "Session mutation gate released: session={SessionId} operation={Operation}",
            sessionId,
            operationName);
    }

    private static string GetKey(Guid? sessionId)
    {
        return sessionId?.ToString() ?? "default";
    }

    private sealed class Lease : SessionGateLease
    {
        private readonly InMemorySessionMutationGate _owner;
        private readonly string _key;
        private int _disposed;

        public Lease(
            InMemorySessionMutationGate owner,
            string key,
            Guid? sessionId,
            string operationName)
        {
            _owner = owner;
            _key = key;
            SessionId = sessionId;
            OperationName = operationName;
        }

        public Guid? SessionId { get; }

        public string OperationName { get; }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.Release(_key, SessionId, OperationName);
            }

            return ValueTask.CompletedTask;
        }
    }
}
