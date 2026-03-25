namespace QuillForge.Core.Services;

/// <summary>
/// Generate speech audio from text.
/// </summary>
public interface ITtsGenerator
{
    Task<TtsResult> GenerateAsync(string text, TtsOptions? options = null, CancellationToken ct = default);
}

public sealed record TtsResult(string FilePath, TimeSpan Duration);

public sealed record TtsOptions
{
    public string? Voice { get; init; }
    public double? Speed { get; init; }
}
