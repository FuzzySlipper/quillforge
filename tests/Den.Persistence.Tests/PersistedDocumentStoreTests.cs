using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json.Nodes;

namespace Den.Persistence.Tests;

/// <summary>
/// A simple record used as the persisted document model in tests.
/// </summary>
public sealed record TestConfig
{
    public string Name { get; init; } = "";
    public int MaxRetries { get; init; }
    public bool Enabled { get; init; }
}

public sealed record VersionedConfig
{
    public string DisplayName { get; init; } = "";
    public int MaxRetries { get; init; }
    public bool Enabled { get; init; }
}

/// <summary>
/// Minimal document definition using only defaults from the base class.
/// </summary>
public sealed class TestConfigDocument : PersistedDocumentBase<TestConfig>
{
    public override string RelativePath => "data/test-config.json";

    public override TestConfig CreateDefault() => new()
    {
        Name = "default",
        MaxRetries = 3,
        Enabled = true
    };
}

/// <summary>
/// Document definition with custom normalization and validation.
/// </summary>
public sealed class ValidatedConfigDocument : PersistedDocumentBase<TestConfig>
{
    public override string RelativePath => "data/validated-config.json";

    public override TestConfig CreateDefault() => new()
    {
        Name = "default",
        MaxRetries = 3,
        Enabled = true
    };

    public override TestConfig Normalize(TestConfig value) => value with
    {
        Name = string.IsNullOrWhiteSpace(value.Name) ? "default" : value.Name.Trim(),
        MaxRetries = Math.Clamp(value.MaxRetries, 0, 100)
    };

    public override void ThrowIfInvalid(TestConfig value)
    {
        if (value.Name.Length > 200)
            throw new InvalidOperationException("Name must be 200 characters or fewer.");
    }
}

/// <summary>
/// YAML variant of the test document for cross-format testing.
/// </summary>
public sealed class YamlTestConfigDocument : PersistedDocumentBase<TestConfig>
{
    public override string RelativePath => "data/test-config.yaml";

    public override TestConfig CreateDefault() => new()
    {
        Name = "yaml-default",
        MaxRetries = 5,
        Enabled = true
    };
}

public sealed class JsonVersionedConfigDocument : PersistedDocumentBase<VersionedConfig>, IVersionedPersistedDocument<VersionedConfig>
{
    public override string RelativePath => "data/versioned-config.json";

    public int CurrentVersion => 2;

    public override VersionedConfig CreateDefault() => new()
    {
        DisplayName = "default",
        MaxRetries = 3,
        Enabled = true
    };

    public void MigrateOneVersion(JsonObject document, int fromVersion)
    {
        if (fromVersion != 1)
        {
            throw new InvalidOperationException($"Unsupported migration from version {fromVersion}.");
        }

        if (document["name"] is not null)
        {
            document["displayName"] = document["name"]?.DeepClone();
            document.Remove("name");
        }
    }
}

public sealed class YamlVersionedConfigDocument : PersistedDocumentBase<VersionedConfig>, IVersionedPersistedDocument<VersionedConfig>
{
    public override string RelativePath => "data/versioned-config.yaml";

    public int CurrentVersion => 2;

    public string VersionFieldName => "schema_version";

    public override VersionedConfig CreateDefault() => new()
    {
        DisplayName = "default",
        MaxRetries = 3,
        Enabled = true
    };

    public void MigrateOneVersion(JsonObject document, int fromVersion)
    {
        if (fromVersion != 1)
        {
            throw new InvalidOperationException($"Unsupported migration from version {fromVersion}.");
        }

        if (document["name"] is not null)
        {
            document["display_name"] = document["name"]?.DeepClone();
            document.Remove("name");
        }
    }
}

