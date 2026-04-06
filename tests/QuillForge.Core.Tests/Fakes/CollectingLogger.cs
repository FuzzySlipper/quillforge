using Microsoft.Extensions.Logging;

namespace QuillForge.Core.Tests.Fakes;

public sealed class CollectingLogger<T> : ILogger<T>
{
    public List<LogEntry> Entries { get; } = [];

    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull
    {
        return NoOpDisposable.Instance;
    }

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);

        if (state is IEnumerable<KeyValuePair<string, object?>> structuredState)
        {
            foreach (var pair in structuredState)
            {
                properties[pair.Key] = pair.Value;
            }
        }

        var message = formatter(state, exception);
        var template = properties.TryGetValue("{OriginalFormat}", out var originalFormat)
            ? originalFormat?.ToString()
            : null;

        Entries.Add(new LogEntry(logLevel, eventId, message, template, exception, properties));
    }

    private sealed class NoOpDisposable : IDisposable
    {
        public static NoOpDisposable Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}

public sealed record LogEntry(
    LogLevel Level,
    EventId EventId,
    string Message,
    string? Template,
    Exception? Exception,
    IReadOnlyDictionary<string, object?> Properties);
