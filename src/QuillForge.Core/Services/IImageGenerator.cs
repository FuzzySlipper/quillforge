namespace QuillForge.Core.Services;

/// <summary>
/// Generate images from text prompts.
/// </summary>
public interface IImageGenerator
{
    Task<ImageResult> GenerateAsync(string prompt, ImageOptions? options = null, CancellationToken ct = default);
}

public sealed record ImageResult(string FilePath, int Width, int Height);

public sealed record ImageOptions
{
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Style { get; init; }
}
