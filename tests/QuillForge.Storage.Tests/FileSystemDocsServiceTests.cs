using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Storage.Docs;

namespace QuillForge.Storage.Tests;

public sealed class FileSystemDocsServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemDocsService _service;

    public FileSystemDocsServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"quillforge-docs-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _service = new FileSystemDocsService(_tempDir,
            NullLoggerFactory.Instance.CreateLogger<FileSystemDocsService>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task ListTopics_ReturnsTopicsFromFrontmatter()
    {
        File.WriteAllText(Path.Combine(_tempDir, "tools.md"), """
            ---
            name: Tools Reference
            summary: Every tool available
            ---
            # Tools
            Content here.
            """);

        File.WriteAllText(Path.Combine(_tempDir, "profiles.md"), """
            ---
            name: Profiles
            summary: How profiles work
            ---
            # Profiles
            Content here.
            """);

        var topics = await _service.ListTopicsAsync();

        Assert.Equal(2, topics.Count);
        Assert.Equal("profiles", topics[0].Slug);
        Assert.Equal("Profiles", topics[0].Name);
        Assert.Equal("How profiles work", topics[0].Summary);
        Assert.Equal("tools", topics[1].Slug);
    }

    [Fact]
    public async Task ListTopics_EmptyDirectory_ReturnsEmpty()
    {
        var topics = await _service.ListTopicsAsync();
        Assert.Empty(topics);
    }

    [Fact]
    public async Task ListTopics_NonexistentDirectory_ReturnsEmpty()
    {
        var service = new FileSystemDocsService("/nonexistent/path",
            NullLoggerFactory.Instance.CreateLogger<FileSystemDocsService>());

        var topics = await service.ListTopicsAsync();
        Assert.Empty(topics);
    }

    [Fact]
    public async Task GetTopic_ReturnsContentWithoutFrontmatter()
    {
        File.WriteAllText(Path.Combine(_tempDir, "modes.md"), """
            ---
            name: Modes Overview
            summary: All the modes
            ---
            # Modes
            Here is the content.
            """);

        var entry = await _service.GetTopicAsync("modes");

        Assert.NotNull(entry);
        Assert.Equal("modes", entry.Slug);
        Assert.Equal("Modes Overview", entry.Name);
        Assert.Equal("All the modes", entry.Summary);
        Assert.StartsWith("# Modes", entry.Content);
        Assert.DoesNotContain("---", entry.Content);
    }

    [Fact]
    public async Task GetTopic_NotFound_ReturnsNull()
    {
        var entry = await _service.GetTopicAsync("nonexistent");
        Assert.Null(entry);
    }

    [Fact]
    public async Task GetTopic_NoFrontmatter_UsesSlugAsName()
    {
        File.WriteAllText(Path.Combine(_tempDir, "plain.md"), "Just content, no frontmatter.");

        var entry = await _service.GetTopicAsync("plain");

        Assert.NotNull(entry);
        Assert.Equal("plain", entry.Name);
        Assert.Equal("", entry.Summary);
        Assert.Equal("Just content, no frontmatter.", entry.Content);
    }

    [Fact]
    public async Task Search_FindsMatchingLines()
    {
        File.WriteAllText(Path.Combine(_tempDir, "tools.md"), """
            ---
            name: Tools Reference
            summary: Tool docs
            ---
            # Tools
            The query_lore tool queries the Librarian.
            The write_prose tool generates prose.
            The roll_dice tool rolls dice.
            """);

        File.WriteAllText(Path.Combine(_tempDir, "modes.md"), """
            ---
            name: Modes
            summary: Mode docs
            ---
            # Modes
            General mode has no special behavior.
            """);

        var results = await _service.SearchAsync("query_lore");

        Assert.Single(results);
        Assert.Equal("tools", results[0].Slug);
        Assert.Contains(results[0].Snippets, s => s.Contains("query_lore"));
    }

    [Fact]
    public async Task Search_MultiTermQuery_MatchesAcrossSummaryAndBody()
    {
        File.WriteAllText(Path.Combine(_tempDir, "ownership.md"), """
            ---
            name: Ownership
            summary: AppConfig and ProfileConfig separate global defaults from reusable bundles.
            ---
            SessionState owns the active runtime for one session.
            ConversationTree stores the branching message history.
            """);

        var results = await _service.SearchAsync("AppConfig ProfileConfig SessionState ConversationTree");

        Assert.Single(results);
        Assert.Equal("ownership", results[0].Slug);
        Assert.Contains(results[0].Snippets, snippet => snippet.Contains("Summary: AppConfig", StringComparison.Ordinal));
        Assert.Contains(results[0].Snippets, snippet => snippet.Contains("ConversationTree", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Search_NaturalLanguageQuery_IgnoresCommonInstructionWords()
    {
        File.WriteAllText(Path.Combine(_tempDir, "ownership.md"), """
            ---
            name: Ownership
            summary: AppConfig and ProfileConfig separate global defaults from reusable bundles.
            ---
            SessionState owns the active runtime for one session.
            ConversationTree stores the branching message history.
            """);

        var results = await _service.SearchAsync(
            "Can you check the docs and explain the difference between AppConfig, ProfileConfig, SessionState, and ConversationTree?");

        Assert.Single(results);
        Assert.Equal("ownership", results[0].Slug);
    }

    [Fact]
    public async Task Search_KeepsDomainTermsLikeWorkflow()
    {
        File.WriteAllText(Path.Combine(_tempDir, "writer.md"), """
            ---
            name: Writer Mode
            summary: Guided prose generation with approval workflow.
            ---
            Writer mode drafts prose and waits for approval before saving.
            """);

        File.WriteAllText(Path.Combine(_tempDir, "other.md"), """
            ---
            name: Misc Notes
            summary: Unrelated summary.
            ---
            This note mentions the writer once but says nothing about approvals.
            """);

        var results = await _service.SearchAsync("writer workflow");

        var match = Assert.Single(results);
        Assert.Equal("writer", match.Slug);
    }

    [Fact]
    public async Task Search_FindsMatchesInTopicNameAndSummary()
    {
        File.WriteAllText(Path.Combine(_tempDir, "tree.md"), """
            ---
            name: ConversationTree Guide
            summary: ConversationTree stores branching chat history.
            ---
            This topic covers persistence details.
            """);

        var results = await _service.SearchAsync("ConversationTree");

        Assert.Single(results);
        Assert.Contains(results[0].Snippets, snippet => snippet.Contains("# ConversationTree Guide", StringComparison.Ordinal));
        Assert.Contains(results[0].Snippets, snippet => snippet.Contains("Summary: ConversationTree", StringComparison.Ordinal));
        Assert.Contains(results[0].Snippets, snippet => snippet.Contains("This topic covers persistence details.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Search_PrefersTitlePhraseMatchOverBodyPhraseMatch()
    {
        File.WriteAllText(Path.Combine(_tempDir, "tree.md"), """
            ---
            name: ConversationTree Guide
            summary: ConversationTree stores branching chat history.
            ---
            This topic covers persistence details.
            """);

        File.WriteAllText(Path.Combine(_tempDir, "notes.md"), """
            ---
            name: Persistence Notes
            summary: Save and load behavior.
            ---
            This page mentions ConversationTree once in passing near the end.
            """);

        var results = await _service.SearchAsync("ConversationTree");

        Assert.Equal(2, results.Count);
        Assert.Equal("tree", results[0].Slug);
        Assert.Equal("notes", results[1].Slug);
    }

    [Fact]
    public async Task Search_AllInstructionWordsWithoutPhraseMatch_ReturnsEmpty()
    {
        File.WriteAllText(Path.Combine(_tempDir, "writer.md"), """
            ---
            name: Writer Mode
            summary: Guided prose generation with approval workflow.
            ---
            Writer mode drafts prose and waits for approval before saving.
            """);

        var results = await _service.SearchAsync("can you help me please");

        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_FiveTermRelaxation_DoesNotSurfaceLooseNoiseMatches()
    {
        File.WriteAllText(Path.Combine(_tempDir, "strong.md"), """
            ---
            name: Runtime Ownership
            summary: alpha beta gamma delta
            ---
            This document explains how ownership works.
            """);

        File.WriteAllText(Path.Combine(_tempDir, "noise.md"), """
            ---
            name: Loose Notes
            summary: alpha beta
            ---
            This document is not actually about the same topic.
            """);

        var results = await _service.SearchAsync("alpha beta gamma delta epsilon");

        var match = Assert.Single(results);
        Assert.Equal("strong", match.Slug);
    }

    [Fact]
    public async Task Search_CaseInsensitive()
    {
        File.WriteAllText(Path.Combine(_tempDir, "test.md"), """
            ---
            name: Test
            summary: Test doc
            ---
            The LIBRARIAN agent handles lore.
            """);

        var results = await _service.SearchAsync("librarian");

        Assert.Single(results);
    }

    [Fact]
    public async Task Search_NoMatches_ReturnsEmpty()
    {
        File.WriteAllText(Path.Combine(_tempDir, "test.md"), """
            ---
            name: Test
            summary: Test doc
            ---
            Nothing relevant here.
            """);

        var results = await _service.SearchAsync("xyznonexistent");
        Assert.Empty(results);
    }

    [Fact]
    public async Task Search_EmptyQuery_ReturnsEmpty()
    {
        var results = await _service.SearchAsync("");
        Assert.Empty(results);
    }

    [Fact]
    public async Task GetTopic_PathTraversal_ReturnsNull()
    {
        // Create a file outside the docs root that would be reachable via traversal
        var parentDir = Path.GetDirectoryName(_tempDir)!;
        var secretFile = Path.Combine(parentDir, "secret.md");
        File.WriteAllText(secretFile, "---\nname: Secret\nsummary: Private\n---\nSecret content.");
        try
        {
            var entry = await _service.GetTopicAsync("../secret");
            Assert.Null(entry);
        }
        finally
        {
            File.Delete(secretFile);
        }
    }

    [Fact]
    public async Task GetTopic_SiblingPrefixTraversal_ReturnsNull()
    {
        // A slug that tries to escape via a sibling directory with a matching prefix
        var entry = await _service.GetTopicAsync("../../etc/passwd");
        Assert.Null(entry);
    }

    [Fact]
    public async Task GetTopic_CaseOnlySiblingTraversal_ReturnsNull()
    {
        var parentDir = Path.GetDirectoryName(_tempDir)!;
        var siblingName = Path.GetFileName(_tempDir).ToUpperInvariant();
        if (string.Equals(siblingName, Path.GetFileName(_tempDir), StringComparison.Ordinal))
        {
            return;
        }

        var siblingDir = Path.Combine(parentDir, siblingName);
        Directory.CreateDirectory(siblingDir);
        var secretFile = Path.Combine(siblingDir, "secret.md");
        File.WriteAllText(secretFile, "---\nname: Secret\nsummary: Private\n---\nSecret content.");

        try
        {
            var entry = await _service.GetTopicAsync("../" + siblingName + "/secret");
            Assert.Null(entry);
        }
        finally
        {
            Directory.Delete(siblingDir, recursive: true);
        }
    }
}
