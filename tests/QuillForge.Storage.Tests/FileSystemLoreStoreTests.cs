using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Storage.FileSystem;

namespace QuillForge.Storage.Tests;

public class FileSystemLoreStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemLoreStore _store;

    public FileSystemLoreStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "quillforge-lore-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _store = new FileSystemLoreStore(_tempDir, NullLoggerFactory.Instance.CreateLogger<FileSystemLoreStore>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    private void CreateLoreFile(string loreSet, string relativePath, string content)
    {
        var path = Path.Combine(_tempDir, loreSet, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public async Task LoadLoreSet_ReturnsAllMarkdownFiles()
    {
        CreateLoreFile("fantasy", "characters/elena.md", "Elena is a warrior.");
        CreateLoreFile("fantasy", "locations/castle.md", "The castle stands tall.");

        var lore = await _store.LoadLoreSetAsync("fantasy");

        Assert.Equal(2, lore.Count);
        Assert.Contains("characters/elena.md", lore.Keys);
        Assert.Equal("Elena is a warrior.", lore["characters/elena.md"]);
    }

    [Fact]
    public async Task LoadLoreSet_MissingDirectory_ReturnsEmpty()
    {
        var lore = await _store.LoadLoreSetAsync("nonexistent");
        Assert.Empty(lore);
    }

    [Fact]
    public async Task ListLoreSets_ReturnsDirectoryNames()
    {
        CreateLoreFile("fantasy", "test.md", "x");
        CreateLoreFile("scifi", "test.md", "y");

        var sets = await _store.ListLoreSetsAsync();

        Assert.Equal(2, sets.Count);
        Assert.Contains("fantasy", sets);
        Assert.Contains("scifi", sets);
    }

    [Fact]
    public async Task Search_FindsMatchingContent()
    {
        CreateLoreFile("world", "people.md", "Elena carries a silver blade.");
        CreateLoreFile("world", "places.md", "The forest is dark and deep.");

        var results = await _store.SearchAsync("world", "silver blade");

        Assert.Single(results);
        Assert.Equal("people.md", results[0].FilePath);
        Assert.Contains("silver blade", results[0].Snippet);
    }

    [Fact]
    public async Task Search_CaseInsensitive()
    {
        CreateLoreFile("world", "test.md", "The Dragon sleeps.");

        var results = await _store.SearchAsync("world", "dragon");

        Assert.Single(results);
    }
}
