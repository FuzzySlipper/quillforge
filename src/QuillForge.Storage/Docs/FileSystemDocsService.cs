using Microsoft.Extensions.Logging;
using QuillForge.Core.Services;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.Docs;

/// <summary>
/// Reads documentation markdown files from a docs root directory.
/// Each file must have YAML frontmatter with 'name' and 'summary' fields.
/// </summary>
public sealed class FileSystemDocsService : IDocsService
{
    private readonly string _docsRoot;
    private readonly ILogger<FileSystemDocsService> _logger;

    public FileSystemDocsService(string docsRoot, ILogger<FileSystemDocsService> logger)
    {
        _docsRoot = docsRoot;
        _logger = logger;
    }

    public Task<IReadOnlyList<DocTopic>> ListTopicsAsync(CancellationToken ct = default)
    {
        var topics = new List<DocTopic>();

        if (!Directory.Exists(_docsRoot))
        {
            _logger.LogWarning("Docs directory does not exist: {DocsRoot}", _docsRoot);
            return Task.FromResult<IReadOnlyList<DocTopic>>(topics);
        }

        foreach (var file in Directory.GetFiles(_docsRoot, "*.md").OrderBy(f => f))
        {
            ct.ThrowIfCancellationRequested();
            var slug = Path.GetFileNameWithoutExtension(file);
            var (name, summary) = ParseFrontmatter(File.ReadAllText(file));
            topics.Add(new DocTopic(slug, name ?? slug, summary ?? ""));
        }

        _logger.LogDebug("Listed {Count} doc topics from {DocsRoot}", topics.Count, _docsRoot);
        return Task.FromResult<IReadOnlyList<DocTopic>>(topics);
    }

    public Task<DocEntry?> GetTopicAsync(string slug, CancellationToken ct = default)
    {
        var path = ResolveTopicPath(slug);
        if (path is null || !File.Exists(path))
        {
            _logger.LogDebug("Doc topic not found: {Slug}", slug);
            return Task.FromResult<DocEntry?>(null);
        }

        var raw = File.ReadAllText(path);
        var (name, summary) = ParseFrontmatter(raw);
        var body = StripFrontmatter(raw);

        _logger.LogDebug("Loaded doc topic: {Slug}", slug);
        return Task.FromResult<DocEntry?>(new DocEntry(slug, name ?? slug, summary ?? "", body));
    }

    public Task<IReadOnlyList<DocSearchResult>> SearchAsync(string query, CancellationToken ct = default)
    {
        var results = new List<DocSearchResult>();

        if (!Directory.Exists(_docsRoot) || string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult<IReadOnlyList<DocSearchResult>>(results);
        }

        foreach (var file in Directory.GetFiles(_docsRoot, "*.md").OrderBy(f => f))
        {
            ct.ThrowIfCancellationRequested();
            var raw = File.ReadAllText(file);
            var (name, _) = ParseFrontmatter(raw);
            var body = StripFrontmatter(raw);
            var slug = Path.GetFileNameWithoutExtension(file);

            var snippets = new List<string>();
            var lines = body.Split('\n');
            for (var i = 0; i < lines.Length; i++)
            {
                if (lines[i].Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    // Include surrounding context (1 line before and after)
                    var start = Math.Max(0, i - 1);
                    var end = Math.Min(lines.Length - 1, i + 1);
                    var snippet = string.Join('\n', lines[start..(end + 1)]).Trim();
                    if (!string.IsNullOrEmpty(snippet))
                    {
                        snippets.Add(snippet);
                    }
                }
            }

            if (snippets.Count > 0)
            {
                results.Add(new DocSearchResult(slug, name ?? slug, snippets));
            }
        }

        _logger.LogDebug("Doc search for \"{Query}\" found {Count} matching topics", query, results.Count);
        return Task.FromResult<IReadOnlyList<DocSearchResult>>(results);
    }

    private static (string? Name, string? Summary) ParseFrontmatter(string content)
    {
        if (!content.StartsWith("---"))
        {
            return (null, null);
        }

        var endIndex = content.IndexOf("---", 3, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return (null, null);
        }

        var frontmatter = content[3..endIndex];
        string? name = null;
        string? summary = null;

        foreach (var line in frontmatter.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
            {
                name = trimmed["name:".Length..].Trim();
            }
            else if (trimmed.StartsWith("summary:", StringComparison.OrdinalIgnoreCase))
            {
                summary = trimmed["summary:".Length..].Trim();
            }
        }

        return (name, summary);
    }

    /// <summary>
    /// Resolves a topic slug to a full path within the docs root, rejecting path traversal.
    /// Returns null if the resolved path escapes the docs root.
    /// </summary>
    private string? ResolveTopicPath(string slug)
    {
        if (!PathBoundaryGuard.TryResolvePath(_docsRoot, slug + ".md", out var resolved))
        {
            _logger.LogWarning("Path traversal blocked for doc slug: {Slug}", slug);
            return null;
        }

        return resolved;
    }

    private static string StripFrontmatter(string content)
    {
        if (!content.StartsWith("---"))
        {
            return content;
        }

        var endIndex = content.IndexOf("---", 3, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return content;
        }

        return content[(endIndex + 3)..].TrimStart('\r', '\n');
    }
}
