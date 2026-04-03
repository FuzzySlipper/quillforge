using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Storage.Configuration;

namespace QuillForge.Storage.Tests;

public class AppConfigStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly Den.Persistence.AtomicFileWriter _writer;

    public AppConfigStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "appconfig-store-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _writer = new Den.Persistence.AtomicFileWriter(NullLogger<Den.Persistence.AtomicFileWriter>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    private AppConfigStore CreateStore()
        => new(_tempDir, _writer, NullLogger<AppConfigStore>.Instance);

    // --- Defaults ---

    [Fact]
    public async Task Load_WhenNoFile_ReturnsDefaults()
    {
        var store = CreateStore();

        var config = await store.LoadAsync();

        Assert.Equal("default", config.Models.Orchestrator);
        Assert.Equal("default", config.Profiles.Default);
        Assert.Equal(7.0, config.Forge.ReviewPassThreshold);
        Assert.Equal(3, config.Forge.MaxRevisions);
        Assert.Equal(6000, config.Persona.MaxTokens);
    }

    // --- Round-trip ---

    [Fact]
    public async Task SaveAndLoad_RoundTrips()
    {
        var store = CreateStore();
        var config = new AppConfig
        {
            Models = new ModelsConfig { Orchestrator = "claude-opus" },
            Forge = new ForgeConfig { ReviewPassThreshold = 8.5, MaxRevisions = 5 },
        };

        await store.SaveAsync(config);
        var loaded = await store.LoadAsync();

        Assert.Equal("claude-opus", loaded.Models.Orchestrator);
        Assert.Equal(8.5, loaded.Forge.ReviewPassThreshold);
        Assert.Equal(5, loaded.Forge.MaxRevisions);
    }

    // --- Normalization ---

    [Fact]
    public async Task Load_NormalizesOutOfRangeValues()
    {
        // Write a config with out-of-range values directly to disk
        var configPath = Path.Combine(_tempDir, "config.yaml");
        await File.WriteAllTextAsync(configPath, """
            forge:
              review_pass_threshold: 99
              max_revisions: -5
              stage_timeout_minutes: 0
            persona:
              max_tokens: 10
            agents:
              orchestrator:
                max_tool_rounds: 0
              librarian:
                max_tokens: 50
              council:
                temperature: 5.0
            timeouts:
              tool_execution_seconds: 1
              provider_http_seconds: 0
              update_check_hours: 0
            """);

        var store = CreateStore();
        var config = await store.LoadAsync();

        Assert.Equal(10, config.Forge.ReviewPassThreshold); // clamped to max
        Assert.Equal(0, config.Forge.MaxRevisions); // clamped to min 0
        Assert.Equal(1, config.Forge.StageTimeoutMinutes); // clamped to min 1
        Assert.Equal(100, config.Persona.MaxTokens); // clamped to min 100
        Assert.Equal(1, config.Agents.Orchestrator.MaxToolRounds); // clamped to min 1
        Assert.Equal(256, config.Agents.Librarian.MaxTokens); // clamped to min 256
        Assert.Equal(2.0, config.Agents.Council.Temperature); // clamped to max 2
        Assert.Equal(5, config.Timeouts.ToolExecutionSeconds); // clamped to min 5
        Assert.Equal(1, config.Timeouts.ProviderHttpSeconds); // clamped to min 1
        Assert.Equal(1, config.Timeouts.UpdateCheckHours); // clamped to min 1
    }

    [Fact]
    public async Task Save_NormalizesBeforePersist()
    {
        var store = CreateStore();
        await store.SaveAsync(new AppConfig
        {
            Forge = new ForgeConfig { ReviewPassThreshold = -1 }
        });

        var store2 = CreateStore();
        var reloaded = await store2.LoadAsync();

        Assert.Equal(1, reloaded.Forge.ReviewPassThreshold); // clamped on save
    }

    // --- Update semantics ---

    [Fact]
    public async Task Update_AppliesFunctionAndPersists()
    {
        var store = CreateStore();
        await store.SaveAsync(new AppConfig
        {
            Models = new ModelsConfig { Orchestrator = "original" }
        });

        var result = await store.UpdateAsync(c => c with
        {
            Models = new ModelsConfig { Orchestrator = "updated" }
        });

        Assert.Equal("updated", result.Models.Orchestrator);

        // Verify persistence with a fresh store instance
        var store2 = CreateStore();
        var reloaded = await store2.LoadAsync();
        Assert.Equal("updated", reloaded.Models.Orchestrator);
    }

    // --- File location ---

    [Fact]
    public async Task Save_WritesToConfigYaml()
    {
        var store = CreateStore();
        await store.SaveAsync(new AppConfig());

        Assert.True(File.Exists(Path.Combine(_tempDir, "config.yaml")));
    }
}
