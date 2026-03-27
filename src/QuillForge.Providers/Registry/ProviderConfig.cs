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
    public string? ModelsUrl { get; init; }
    public string? DefaultModel { get; init; }
    public int? ContextLimit { get; init; }
    public IReadOnlyDictionary<string, string>? ExtraSettings { get; init; }
    public ProviderOptions? Options { get; init; }
}

/// <summary>
/// Per-provider sampling parameters.
/// </summary>
public sealed record ProviderOptions
{
    public float? Temperature { get; init; }
    public float? TopP { get; init; }
    public int? TopK { get; init; }
    public float? FrequencyPenalty { get; init; }
    public float? PresencePenalty { get; init; }
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
