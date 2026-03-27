using System.Text.Json;

namespace QuillForge.Core.Diagnostics;

/// <summary>
/// Rotating-file LLM debug logger. Writes formatted JSON entries to build/data/llm-debug.log.
/// Thread-safe. Rotates at 10MB with 2 backups.
/// </summary>
public sealed class LlmDebugLogger : ILlmDebugLogger, IDisposable
{
    private const long MaxFileSize = 10_000_000; // 10MB
    private const int MaxBackups = 2;

    private readonly string _logPath;
    private readonly Lock _lock = new();
    private StreamWriter? _writer;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    public LlmDebugLogger(string dataDir)
    {
        _logPath = Path.Combine(dataDir, "llm-debug.log");
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
    }

    public void LogRequest(string agent, string model, int maxTokens, string systemPreview, int messagesCount, int toolsCount)
    {
        var entry = new Dictionary<string, object?>
        {
            ["ts"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["dir"] = "request",
            ["agent"] = agent,
            ["model"] = model,
            ["max_tokens"] = maxTokens,
            ["system_len"] = systemPreview.Length,
            ["system_preview"] = systemPreview.Length > 300 ? systemPreview[..300] : systemPreview,
            ["messages"] = messagesCount,
            ["tools"] = toolsCount,
        };
        Write(entry);
    }

    public void LogResponse(string agent, string model, string? stopReason, string contentPreview, int inputTokens = 0, int outputTokens = 0, string? error = null)
    {
        var entry = new Dictionary<string, object?>
        {
            ["ts"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["dir"] = "response",
            ["agent"] = agent,
            ["model"] = model,
            ["stop_reason"] = stopReason,
            ["input_tokens"] = inputTokens,
            ["output_tokens"] = outputTokens,
            ["content_len"] = contentPreview.Length,
            ["content_preview"] = contentPreview.Length > 500 ? contentPreview[..500] : contentPreview,
        };

        if (error is not null)
            entry["error"] = error;

        Write(entry);
    }

    public void LogError(string agent, string model, string error)
    {
        var entry = new Dictionary<string, object?>
        {
            ["ts"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            ["dir"] = "error",
            ["agent"] = agent,
            ["model"] = model,
            ["error"] = error,
        };
        Write(entry);
    }

    private void Write(Dictionary<string, object?> entry)
    {
        lock (_lock)
        {
            try
            {
                RotateIfNeeded();
                EnsureWriter();

                var direction = entry.GetValueOrDefault("dir")?.ToString() ?? "?";
                var agent = entry.GetValueOrDefault("agent")?.ToString() ?? "?";
                var ts = entry.GetValueOrDefault("ts")?.ToString() ?? "";
                var header = $"──── {direction.ToUpperInvariant()} │ {agent} │ {ts} ";
                header += new string('─', Math.Max(0, 80 - header.Length));

                _writer!.WriteLine();
                _writer.WriteLine(header);
                _writer.WriteLine(JsonSerializer.Serialize(entry, JsonOptions));
                _writer.Flush();
            }
            catch
            {
                // Diagnostic logging should never crash the app
            }
        }
    }

    private void EnsureWriter()
    {
        _writer ??= new StreamWriter(_logPath, append: true) { AutoFlush = false };
    }

    private void RotateIfNeeded()
    {
        if (!File.Exists(_logPath))
            return;

        var info = new FileInfo(_logPath);
        if (info.Length < MaxFileSize)
            return;

        _writer?.Dispose();
        _writer = null;

        // Shift backups: .log.2 → delete, .log.1 → .log.2, .log → .log.1
        for (int i = MaxBackups; i >= 1; i--)
        {
            var src = i == 1 ? _logPath : $"{_logPath}.{i - 1}";
            var dst = $"{_logPath}.{i}";
            if (File.Exists(dst)) File.Delete(dst);
            if (File.Exists(src)) File.Move(src, dst);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }
}
