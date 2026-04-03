using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Storage.FileSystem;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.Tests;

public sealed class FileSystemPlotStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemPlotStore _store;

    public FileSystemPlotStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"quillforge-plots-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _store = new FileSystemPlotStore(
            _tempDir,
            new AtomicFileWriter(NullLogger<AtomicFileWriter>.Instance),
            NullLogger<FileSystemPlotStore>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAndLoad_RoundTripsPlotMarkdown()
    {
        await _store.SaveAsync("heist-arc", "# Heist Arc\n\n## Premise\nSteal the ledger.");

        var content = await _store.LoadAsync("heist-arc");

        Assert.Contains("Heist Arc", content);
    }

    [Fact]
    public async Task ListAsync_ReturnsStoredPlotNamesWithoutExtensions()
    {
        await _store.SaveAsync("storm-arc", "# Storm Arc");
        await _store.SaveAsync("court-arc.md", "# Court Arc");

        var names = await _store.ListAsync();

        Assert.Contains("storm-arc", names);
        Assert.Contains("court-arc", names);
    }
}
