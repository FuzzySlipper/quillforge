using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Storage.FileSystem;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.Tests;

public sealed class FileSystemProfileConfigStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AtomicFileWriter _writer;

    public FileSystemProfileConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "quillforge-profile-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _writer = new AtomicFileWriter(NullLogger<AtomicFileWriter>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    private FileSystemProfileConfigStore CreateStore()
        => new(_tempDir, _writer, NullLogger<FileSystemProfileConfigStore>.Instance);

    [Fact]
    public async Task SaveAndLoadAsync_RoundTripsProfileConfig()
    {
        var store = CreateStore();
        var profile = new ProfileConfig
        {
            Conductor = "narrator",
            LoreSet = "fantasy",
            NarrativeRules = "default",
            WritingStyle = "literary",
            Roleplay = new RoleplayConfig
            {
                AiCharacter = "guide",
                UserCharacter = "author",
            },
        };

        await store.SaveAsync("authoring", profile);
        var loaded = await store.LoadAsync("authoring");

        Assert.Equal("narrator", loaded.Conductor);
        Assert.Equal("fantasy", loaded.LoreSet);
        Assert.Equal("default", loaded.NarrativeRules);
        Assert.Equal("literary", loaded.WritingStyle);
        Assert.Equal("guide", loaded.Roleplay.AiCharacter);
        Assert.Equal("author", loaded.Roleplay.UserCharacter);
    }

    [Fact]
    public async Task ListAsync_ReturnsSortedProfileIds()
    {
        var store = CreateStore();
        await store.SaveAsync("zeta", new ProfileConfig());
        await store.SaveAsync("alpha", new ProfileConfig());

        var profiles = await store.ListAsync();

        Assert.Equal(["alpha", "zeta"], profiles);
    }

    [Fact]
    public async Task SaveAsync_WritesYamlFileUnderProfilesDirectory()
    {
        var store = CreateStore();

        await store.SaveAsync("default", new ProfileConfig());

        Assert.True(File.Exists(Path.Combine(_tempDir, "profiles", "default.yaml")));
    }
}
