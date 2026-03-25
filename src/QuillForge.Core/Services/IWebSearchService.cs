namespace QuillForge.Core.Services;

/// <summary>
/// Searches the web for real-world information.
/// </summary>
public interface IWebSearchService
{
    Task<IReadOnlyList<WebSearchResult>> SearchAsync(string query, CancellationToken ct = default);
}

public sealed record WebSearchResult(string Title, string Url, string Snippet);
