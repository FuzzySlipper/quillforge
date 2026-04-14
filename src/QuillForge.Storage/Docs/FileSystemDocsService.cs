using System.Text;
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
    private static readonly HashSet<string> SearchStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "an",
        "and",
        "are",
        "be",
        "between",
        "can",
        "check",
        "does",
        "do",
        "difference",
        "differences",
        "explain",
        "for",
        "help",
        "how",
        "i",
        "in",
        "is",
        "me",
        "my",
        "of",
        "on",
        "or",
        "please",
        "show",
        "tell",
        "that",
        "the",
        "their",
        "these",
        "this",
        "those",
        "to",
        "understand",
        "use",
        "what",
        "which",
        "work",
        "works",
        "you",
        "your",
    };

    private const int TitlePhraseBoost = 300;
    private const int SummaryPhraseBoost = 200;
    private const int BodyPhraseBoost = 100;
    private const int TitleTermBoost = 6;
    private const int SummaryTermBoost = 4;
    private const int BodyTermBoost = 2;
    private const int LeadingBodySnippetTrimLength = 280;

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

        var normalizedQuery = query.Trim();
        var queryTerms = TokenizeQuery(normalizedQuery);
        var candidates = new List<SearchCandidate>();

        foreach (var file in Directory.GetFiles(_docsRoot, "*.md").OrderBy(f => f))
        {
            ct.ThrowIfCancellationRequested();
            var raw = File.ReadAllText(file);
            var (name, summary) = ParseFrontmatter(raw);
            var body = StripFrontmatter(raw);
            var slug = Path.GetFileNameWithoutExtension(file);
            var effectiveName = name ?? slug;
            var effectiveSummary = summary ?? "";

            var match = EvaluateMatch(effectiveName, effectiveSummary, body, normalizedQuery, queryTerms);
            if (!match.IsMatch)
            {
                continue;
            }

            var snippets = CollectSnippets(effectiveName, effectiveSummary, body, normalizedQuery, queryTerms);
            candidates.Add(new SearchCandidate(new DocSearchResult(slug, effectiveName, snippets), match.Score));

            _logger.LogDebug(
                "Doc search matched topic {Slug}: phraseMatch={HasPhraseMatch}, matchedTerms={MatchedTerms}/{RequiredTerms}",
                slug,
                match.HasPhraseMatch,
                match.MatchedTermCount,
                match.RequiredTermCount);
        }

        candidates.Sort(static (left, right) =>
        {
            var scoreComparison = right.Score.CompareTo(left.Score);
            if (scoreComparison != 0)
            {
                return scoreComparison;
            }

            return string.Compare(left.Result.Slug, right.Result.Slug, StringComparison.Ordinal);
        });

        foreach (var candidate in candidates)
        {
            results.Add(candidate.Result);
        }

        _logger.LogDebug("Doc search for \"{Query}\" found {Count} matching topics", normalizedQuery, results.Count);
        return Task.FromResult<IReadOnlyList<DocSearchResult>>(results);
    }

    private static SearchMatch EvaluateMatch(
        string name,
        string summary,
        string body,
        string query,
        IReadOnlyList<string> queryTerms)
    {
        var titlePhraseMatch = ContainsIgnoreCase(name, query);
        var summaryPhraseMatch = ContainsIgnoreCase(summary, query);
        var bodyPhraseMatch = ContainsIgnoreCase(body, query);
        var hasPhraseMatch = titlePhraseMatch || summaryPhraseMatch || bodyPhraseMatch;

        var matchedTermCount = CountMatchedTerms(name, summary, body, queryTerms);
        var titleTermCount = CountMatchedTerms(name, queryTerms);
        var summaryTermCount = CountMatchedTerms(summary, queryTerms);
        var bodyTermCount = CountMatchedTerms(body, queryTerms);
        if (hasPhraseMatch)
        {
            return new SearchMatch(
                true,
                titlePhraseMatch,
                summaryPhraseMatch,
                bodyPhraseMatch,
                matchedTermCount,
                queryTerms.Count,
                titleTermCount,
                summaryTermCount,
                bodyTermCount);
        }

        if (queryTerms.Count == 0)
        {
            return new SearchMatch(false, false, false, false, 0, 0, 0, 0, 0);
        }

        var requiredTermCount = GetRequiredTermCount(queryTerms.Count);
        var isMatch = matchedTermCount >= requiredTermCount;
        return new SearchMatch(
            isMatch,
            false,
            false,
            false,
            matchedTermCount,
            requiredTermCount,
            titleTermCount,
            summaryTermCount,
            bodyTermCount);
    }

    private static List<string> CollectSnippets(
        string name,
        string summary,
        string body,
        string query,
        IReadOnlyList<string> queryTerms)
    {
        var snippets = new List<string>();
        var seenSnippets = new HashSet<string>(StringComparer.Ordinal);
        var frontmatterMatched = false;
        var foundBodySnippet = false;

        if (IsTextMatch(name, query, queryTerms))
        {
            AddSnippet(snippets, seenSnippets, "# " + name);
            frontmatterMatched = true;
        }

        if (!string.IsNullOrWhiteSpace(summary) && IsTextMatch(summary, query, queryTerms))
        {
            AddSnippet(snippets, seenSnippets, "Summary: " + summary);
            frontmatterMatched = true;
        }

        var lines = body.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!IsTextMatch(lines[i], query, queryTerms))
            {
                continue;
            }

            // Include surrounding context (1 line before and after).
            var start = Math.Max(0, i - 1);
            var end = Math.Min(lines.Length - 1, i + 1);
            var snippet = string.Join('\n', lines[start..(end + 1)]).Trim();
            AddSnippet(snippets, seenSnippets, snippet);
            foundBodySnippet = true;
        }

        if (frontmatterMatched && !foundBodySnippet)
        {
            var leadingBodySnippet = ExtractLeadingBodySnippet(body);
            if (!string.IsNullOrWhiteSpace(leadingBodySnippet))
            {
                AddSnippet(snippets, seenSnippets, leadingBodySnippet);
            }
        }

        return snippets;
    }

    private static void AddSnippet(List<string> snippets, HashSet<string> seenSnippets, string snippet)
    {
        if (string.IsNullOrWhiteSpace(snippet))
        {
            return;
        }

        if (!seenSnippets.Add(snippet))
        {
            return;
        }

        snippets.Add(snippet);
    }

    private static bool IsTextMatch(string text, string query, IReadOnlyList<string> queryTerms)
    {
        if (ContainsIgnoreCase(text, query))
        {
            return true;
        }

        foreach (var term in queryTerms)
        {
            if (ContainsIgnoreCase(text, term))
            {
                return true;
            }
        }

        return false;
    }

    private static int CountMatchedTerms(
        string name,
        string summary,
        string body,
        IReadOnlyList<string> queryTerms)
    {
        var matchedTermCount = 0;

        foreach (var term in queryTerms)
        {
            if (ContainsIgnoreCase(name, term) ||
                ContainsIgnoreCase(summary, term) ||
                ContainsIgnoreCase(body, term))
            {
                matchedTermCount++;
            }
        }

        return matchedTermCount;
    }

    private static int CountMatchedTerms(string text, IReadOnlyList<string> queryTerms)
    {
        var matchedTermCount = 0;

        foreach (var term in queryTerms)
        {
            if (ContainsIgnoreCase(text, term))
            {
                matchedTermCount++;
            }
        }

        return matchedTermCount;
    }

    private static int GetRequiredTermCount(int queryTermCount)
    {
        if (queryTermCount <= 4)
        {
            return queryTermCount;
        }

        return queryTermCount - 1;
    }

    private static List<string> TokenizeQuery(string query)
    {
        var terms = new List<string>();
        var seenTerms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var builder = new StringBuilder();

        foreach (var ch in query)
        {
            if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-')
            {
                builder.Append(ch);
                continue;
            }

            AddToken(builder, terms, seenTerms);
        }

        AddToken(builder, terms, seenTerms);
        return terms;
    }

    private static void AddToken(
        StringBuilder builder,
        List<string> terms,
        HashSet<string> seenTerms)
    {
        if (builder.Length == 0)
        {
            return;
        }

        var token = builder.ToString();
        builder.Clear();

        if (token.Length <= 1)
        {
            return;
        }

        if (SearchStopWords.Contains(token))
        {
            return;
        }

        if (!seenTerms.Add(token))
        {
            return;
        }

        terms.Add(token);
    }

    private static bool ContainsIgnoreCase(string text, string value)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
        {
            return false;
        }

        return text.Contains(value, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ExtractLeadingBodySnippet(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var lines = body.ReplaceLineEndings("\n").Split('\n');
        var snippetLines = new List<string>();
        var started = false;

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                if (started)
                {
                    break;
                }

                continue;
            }

            started = true;
            snippetLines.Add(line);
            if (snippetLines.Count >= 3)
            {
                break;
            }
        }

        if (snippetLines.Count == 0)
        {
            return null;
        }

        var snippet = string.Join('\n', snippetLines);
        if (snippet.Length <= LeadingBodySnippetTrimLength)
        {
            return snippet;
        }

        return snippet[..LeadingBodySnippetTrimLength].TrimEnd() + "...";
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

    private sealed record SearchCandidate(DocSearchResult Result, int Score);

    private sealed record SearchMatch(
        bool IsMatch,
        bool HasTitlePhraseMatch,
        bool HasSummaryPhraseMatch,
        bool HasBodyPhraseMatch,
        int MatchedTermCount,
        int RequiredTermCount,
        int TitleTermCount,
        int SummaryTermCount,
        int BodyTermCount)
    {
        public bool HasPhraseMatch => HasTitlePhraseMatch || HasSummaryPhraseMatch || HasBodyPhraseMatch;

        public int Score =>
            (HasTitlePhraseMatch ? TitlePhraseBoost : 0) +
            (HasSummaryPhraseMatch ? SummaryPhraseBoost : 0) +
            (HasBodyPhraseMatch ? BodyPhraseBoost : 0) +
            (TitleTermCount * TitleTermBoost) +
            (SummaryTermCount * SummaryTermBoost) +
            (BodyTermCount * BodyTermBoost) +
            MatchedTermCount;
    }
}
