using System.Text.Json;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents.Tools;

/// <summary>
/// Wraps a tool handler with rate limiting. All callers sharing the same instance
/// queue through a semaphore with a minimum delay between invocations.
/// </summary>
public sealed class ThrottledToolHandler : IToolHandler
{
    private readonly IToolHandler _inner;
    private readonly SemaphoreSlim _gate = new(1);
    private readonly TimeSpan _minInterval;
    private DateTime _lastCall = DateTime.MinValue;

    public ThrottledToolHandler(IToolHandler inner, TimeSpan minInterval)
    {
        _inner = inner;
        _minInterval = minInterval;
    }

    public string Name => _inner.Name;
    public ToolDefinition Definition => _inner.Definition;

    public async Task<ToolResult> HandleAsync(JsonElement input, AgentContext context, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var elapsed = DateTime.UtcNow - _lastCall;
            if (elapsed < _minInterval)
            {
                await Task.Delay(_minInterval - elapsed, ct);
            }

            var result = await _inner.HandleAsync(input, context, ct);
            _lastCall = DateTime.UtcNow;
            return result;
        }
        finally
        {
            _gate.Release();
        }
    }
}
