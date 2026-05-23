using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Providers.Registry;

namespace QuillForge.Providers.Tests;

public class ProviderRegistryTests
{
    private static ProviderRegistry CreateRegistry()
    {
        var loggerFactory = NullLoggerFactory.Instance;
        var factory = new ProviderFactory(loggerFactory.CreateLogger<ProviderFactory>());
        return new ProviderRegistry(factory, new AppConfig(),
            loggerFactory.CreateLogger<ProviderRegistry>(),
            loggerFactory);
    }

    [Fact]
    public void IsReasoningModel_DetectsDeepSeekReasonerAndThinkingVariants()
    {
        Assert.True(ProviderFactory.IsReasoningModel("deepseek-reasoner"));
        Assert.True(ProviderFactory.IsReasoningModel("deepseek-r1"));
        Assert.True(ProviderFactory.IsReasoningModel("deepseek-r1-distill"));
        Assert.True(ProviderFactory.IsReasoningModel("kimi-k2-preview"));
        Assert.True(ProviderFactory.IsReasoningModel("qwq-32b"));
        Assert.True(ProviderFactory.IsReasoningModel("o1-preview"));
        Assert.True(ProviderFactory.IsReasoningModel("o3-mini"));
        Assert.True(ProviderFactory.IsReasoningModel("claude-thinking"));
        Assert.False(ProviderFactory.IsReasoningModel("gpt-4o"));
        Assert.False(ProviderFactory.IsReasoningModel("llama3"));
        Assert.False(ProviderFactory.IsReasoningModel("claude-sonnet"));
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
    public void ResolveProviderAlias_MapsProviderTypeAliasToSingleRegisteredProvider()
    {
        var registry = CreateRegistry();
        registry.Register(new ProviderConfig
        {
            Alias = "sonnet",
            Type = ProviderType.Anthropic,
            ApiKey = "sk-ant-test",
            DefaultModel = "claude-sonnet-4-20250514",
        });

        var resolution = registry.ResolveProviderAlias("anthropic");

        Assert.True(resolution.IsResolved, resolution.Error);
        Assert.Equal("sonnet", resolution.ResolvedAlias);
    }

    [Fact]
    public void ResolveProviderAlias_MapsDefaultModelNameToRegisteredProvider()
    {
        var registry = CreateRegistry();
        registry.Register(new ProviderConfig
        {
            Alias = "haiku",
            Type = ProviderType.Anthropic,
            ApiKey = "sk-ant-test",
            DefaultModel = "claude-haiku-4-5-20251001",
        });

        var resolution = registry.ResolveProviderAlias("claude-haiku-4-5-20251001");

        Assert.True(resolution.IsResolved, resolution.Error);
        Assert.Equal("haiku", resolution.ResolvedAlias);
    }

    [Fact]
    public void ResolveProviderAlias_FailsProviderTypeAliasWhenAmbiguous()
    {
        var registry = CreateRegistry();
        registry.Register(new ProviderConfig
        {
            Alias = "sonnet",
            Type = ProviderType.Anthropic,
            ApiKey = "sk-ant-test",
        });
        registry.Register(new ProviderConfig
        {
            Alias = "haiku",
            Type = ProviderType.Anthropic,
            ApiKey = "sk-ant-test",
        });

        var resolution = registry.ResolveProviderAlias("anthropic");

        Assert.False(resolution.IsResolved);
        Assert.NotNull(resolution.Error);
        Assert.Contains("multiple registered providers", resolution.Error);
        Assert.Contains("sonnet", resolution.Error);
        Assert.Contains("haiku", resolution.Error);
    }

    [Fact]
    public void DefaultCouncilProviderAliasesResolveWithDocumentedAnthropicProviderSetup()
    {
        var registry = CreateRegistry();
        registry.Register(new ProviderConfig
        {
            Alias = "claude",
            Type = ProviderType.Anthropic,
            ApiKey = "sk-ant-test",
            DefaultModel = "claude-sonnet-4-20250514",
        });

        var defaultsCouncilPath = GetDefaultsCouncilPath();
        var providerAliases = Directory.EnumerateFiles(defaultsCouncilPath, "*.md")
            .Select(ReadProviderAlias)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        Assert.NotEmpty(providerAliases);
        foreach (var providerAlias in providerAliases)
        {
            var resolution = registry.ResolveProviderAlias(providerAlias);
            Assert.True(resolution.IsResolved, $"Default council provider '{providerAlias}' should resolve: {resolution.Error}");
            Assert.Equal("claude", resolution.ResolvedAlias);
        }
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

    private static string GetDefaultsCouncilPath()
    {
        var searchPaths = new[]
        {
            Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "..", "..", "..", "dev", "defaults", "council"),
            Path.Combine(Directory.GetCurrentDirectory(), "dev", "defaults", "council"),
        };

        foreach (var searchPath in searchPaths)
        {
            var fullPath = Path.GetFullPath(searchPath);
            if (Directory.Exists(fullPath))
            {
                return fullPath;
            }
        }

        throw new InvalidOperationException("Could not find dev/defaults/council for default council provider regression test.");
    }

    private static string ReadProviderAlias(string path)
    {
        foreach (var line in File.ReadLines(path))
        {
            var trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                break;
            }

            if (trimmed.StartsWith("provider:", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed["provider:".Length..].Trim();
            }
        }

        throw new InvalidOperationException($"Council member default '{path}' does not declare a provider alias.");
    }
}
