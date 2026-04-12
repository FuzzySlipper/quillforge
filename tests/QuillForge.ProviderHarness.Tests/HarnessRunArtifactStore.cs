using System.Text.Json;

namespace QuillForge.ProviderHarness.Tests;

public sealed class HarnessRunArtifactStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly Lock _lock = new();
    private readonly List<HarnessProviderTraceArtifactReference> _providerTraceFiles = [];
    private int _providerTraceCount;

    public HarnessRunArtifactStore(string scenarioName, string? baseRoot = null)
    {
        ScenarioName = scenarioName;
        RunId = Guid.NewGuid().ToString("N");
        BaseRoot = Path.GetFullPath(baseRoot ?? Path.Combine(Path.GetTempPath(), "quillforge-harness-runs"));
        CreatedAt = DateTimeOffset.UtcNow;

        var sanitizedScenario = SanitizePathSegment(scenarioName);
        RunDirectory = Path.Combine(
            BaseRoot,
            $"{CreatedAt:yyyyMMdd-HHmmss}-{sanitizedScenario}-{RunId[..8]}");

        ProviderDirectory = Path.Combine(RunDirectory, "provider");
        ProviderTracesDirectory = Path.Combine(ProviderDirectory, "traces");
        AppDirectory = Path.Combine(RunDirectory, "app");
        ArtifactsDirectory = Path.Combine(RunDirectory, "artifacts");
        ReportsDirectory = Path.Combine(RunDirectory, "reports");

        Directory.CreateDirectory(ProviderTracesDirectory);
        Directory.CreateDirectory(AppDirectory);
        Directory.CreateDirectory(ArtifactsDirectory);
        Directory.CreateDirectory(ReportsDirectory);

        PersistManifest();
    }

    public const int SchemaVersion = 1;

    public string RunId { get; }
    public string ScenarioName { get; }
    public string BaseRoot { get; }
    public DateTimeOffset CreatedAt { get; }
    public string RunDirectory { get; }
    public string ProviderDirectory { get; }
    public string ProviderTracesDirectory { get; }
    public string AppDirectory { get; }
    public string ArtifactsDirectory { get; }
    public string ReportsDirectory { get; }

    public void PersistProviderTrace(HarnessProviderTrace trace)
    {
        lock (_lock)
        {
            _providerTraceCount++;
            var fileName = $"{_providerTraceCount:000}-{trace.TraceId}.json";
            var absolutePath = Path.Combine(ProviderTracesDirectory, fileName);
            WriteJsonAtomic(absolutePath, trace);

            _providerTraceFiles.Add(new HarnessProviderTraceArtifactReference
            {
                Sequence = _providerTraceCount,
                TraceId = trace.TraceId,
                RelativePath = Path.GetRelativePath(RunDirectory, absolutePath).Replace('\\', '/'),
                Model = trace.Model,
                Stream = trace.Stream,
                ResponseMode = trace.ResponseMode.ToString(),
                StatusCode = trace.StatusCode,
                FinishReason = trace.FinishReason,
                Fault = trace.Fault,
            });

            PersistManifest();
        }
    }

    public IReadOnlyList<HarnessProviderTraceArtifactReference> SnapshotProviderTraceFiles()
    {
        lock (_lock)
        {
            return _providerTraceFiles.ToList();
        }
    }

    public string PersistJson<T>(string relativePath, T value)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        var absolutePath = ResolvePath(normalizedRelativePath);
        WriteJsonAtomic(absolutePath, value);
        return normalizedRelativePath;
    }

    public string PersistText(string relativePath, string content)
    {
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        var absolutePath = ResolvePath(normalizedRelativePath);
        WriteTextAtomic(absolutePath, content);
        return normalizedRelativePath;
    }

    private void PersistManifest()
    {
        var manifest = new HarnessRunArtifactManifest
        {
            SchemaVersion = SchemaVersion,
            RunId = RunId,
            ScenarioName = ScenarioName,
            CreatedAt = CreatedAt,
            RunDirectory = RunDirectory.Replace('\\', '/'),
            ProviderDirectory = Path.GetRelativePath(RunDirectory, ProviderDirectory).Replace('\\', '/'),
            AppDirectory = Path.GetRelativePath(RunDirectory, AppDirectory).Replace('\\', '/'),
            ArtifactsDirectory = Path.GetRelativePath(RunDirectory, ArtifactsDirectory).Replace('\\', '/'),
            ReportsDirectory = Path.GetRelativePath(RunDirectory, ReportsDirectory).Replace('\\', '/'),
            ProviderTraceFiles = _providerTraceFiles.ToList(),
        };

        WriteJsonAtomic(Path.Combine(RunDirectory, "run-manifest.json"), manifest);
    }

    private static void WriteJsonAtomic<T>(string path, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path + ".tmp";
        var json = JsonSerializer.Serialize(value, JsonOptions);
        WriteTextAtomic(tempPath, json);
        File.Move(tempPath, path, overwrite: true);
    }

    private static void WriteTextAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tempPath = path.EndsWith(".tmp", StringComparison.Ordinal) ? path : path + ".tmp";
        File.WriteAllText(tempPath, content);

        if (!ReferenceEquals(tempPath, path) && !string.Equals(tempPath, path, StringComparison.Ordinal))
        {
            File.Move(tempPath, path, overwrite: true);
        }
    }

    private string ResolvePath(string normalizedRelativePath)
    {
        var absolutePath = Path.GetFullPath(Path.Combine(RunDirectory, normalizedRelativePath));
        if (!absolutePath.StartsWith(RunDirectory, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Harness artifact path '{normalizedRelativePath}' resolves outside run directory '{RunDirectory}'.");
        }

        return absolutePath;
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new InvalidOperationException("Harness artifact path must not be empty.");
        }

        return relativePath.Replace('\\', '/').TrimStart('/');
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unnamed-run";
        }

        var buffer = new char[value.Length];
        var count = 0;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                buffer[count++] = char.ToLowerInvariant(character);
                continue;
            }

            if (character is '-' or '_' or '.')
            {
                buffer[count++] = character;
                continue;
            }

            if (char.IsWhiteSpace(character) || character == '/')
            {
                buffer[count++] = '-';
            }
        }

        var sanitized = new string(buffer, 0, count).Trim('-');
        return string.IsNullOrWhiteSpace(sanitized) ? "unnamed-run" : sanitized;
    }
}

public sealed record HarnessRunArtifactManifest
{
    public required int SchemaVersion { get; init; }
    public required string RunId { get; init; }
    public required string ScenarioName { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required string RunDirectory { get; init; }
    public required string ProviderDirectory { get; init; }
    public required string AppDirectory { get; init; }
    public required string ArtifactsDirectory { get; init; }
    public required string ReportsDirectory { get; init; }
    public IReadOnlyList<HarnessProviderTraceArtifactReference> ProviderTraceFiles { get; init; } = [];
}

public sealed record HarnessProviderTraceArtifactReference
{
    public required int Sequence { get; init; }
    public required string TraceId { get; init; }
    public required string RelativePath { get; init; }
    public string? Model { get; init; }
    public required bool Stream { get; init; }
    public required string ResponseMode { get; init; }
    public required int StatusCode { get; init; }
    public string? FinishReason { get; init; }
    public string? Fault { get; init; }
}