public class PersistedDocumentStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AtomicFileWriter _writer;

    public PersistedDocumentStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "den-persistence-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _writer = new AtomicFileWriter(NullLogger<AtomicFileWriter>.Instance);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private JsonPersistedDocumentStore<TestConfig> CreateJsonStore(IPersistedDocument<TestConfig>? document = null)
        => new(
            document ?? new TestConfigDocument(),
            _tempDir,
            _writer,
            NullLogger.Instance);

    private YamlPersistedDocumentStore<TestConfig> CreateYamlStore(IPersistedDocument<TestConfig>? document = null)
        => new(
            document ?? new YamlTestConfigDocument(),
            _tempDir,
            _writer,
            NullLogger.Instance);

    private JsonPersistedDocumentStore<VersionedConfig> CreateVersionedJsonStore(IPersistedDocument<VersionedConfig>? document = null)
        => new(
            document ?? new JsonVersionedConfigDocument(),
            _tempDir,
            _writer,
            NullLogger.Instance);

    private YamlPersistedDocumentStore<VersionedConfig> CreateVersionedYamlStore(IPersistedDocument<VersionedConfig>? document = null)
        => new(
            document ?? new YamlVersionedConfigDocument(),
            _tempDir,
            _writer,
            NullLogger.Instance);

    // --- Load defaults ---

    [Fact]
    public async Task Load_WhenFileDoesNotExist_ReturnsDefaults()
    {
        var store = CreateJsonStore();

        var result = await store.LoadAsync();

        Assert.Equal("default", result.Name);
        Assert.Equal(3, result.MaxRetries);
        Assert.True(result.Enabled);
    }

    [Fact]
    public async Task Load_Yaml_WhenFileDoesNotExist_ReturnsDefaults()
    {
        var store = CreateYamlStore();

        var result = await store.LoadAsync();

        Assert.Equal("yaml-default", result.Name);
        Assert.Equal(5, result.MaxRetries);
        Assert.True(result.Enabled);
    }

    // --- Save and reload round-trip ---

    [Fact]
    public async Task SaveAndLoad_Json_RoundTrips()
    {
        var store = CreateJsonStore();
        var config = new TestConfig { Name = "saved", MaxRetries = 7, Enabled = false };

        await store.SaveAsync(config);
        var loaded = await store.LoadAsync();

        Assert.Equal("saved", loaded.Name);
        Assert.Equal(7, loaded.MaxRetries);
        Assert.False(loaded.Enabled);
    }

    [Fact]
    public async Task SaveAndLoad_Yaml_RoundTrips()
    {
        var store = CreateYamlStore();
        var config = new TestConfig { Name = "saved", MaxRetries = 7, Enabled = false };

        await store.SaveAsync(config);
        var loaded = await store.LoadAsync();

        Assert.Equal("saved", loaded.Name);
        Assert.Equal(7, loaded.MaxRetries);
        Assert.False(loaded.Enabled);
    }

    // --- Update semantics ---

    [Fact]
    public async Task Update_AppliesFunctionAndPersists()
    {
        var store = CreateJsonStore();
        await store.SaveAsync(new TestConfig { Name = "original", MaxRetries = 1, Enabled = true });

        var result = await store.UpdateAsync(c => c with { Name = "updated", MaxRetries = 10 });

        Assert.Equal("updated", result.Name);
        Assert.Equal(10, result.MaxRetries);

        // Verify it persisted by creating a new store instance over the same file
        var store2 = CreateJsonStore();
        var reloaded = await store2.LoadAsync();
        Assert.Equal("updated", reloaded.Name);
        Assert.Equal(10, reloaded.MaxRetries);
    }

    [Fact]
    public async Task Update_OnMissingFile_LoadsDefaultsThenApplies()
    {
        var store = CreateJsonStore();

        var result = await store.UpdateAsync(c => c with { Name = "from-default" });

        Assert.Equal("from-default", result.Name);
        Assert.Equal(3, result.MaxRetries); // default value preserved
    }

    // --- Normalization ---

    [Fact]
    public async Task Load_AppliesNormalization()
    {
        var document = new ValidatedConfigDocument();
        var store = CreateJsonStore(document);

        // Write a file with denormalized data
        var path = Path.Combine(_tempDir, document.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """{"name": "  padded  ", "maxRetries": 500, "enabled": true}""");

        var loaded = await store.LoadAsync();

        Assert.Equal("padded", loaded.Name); // trimmed
        Assert.Equal(100, loaded.MaxRetries); // clamped to max
    }

    [Fact]
    public async Task Save_AppliesNormalizationBeforePersist()
    {
        var document = new ValidatedConfigDocument();
        var store = CreateJsonStore(document);

        await store.SaveAsync(new TestConfig { Name = "  spaced  ", MaxRetries = -5, Enabled = true });

        var store2 = CreateJsonStore(document);
        var reloaded = await store2.LoadAsync();

        Assert.Equal("spaced", reloaded.Name);
        Assert.Equal(0, reloaded.MaxRetries); // clamped to min
    }

    // --- Validation ---

    [Fact]
    public async Task Save_ThrowsOnInvalidValue()
    {
        var document = new ValidatedConfigDocument();
        var store = CreateJsonStore(document);

        var invalid = new TestConfig { Name = new string('x', 201), MaxRetries = 1, Enabled = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(invalid));
    }

    [Fact]
    public async Task Update_ThrowsOnInvalidValue()
    {
        var document = new ValidatedConfigDocument();
        var store = CreateJsonStore(document);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.UpdateAsync(_ => new TestConfig { Name = new string('x', 201), MaxRetries = 1, Enabled = true }));
    }

    // --- Corrupt file handling ---

    [Fact]
    public async Task Load_WithCorruptFile_ReturnsDefaults()
    {
        var document = new TestConfigDocument();
        var path = Path.Combine(_tempDir, document.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, "this is not valid json {{{");

        var store = CreateJsonStore(document);
        var loaded = await store.LoadAsync();

        Assert.Equal("default", loaded.Name);
        Assert.Equal(3, loaded.MaxRetries);
    }

    // --- Atomic writes ---

    [Fact]
    public async Task Save_CreatesParentDirectories()
    {
        var store = CreateJsonStore();
        var config = new TestConfig { Name = "test", MaxRetries = 1, Enabled = true };

        await store.SaveAsync(config);

        var path = Path.Combine(_tempDir, "data/test-config.json");
        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Save_DoesNotLeaveOrphanedTempFiles()
    {
        var store = CreateJsonStore();
        await store.SaveAsync(new TestConfig { Name = "test", MaxRetries = 1, Enabled = true });

        var dataDir = Path.Combine(_tempDir, "data");
        var tempFiles = Directory.GetFiles(dataDir, "*.tmp.*");
        Assert.Empty(tempFiles);
    }

    // --- Concurrent access ---

    [Fact]
    public async Task ConcurrentUpdates_DoNotLoseWrites()
    {
        var store = CreateJsonStore();
        await store.SaveAsync(new TestConfig { Name = "start", MaxRetries = 0, Enabled = true });

        const int updateCount = 50;
        var tasks = Enumerable.Range(0, updateCount)
            .Select(_ => store.UpdateAsync(c => c with { MaxRetries = c.MaxRetries + 1 }))
            .ToArray();

        await Task.WhenAll(tasks);

        var final = await store.LoadAsync();
        Assert.Equal(updateCount, final.MaxRetries);
    }

    // --- Schema versioning ---

    [Fact]
    public async Task Load_JsonVersionedDocument_MigratesLegacyShapeBeforeDeserialization()
    {
        var document = new JsonVersionedConfigDocument();
        var path = Path.Combine(_tempDir, document.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """{"name":"legacy-name","maxRetries":9,"enabled":false}""");

        var store = CreateVersionedJsonStore(document);
        var loaded = await store.LoadAsync();

        Assert.Equal("legacy-name", loaded.DisplayName);
        Assert.Equal(9, loaded.MaxRetries);
        Assert.False(loaded.Enabled);

        var migrated = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(2, migrated["schemaVersion"]!.GetValue<int>());
        Assert.Equal("legacy-name", migrated["displayName"]!.GetValue<string>());
        Assert.Null(migrated["name"]);
    }

    [Fact]
    public async Task Save_JsonVersionedDocument_WritesCurrentSchemaVersion()
    {
        var document = new JsonVersionedConfigDocument();
        var store = CreateVersionedJsonStore(document);

        await store.SaveAsync(new VersionedConfig
        {
            DisplayName = "current",
            MaxRetries = 4,
            Enabled = true
        });

        var path = Path.Combine(_tempDir, document.RelativePath);
        var saved = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(2, saved["schemaVersion"]!.GetValue<int>());
        Assert.Equal("current", saved["displayName"]!.GetValue<string>());
    }

    [Fact]
    public async Task Load_YamlVersionedDocument_MigratesLegacyShapeBeforeDeserialization()
    {
        var document = new YamlVersionedConfigDocument();
        var path = Path.Combine(_tempDir, document.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, """
            name: legacy-yaml
            max_retries: 12
            enabled: false
            """);

        var store = CreateVersionedYamlStore(document);
        var loaded = await store.LoadAsync();

        Assert.Equal("legacy-yaml", loaded.DisplayName);
        Assert.Equal(12, loaded.MaxRetries);
        Assert.False(loaded.Enabled);

        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("schema_version: 2", content);
        Assert.Contains("display_name: legacy-yaml", content);
        Assert.DoesNotContain(Environment.NewLine + "name: legacy-yaml", Environment.NewLine + content);
    }

    [Fact]
    public async Task Save_YamlVersionedDocument_WritesCurrentSchemaVersion()
    {
        var document = new YamlVersionedConfigDocument();
        var store = CreateVersionedYamlStore(document);

        await store.SaveAsync(new VersionedConfig
        {
            DisplayName = "current-yaml",
            MaxRetries = 6,
            Enabled = true
        });

        var path = Path.Combine(_tempDir, document.RelativePath);
        var content = await File.ReadAllTextAsync(path);
        Assert.Contains("schema_version: 2", content);
        Assert.Contains("display_name: current-yaml", content);
    }
}
