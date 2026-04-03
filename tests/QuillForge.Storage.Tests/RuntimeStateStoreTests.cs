using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core;
using QuillForge.Storage.FileSystem;

namespace QuillForge.Storage.Tests;

public class RuntimeStateStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Den.Persistence.AtomicFileWriter _writer;

    public RuntimeStateStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "runtime-state-store-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _writer = new Den.Persistence.AtomicFileWriter(NullLogger<Den.Persistence.AtomicFileWriter>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    private RuntimeStateStore CreateStore()
        => new(_tempDir, _writer, NullLogger<RuntimeStateStore>.Instance);

    [Fact]
    public void Load_WhenNoFile_ReturnsDefaults()
    {
        var store = CreateStore();

        var state = store.Load();

        Assert.Null(state.LastMode);
        Assert.Null(state.LastProject);
        Assert.Null(state.LastFile);
        Assert.Null(state.LastCharacter);
        Assert.Null(state.LastSessionId);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var store = CreateStore();
        var sessionId = Guid.NewGuid();

        await store.SaveAsync(new RuntimeState
        {
            LastMode = "writer",
            LastProject = "novel",
            LastFile = "chapter1.md",
            LastCharacter = "Archivist",
            LastSessionId = sessionId,
        });

        var loaded = CreateStore().Load();

        Assert.Equal("writer", loaded.LastMode);
        Assert.Equal("novel", loaded.LastProject);
        Assert.Equal("chapter1.md", loaded.LastFile);
        Assert.Equal("Archivist", loaded.LastCharacter);
        Assert.Equal(sessionId, loaded.LastSessionId);
    }

    [Fact]
    public async Task Update_AppliesFunctionAndPersists()
    {
        var store = CreateStore();
        await store.SaveAsync(new RuntimeState
        {
            LastMode = "general",
            LastProject = "alpha",
        });

        var result = await store.UpdateAsync(current => new RuntimeState
        {
            LastMode = "council",
            LastProject = current.LastProject,
            LastFile = "brief.md",
            LastCharacter = "Scribe",
            LastSessionId = Guid.NewGuid(),
        });

        Assert.Equal("council", result.LastMode);
        Assert.Equal("alpha", result.LastProject);
        Assert.Equal("brief.md", result.LastFile);
        Assert.Equal("Scribe", result.LastCharacter);
        Assert.NotNull(result.LastSessionId);

        var reloaded = CreateStore().Load();
        Assert.Equal("council", reloaded.LastMode);
        Assert.Equal("alpha", reloaded.LastProject);
        Assert.Equal("brief.md", reloaded.LastFile);
        Assert.Equal("Scribe", reloaded.LastCharacter);
        Assert.Equal(result.LastSessionId, reloaded.LastSessionId);
    }

    [Fact]
    public async Task Save_WritesToLegacyRuntimeStatePath()
    {
        var store = CreateStore();

        await store.SaveAsync(new RuntimeState { LastMode = "general" });

        Assert.True(File.Exists(Path.Combine(_tempDir, ContentPaths.RuntimeStateFile)));
    }
}
