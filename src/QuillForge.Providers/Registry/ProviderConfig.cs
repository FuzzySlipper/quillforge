namespace QuillForge.Providers.Registry;

/// <summary>
/// Configuration for a single LLM provider (Anthropic, OpenAI, etc.).
/// </summary>
public sealed record ProviderConfig
{
    public required string Alias { get; init; }
    public required ProviderType Type { get; init; }
    public required string ApiKey { get; init; }
    public string? BaseUrl { get; init; }
    public string? DefaultModel { get; init; }
    public IReadOnlyDictionary<string, string>? ExtraSettings { get; init; }
}

public enum ProviderType
{
    Anthropic,
    OpenAI,
    AzureOpenAI,
    Ollama,
    OpenRouter,
    Custom,
}
