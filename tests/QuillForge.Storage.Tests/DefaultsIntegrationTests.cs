using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Storage.Configuration;
using QuillForge.Storage.FileSystem;

namespace QuillForge.Storage.Tests;

/// <summary>
/// Tests that verify the dev/defaults content loads correctly through the storage layer.
/// These use the actual default content files shipped with the project.
/// </summary>
[Trait("Category", "Integration")]
public class DefaultsIntegrationTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string? _defaultsPath;

    public DefaultsIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "quillforge-defaults-test-" + Guid.NewGuid().ToString("N")[..8]);

        // Find dev/defaults relative to the test assembly
        var searchPaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "dev", "defaults"),
            Path.Combine(Directory.GetCurrentDirectory(), "dev", "defaults"),
        };

        _defaultsPath = searchPaths.Select(Path.GetFullPath).FirstOrDefault(Directory.Exists);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void FirstRunSetup_CopiesDefaults()
    {
        if (_defaultsPath is null) return;

        var setup = new FirstRunSetup(NullLoggerFactory.Instance.CreateLogger<FirstRunSetup>());
        setup.EnsureContentDirectory(_tempDir, _defaultsPath);

        // Should have lore files
        Assert.True(File.Exists(Path.Combine(_tempDir, "lore", "default", "world-overview.md")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "lore", "default", "characters", "elena-vasquez.md")));

        // Should have persona files
        Assert.True(Directory.Exists(Path.Combine(_tempDir, "persona", "narrator")));

        // Should have writing styles
        Assert.True(File.Exists(Path.Combine(_tempDir, "writing-styles", "default.md")));
        Assert.True(File.Exists(Path.Combine(_tempDir, "writing-styles", "literary.md")));

        // Should have council advisors
        Assert.True(File.Exists(Path.Combine(_tempDir, "council", "analyst.md")));

        // Should have forge prompts
        Assert.True(File.Exists(Path.Combine(_tempDir, "forge-prompts", "planner.md")));

        // Should have layouts
        Assert.True(File.Exists(Path.Combine(_tempDir, "layouts", "default.md")));
    }

    [Fact]
    public async Task LoreStore_LoadsDefaultLoreSet()
    {
        if (_defaultsPath is null) return;

        var setup = new FirstRunSetup(NullLoggerFactory.Instance.CreateLogger<FirstRunSetup>());
        setup.EnsureContentDirectory(_tempDir, _defaultsPath);

        var store = new FileSystemLoreStore(
            Path.Combine(_tempDir, "lore"),
            NullLoggerFactory.Instance.CreateLogger<FileSystemLoreStore>());

        var loreSets = await store.ListLoreSetsAsync();
        Assert.Contains("default", loreSets);

        var lore = await store.LoadLoreSetAsync("default");
        Assert.True(lore.Count > 0);
        Assert.True(lore.ContainsKey("world-overview.md") || lore.Keys.Any(k => k.Contains("world-overview")));
    }

    [Fact]
    public async Task LoreStore_SearchFindsCharacter()
    {
        if (_defaultsPath is null) return;

        var setup = new FirstRunSetup(NullLoggerFactory.Instance.CreateLogger<FirstRunSetup>());
        setup.EnsureContentDirectory(_tempDir, _defaultsPath);

        var store = new FileSystemLoreStore(
            Path.Combine(_tempDir, "lore"),
            NullLoggerFactory.Instance.CreateLogger<FileSystemLoreStore>());

        var results = await store.SearchAsync("default", "Elena");
        Assert.True(results.Count > 0, "Should find Elena in the default lore set");
    }

    [Fact]
    public async Task WritingStyleStore_LoadsDefaultStyles()
    {
        if (_defaultsPath is null) return;

        var setup = new FirstRunSetup(NullLoggerFactory.Instance.CreateLogger<FirstRunSetup>());
        setup.EnsureContentDirectory(_tempDir, _defaultsPath);

        var store = new FileSystemWritingStyleStore(
            Path.Combine(_tempDir, "writing-styles"),
            NullLoggerFactory.Instance.CreateLogger<FileSystemWritingStyleStore>());

        var styles = await store.ListAsync();
        Assert.Contains("default", styles);
        Assert.Contains("literary", styles);
        Assert.Contains("pulp", styles);

        var defaultStyle = await store.LoadAsync("default");
        Assert.False(string.IsNullOrWhiteSpace(defaultStyle));
    }

    [Fact]
    public async Task PersonaStore_LoadsNarratorPersona()
    {
        if (_defaultsPath is null) return;

        var setup = new FirstRunSetup(NullLoggerFactory.Instance.CreateLogger<FirstRunSetup>());
        setup.EnsureContentDirectory(_tempDir, _defaultsPath);

        var store = new FileSystemPersonaStore(
            Path.Combine(_tempDir, "persona"),
            NullLoggerFactory.Instance.CreateLogger<FileSystemPersonaStore>());

        var personas = await store.ListAsync();
        Assert.True(personas.Count > 0);
    }

    [Fact]
    public void ConfigLoader_WorksWithDefaults()
    {
        if (_defaultsPath is null) return;

        var setup = new FirstRunSetup(NullLoggerFactory.Instance.CreateLogger<FirstRunSetup>());
        setup.EnsureContentDirectory(_tempDir, _defaultsPath);

        var loader = new ConfigurationLoader(
            NullLoggerFactory.Instance.CreateLogger<ConfigurationLoader>());
        var config = loader.Load(Path.Combine(_tempDir, "config.yaml"));

        // Should have sensible defaults
        Assert.NotNull(config);
        Assert.Equal("default", config.Models.Orchestrator);
    }
}
