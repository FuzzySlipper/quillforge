namespace QuillForge.Core.Services;

/// <summary>
/// Read-only access to the app documentation files.
/// Docs live outside the user content root (alongside the binary)
/// and are not modifiable by the LLM.
/// </summary>
public interface IDocsService
{
    /// <summary>
    /// Lists all available doc topics with their names and summaries.
    /// </summary>
    Task<IReadOnlyList<DocTopic>> ListTopicsAsync(CancellationToken ct = default);

    /// <summary>
    /// Returns the full content of a single doc by topic slug (filename without extension).
    /// Returns null if not found.
    /// </summary>
    Task<DocEntry?> GetTopicAsync(string slug, CancellationToken ct = default);

    /// <summary>
    /// Searches all docs for lines matching the query string (case-insensitive).
    /// Returns matching snippets grouped by topic.
    /// </summary>
    Task<IReadOnlyList<DocSearchResult>> SearchAsync(string query, CancellationToken ct = default);
}

public sealed record DocTopic(string Slug, string Name, string Summary);

public sealed record DocEntry(string Slug, string Name, string Summary, string Content);

public sealed record DocSearchResult(string Slug, string Name, IReadOnlyList<string> Snippets);
