using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Storage.FileSystem;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.Tests;

public sealed class FileSystemCharacterCardStoreTests : IDisposable
{
    private readonly string _root;
    private readonly FileSystemCharacterCardStore _store;

    public FileSystemCharacterCardStoreTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"quillforge-character-cards-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _store = new FileSystemCharacterCardStore(
            _root,
            _root,
            new AtomicFileWriter(NullLogger<AtomicFileWriter>.Instance),
            NullLogger<FileSystemCharacterCardStore>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public async Task DeleteAsync_RemovesSavedCardFile()
    {
        await _store.SaveAsync("captain", new CharacterCard
        {
            Name = "Captain Rowan",
            Description = "An old naval officer.",
        });

        var deleted = await _store.DeleteAsync("captain");
        var loaded = await _store.LoadAsync("captain");

        Assert.True(deleted);
        Assert.Null(loaded);
        Assert.False(File.Exists(Path.Combine(_root, "captain.yaml")));
    }

    [Fact]
    public async Task DeleteAsync_ReturnsFalseForMissingCard()
    {
        var deleted = await _store.DeleteAsync("missing-card");

        Assert.False(deleted);
    }
}
