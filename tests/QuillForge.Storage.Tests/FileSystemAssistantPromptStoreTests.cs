using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Storage.FileSystem;

namespace QuillForge.Storage.Tests;

public sealed class FileSystemAssistantPromptStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemAssistantPromptStore _store;

    public FileSystemAssistantPromptStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"quillforge-assistant-prompt-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new FileSystemAssistantPromptStore(
            _tempDir,
            NullLogger<FileSystemAssistantPromptStore>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadAsync_ExistingPrompt_ReturnsContent()
    {
        File.WriteAllText(Path.Combine(_tempDir, "default.md"), "You are a helpful interface.");

        var content = await _store.LoadAsync("default");

        Assert.Equal("You are a helpful interface.", content);
    }

    [Fact]
    public async Task LoadAsync_MissingPrompt_ReturnsEmptyLayer()
    {
        var content = await _store.LoadAsync("nonexistent");

        Assert.Equal(string.Empty, content);
    }

    [Fact]
    public async Task LoadAsync_PathTraversal_ReturnsEmptyLayer()
    {
        var parentDir = Path.GetDirectoryName(_tempDir)!;
        var secretFile = Path.Combine(parentDir, "secret.md");
        File.WriteAllText(secretFile, "Secret prompt content.");

        try
        {
            var content = await _store.LoadAsync("../secret");

            Assert.Equal(string.Empty, content);
        }
        finally
        {
            File.Delete(secretFile);
        }
    }

    [Fact]
    public async Task ListAsync_ReturnsPromptNames()
    {
        File.WriteAllText(Path.Combine(_tempDir, "default.md"), "Default prompt.");
        File.WriteAllText(Path.Combine(_tempDir, "friendly.md"), "Friendly prompt.");

        var names = await _store.ListAsync();

        Assert.Equal(2, names.Count);
        Assert.Contains("default", names);
        Assert.Contains("friendly", names);
    }

    [Fact]
    public async Task ListAsync_NonexistentDirectory_ReturnsEmpty()
    {
        var store = new FileSystemAssistantPromptStore(
            "/nonexistent/path",
            NullLogger<FileSystemAssistantPromptStore>.Instance);

        var names = await store.ListAsync();

        Assert.Empty(names);
    }
}
