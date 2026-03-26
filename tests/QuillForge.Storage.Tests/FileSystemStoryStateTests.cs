using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Storage.FileSystem;
using QuillForge.Storage.Utilities;

namespace QuillForge.Storage.Tests;

public class FileSystemStoryStateTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemStoryStateService _service;

    public FileSystemStoryStateTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "quillforge-state-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        var writer = new AtomicFileWriter(NullLoggerFactory.Instance.CreateLogger<AtomicFileWriter>());
        _service = new FileSystemStoryStateService(_tempDir, writer,
            NullLoggerFactory.Instance.CreateLogger<FileSystemStoryStateService>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task Load_MissingFile_ReturnsEmpty()
    {
        var state = await _service.LoadAsync("nonexistent.state.yaml");
        Assert.Empty(state);
    }

    [Fact]
    public async Task SaveAndLoad_RoundTrip()
    {
        var state = new Dictionary<string, object>
        {
            ["tension"] = "high",
            ["_event_counter"] = 5,
        };

        await _service.SaveAsync("test.state.yaml", state);
        var loaded = await _service.LoadAsync("test.state.yaml");

        Assert.Equal("high", loaded["tension"].ToString());
    }

    [Fact]
    public async Task Merge_AddsNewKeys()
    {
        await _service.SaveAsync("test.state.yaml", new Dictionary<string, object>
        {
            ["existing"] = "value",
        });

        var merged = await _service.MergeAsync("test.state.yaml", new Dictionary<string, object>
        {
            ["new_key"] = "new_value",
        });

        Assert.Equal("value", merged["existing"].ToString());
        Assert.Equal("new_value", merged["new_key"].ToString());
    }

    [Fact]
    public async Task Merge_OverwritesExistingKeys()
    {
        await _service.SaveAsync("test.state.yaml", new Dictionary<string, object>
        {
            ["mood"] = "calm",
        });

        var merged = await _service.MergeAsync("test.state.yaml", new Dictionary<string, object>
        {
            ["mood"] = "tense",
        });

        Assert.Equal("tense", merged["mood"].ToString());
    }

    [Fact]
    public async Task IncrementCounter_FromZero()
    {
        await _service.IncrementCounterAsync("test.state.yaml", "_event_counter");

        var state = await _service.LoadAsync("test.state.yaml");
        Assert.Equal(1, int.Parse(state["_event_counter"].ToString()!));
    }

    [Fact]
    public async Task IncrementCounter_FromExisting()
    {
        await _service.SaveAsync("test.state.yaml", new Dictionary<string, object>
        {
            ["_event_counter"] = 10,
        });

        await _service.IncrementCounterAsync("test.state.yaml", "_event_counter");

        var state = await _service.LoadAsync("test.state.yaml");
        Assert.Equal(11, int.Parse(state["_event_counter"].ToString()!));
    }

    [Fact]
    public async Task RemoveKey_DeletesIt()
    {
        await _service.SaveAsync("test.state.yaml", new Dictionary<string, object>
        {
            ["keep"] = "yes",
            ["remove"] = "this",
        });

        await _service.RemoveKeyAsync("test.state.yaml", "remove");

        var state = await _service.LoadAsync("test.state.yaml");
        Assert.True(state.ContainsKey("keep"));
        Assert.False(state.ContainsKey("remove"));
    }

    [Fact]
    public async Task RemoveKey_NonExistent_NoError()
    {
        await _service.SaveAsync("test.state.yaml", new Dictionary<string, object>
        {
            ["only"] = "key",
        });

        // Should not throw
        await _service.RemoveKeyAsync("test.state.yaml", "does_not_exist");

        var state = await _service.LoadAsync("test.state.yaml");
        Assert.Single(state);
    }

    [Fact]
    public async Task BackwardsCompatible_WithPythonYaml()
    {
        // Write a YAML file in the format the Python version produces
        var yaml = """
            tension_level: high
            _event_counter: 42
            plot_threads:
              dragon_quest: active
              romance: simmering
            character_conditions:
              elena: wounded
              marcus: suspicious
            """;

        var path = Path.Combine(_tempDir, "compat.state.yaml");
        await File.WriteAllTextAsync(path, yaml);

        var state = await _service.LoadAsync("compat.state.yaml");

        Assert.Equal("high", state["tension_level"].ToString());
        Assert.True(state.ContainsKey("plot_threads"));
        Assert.True(state.ContainsKey("character_conditions"));
    }
}
