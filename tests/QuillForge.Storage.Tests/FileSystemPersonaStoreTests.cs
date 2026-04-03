using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Storage.FileSystem;

namespace QuillForge.Storage.Tests;

public sealed class FileSystemPersonaStoreTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemPersonaStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "quillforge-persona-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public async Task LoadAsync_PrefersConductorProfileOverLegacyPersona()
    {
        var conductorRoot = Path.Combine(_tempDir, "conductor");
        var legacyRoot = Path.Combine(_tempDir, "persona");
        Directory.CreateDirectory(conductorRoot);
        Directory.CreateDirectory(legacyRoot);

        await File.WriteAllTextAsync(Path.Combine(conductorRoot, "default.md"), "conductor-first");
        await File.WriteAllTextAsync(Path.Combine(legacyRoot, "default.md"), "legacy-persona");

        var store = new FileSystemPersonaStore(
            conductorRoot,
            legacyRoot,
            NullLogger<FileSystemPersonaStore>.Instance);

        var content = await store.LoadAsync("default");

        Assert.Equal("conductor-first", content);
    }

    [Fact]
    public async Task LoadAsync_FallsBackToLegacyPersonaWhenConductorProfileIsMissing()
    {
        var conductorRoot = Path.Combine(_tempDir, "conductor");
        var legacyRoot = Path.Combine(_tempDir, "persona");
        Directory.CreateDirectory(conductorRoot);
        Directory.CreateDirectory(legacyRoot);

        await File.WriteAllTextAsync(Path.Combine(legacyRoot, "editor.md"), "legacy-editor");

        var store = new FileSystemPersonaStore(
            conductorRoot,
            legacyRoot,
            NullLogger<FileSystemPersonaStore>.Instance);

        var content = await store.LoadAsync("editor");

        Assert.Equal("legacy-editor", content);
    }
}
