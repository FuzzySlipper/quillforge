using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Storage.FileSystem;

namespace QuillForge.Storage.Tests;

public sealed class FileSystemLibrarianPromptStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemLibrarianPromptStore _store;

    public FileSystemLibrarianPromptStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"quillforge-librarian-prompt-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new FileSystemLibrarianPromptStore(_tempDir,
            NullLoggerFactory.Instance.CreateLogger<FileSystemLibrarianPromptStore>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_ExistingPrompt_ReturnsContent()
    {
        File.WriteAllText(Path.Combine(_tempDir, "default.md"), "You are a librarian.");

        var content = await _store.LoadAsync("default");

        Assert.Equal("You are a librarian.", content);
    }

    [Fact]
    public async Task LoadAsync_MissingPrompt_FallsBackToBakedInDefaultInstructions()
    {
        var content = await _store.LoadAsync("nonexistent");

        Assert.Contains("Search the entire lore corpus thoroughly before responding.", content);
    }

    [Fact]
    public async Task LoadAsync_PathTraversal_FallsBackToBakedInDefaultInstructions()
    {
        var parentDir = Path.GetDirectoryName(_tempDir)!;
        var secretFile = Path.Combine(parentDir, "secret.md");
        File.WriteAllText(secretFile, "Secret prompt content.");
        try
        {
            var content = await _store.LoadAsync("../secret");
            Assert.Contains("Search the entire lore corpus thoroughly before responding.", content);
            Assert.DoesNotContain("Secret prompt content.", content);
        }
        finally
        {
            File.Delete(secretFile);
        }
    }

    [Fact]
    public async Task LoadAsync_DeepTraversal_FallsBackToBakedInDefaultInstructions()
    {
        var content = await _store.LoadAsync("../../etc/passwd");

        Assert.Contains("Search the entire lore corpus thoroughly before responding.", content);
    }

    [Fact]
    public async Task LoadAsync_CaseOnlySiblingTraversal_FallsBackToBakedInDefaultInstructions()
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
        File.WriteAllText(secretFile, "Sibling prompt content.");

        try
        {
            var content = await _store.LoadAsync("../" + siblingName + "/secret");

            Assert.Contains("Search the entire lore corpus thoroughly before responding.", content);
            Assert.DoesNotContain("Sibling prompt content.", content);
        }
        finally
        {
            Directory.Delete(siblingDir, recursive: true);
        }
    }

    [Fact]
    public async Task ListAsync_ReturnsPromptNames()
    {
        File.WriteAllText(Path.Combine(_tempDir, "default.md"), "Default prompt.");
        File.WriteAllText(Path.Combine(_tempDir, "custom.md"), "Custom prompt.");

        var names = await _store.ListAsync();

        Assert.Equal(2, names.Count);
        Assert.Contains("custom", names);
        Assert.Contains("default", names);
    }

    [Fact]
    public async Task ListAsync_NonexistentDirectory_ReturnsEmpty()
    {
        var store = new FileSystemLibrarianPromptStore("/nonexistent/path",
            NullLoggerFactory.Instance.CreateLogger<FileSystemLibrarianPromptStore>());

        var names = await store.ListAsync();

        Assert.Empty(names);
    }
}
