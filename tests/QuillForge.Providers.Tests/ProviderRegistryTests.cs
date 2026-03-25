using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Providers.Registry;

namespace QuillForge.Providers.Tests;

public class ProviderRegistryTests
{
    private static ProviderRegistry CreateRegistry()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var factory = new ProviderFactory(loggerFactory.CreateLogger<ProviderFactory>());
        return new ProviderRegistry(factory,
            loggerFactory.CreateLogger<ProviderRegistry>(),
            loggerFactory);
    }

    [Fact]
    public void Register_AddsProvider()
    {
        var registry = CreateRegistry();
        registry.Register(new ProviderConfig
        {
            Alias = "test",
            Type = ProviderType.OpenAI,
            ApiKey = "sk-test",
        });

        var providers = registry.ListProviders();
        Assert.Single(providers);
        Assert.Equal("test", providers[0].Alias);
        Assert.Equal(ProviderType.OpenAI, providers[0].Type);
    }

    [Fact]
    public void Remove_DeletesProvider()
    {
        var registry = CreateRegistry();
        registry.Register(new ProviderConfig
        {
            Alias = "test",
            Type = ProviderType.OpenAI,
            ApiKey = "sk-test",
        });

        var removed = registry.Remove("test");
        Assert.True(removed);
        Assert.Empty(registry.ListProviders());
    }

    [Fact]
    public void Remove_NonExistent_ReturnsFalse()
    {
        var registry = CreateRegistry();
        Assert.False(registry.Remove("nonexistent"));
    }

    [Fact]
    public void GetCompletionService_ThrowsForUnknownAlias()
    {
        var registry = CreateRegistry();
        Assert.Throws<KeyNotFoundException>(() => registry.GetCompletionService("unknown"));
    }

    [Fact]
    public void GetConfig_ReturnsRegisteredConfig()
    {
        var registry = CreateRegistry();
        registry.Register(new ProviderConfig
        {
            Alias = "my-claude",
            Type = ProviderType.Anthropic,
            ApiKey = "sk-ant-test",
            DefaultModel = "claude-sonnet-4-20250514",
        });

        var config = registry.GetConfig("my-claude");
        Assert.NotNull(config);
        Assert.Equal(ProviderType.Anthropic, config.Type);
        Assert.Equal("claude-sonnet-4-20250514", config.DefaultModel);
    }

    [Fact]
    public void GetConfig_CaseInsensitive()
    {
        var registry = CreateRegistry();
        registry.Register(new ProviderConfig
        {
            Alias = "MyProvider",
            Type = ProviderType.OpenAI,
            ApiKey = "sk-test",
        });

        Assert.NotNull(registry.GetConfig("myprovider"));
        Assert.NotNull(registry.GetConfig("MYPROVIDER"));
    }

    [Fact]
    public void Register_OverwritesExisting()
    {
        var registry = CreateRegistry();
        registry.Register(new ProviderConfig
        {
            Alias = "test",
            Type = ProviderType.OpenAI,
            ApiKey = "old-key",
        });

        registry.Register(new ProviderConfig
        {
            Alias = "test",
            Type = ProviderType.Anthropic,
            ApiKey = "new-key",
        });

        var config = registry.GetConfig("test");
        Assert.Equal(ProviderType.Anthropic, config!.Type);
        Assert.Equal("new-key", config.ApiKey);
    }

    [Fact]
    public async Task Diagnostics_ReportsState()
    {
        var registry = CreateRegistry();
        registry.Register(new ProviderConfig
        {
            Alias = "claude",
            Type = ProviderType.Anthropic,
            ApiKey = "sk-test",
        });

        var diag = await registry.GetDiagnosticsAsync();
        Assert.Equal("providers", registry.Category);
        Assert.Equal(1, (int)diag["registered_count"]);
    }
}
