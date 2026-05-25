namespace QuillForge.LibrarianEval;

/// <summary>
/// Configuration for the Librarian evaluation harness.
/// Populated from CLI arguments and environment variables.
/// </summary>
public sealed record LibrarianEvalOptions
{
    /// <summary>
    /// Path to the lore corpus directory (contains subdirectories per lore set).
    /// </summary>
    public required string CorpusPath { get; init; }

    /// <summary>
    /// Name of the lore set to evaluate against (subdirectory of CorpusPath).
    /// </summary>
    public string LoreSetName { get; init; } = "default";

    /// <summary>
    /// Output directory for artifacts (questions.jsonl, retrieval-trace.jsonl, etc.).
    /// </summary>
    public required string OutputDir { get; init; }

    /// <summary>
    /// Path to a JSON file containing evaluation questions. If null, uses built-in synthetic questions.
    /// </summary>
    public string? QuestionsFile { get; init; }

    /// <summary>
    /// OpenAI-compatible base URL for live evaluation.
    /// </summary>
    public string? BaseUrl { get; init; }

    /// <summary>
    /// Model name for live evaluation.
    /// </summary>
    public string? Model { get; init; }

    /// <summary>
    /// API key for the provider. May be null for local/self-hosted endpoints.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>
    /// Provider type for live evaluation.
    /// </summary>
    public string ProviderType { get; init; } = "Custom";

    /// <summary>
    /// Maximum tokens for the Librarian agent.
    /// </summary>
    public int MaxTokens { get; init; } = 4096;

    /// <summary>
    /// Number of questions to run (for bounded live runs). 0 = all.
    /// </summary>
    public int Limit { get; init; }
}
