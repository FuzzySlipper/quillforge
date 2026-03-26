using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Storage.Configuration;

namespace QuillForge.Storage.Tests;

public class ConfigurationLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigurationLoader _loader;

    public ConfigurationLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "quillforge-config-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _loader = new ConfigurationLoader(NullLoggerFactory.Instance.CreateLogger<ConfigurationLoader>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        var config = _loader.Load(Path.Combine(_tempDir, "nonexistent.yaml"));

        Assert.Equal("default", config.Models.Orchestrator);
        Assert.Equal("default", config.Lore.Active);
        Assert.Equal(7.0, config.Forge.ReviewPassThreshold);
    }

    [Fact]
    public void ValidYaml_ParsesCorrectly()
    {
        var yaml = """
            models:
              orchestrator: claude
              prose_writer: gpt-4o
            lore:
              active: fantasy-world
            forge:
              review_pass_threshold: 8.0
              max_revisions: 5
            web_search:
              enabled: true
              provider: searxng
              searxng_url: http://localhost:8080
            """;

        var path = Path.Combine(_tempDir, "config.yaml");
        File.WriteAllText(path, yaml);

        var config = _loader.Load(path);

        Assert.Equal("claude", config.Models.Orchestrator);
        Assert.Equal("gpt-4o", config.Models.ProseWriter);
        Assert.Equal("fantasy-world", config.Lore.Active);
        Assert.Equal(8.0, config.Forge.ReviewPassThreshold);
        Assert.Equal(5, config.Forge.MaxRevisions);
        Assert.True(config.WebSearch.Enabled);
        Assert.Equal("http://localhost:8080", config.WebSearch.SearxngUrl);
    }

    [Fact]
    public void PartialYaml_MergesWithDefaults()
    {
        var yaml = """
            models:
              orchestrator: claude
            """;

        var path = Path.Combine(_tempDir, "config.yaml");
        File.WriteAllText(path, yaml);

        var config = _loader.Load(path);

        Assert.Equal("claude", config.Models.Orchestrator);
        // Unspecified fields should have defaults
        Assert.Equal("default", config.Models.ProseWriter);
        Assert.Equal("default", config.Lore.Active);
    }

    [Fact]
    public void MalformedYaml_ReturnsDefaults()
    {
        var path = Path.Combine(_tempDir, "config.yaml");
        File.WriteAllText(path, ": this is not valid yaml [[[");

        var config = _loader.Load(path);

        // Should fall back to defaults without crashing
        Assert.Equal("default", config.Models.Orchestrator);
    }

    [Fact]
    public void WriteDefaults_CreatesValidFile()
    {
        var path = Path.Combine(_tempDir, "config.yaml");
        _loader.WriteDefaults(path);

        Assert.True(File.Exists(path));

        // Should be loadable
        var config = _loader.Load(path);
        Assert.Equal("default", config.Models.Orchestrator);
    }

    [Fact]
    public void FirstRunSetup_CreatesDirectoryStructure()
    {
        var contentRoot = Path.Combine(_tempDir, "build");
        var setup = new FirstRunSetup(NullLoggerFactory.Instance.CreateLogger<FirstRunSetup>());

        var isFirstRun = setup.EnsureContentDirectory(contentRoot);

        Assert.True(isFirstRun);
        Assert.True(Directory.Exists(Path.Combine(contentRoot, "lore", "default")));
        Assert.True(Directory.Exists(Path.Combine(contentRoot, "persona")));
        Assert.True(Directory.Exists(Path.Combine(contentRoot, "data", "sessions")));
        Assert.True(File.Exists(Path.Combine(contentRoot, "persona", "default.md")));
        Assert.True(File.Exists(Path.Combine(contentRoot, "writing-styles", "default.md")));
        Assert.True(File.Exists(Path.Combine(contentRoot, "config.yaml")));
    }

    [Fact]
    public void FirstRunSetup_SecondRun_ReturnsFalse()
    {
        var contentRoot = Path.Combine(_tempDir, "build");
        var setup = new FirstRunSetup(NullLoggerFactory.Instance.CreateLogger<FirstRunSetup>());

        setup.EnsureContentDirectory(contentRoot);
        var isSecondRun = setup.EnsureContentDirectory(contentRoot);

        Assert.False(isSecondRun);
    }
}
