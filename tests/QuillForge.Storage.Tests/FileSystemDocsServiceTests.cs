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
}
