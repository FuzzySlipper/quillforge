using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Services;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

public class DelegatePoolTests
{
    [Fact]
    public async Task RunAsync_ReturnsSetupErrorWithoutInvokingDelegates_WhenAliasCannotResolve()
    {
        var serviceFactoryCalls = 0;
        var pool = new DelegatePool(
            _ =>
            {
                serviceFactoryCalls++;
                return new FakeCompletionService();
            },
            alias => ProviderAliasResolution.Failed(alias, $"No provider registered with alias '{alias}'. Registered aliases: claude."),
            NullLogger<DelegatePool>.Instance);

        var tasks = new[]
        {
            CreateTask("analyst", "anthropic"),
            CreateTask("skeptic", "anthropic"),
        };

        var results = await pool.RunAsync(tasks);

        Assert.Equal(0, serviceFactoryCalls);
        Assert.Equal(2, results.Count);
        Assert.All(results.Values, result =>
        {
            Assert.Equal("anthropic", result.ProviderAlias);
            Assert.NotNull(result.Error);
            Assert.Contains("Provider setup error", result.Error);
            Assert.Contains("No delegated tasks were invoked", result.Error);
            Assert.Equal(string.Empty, result.Content);
        });
    }

    [Fact]
    public async Task RunAsync_DoesNotInjectSamplingParameters_WhenTaskDoesNotConfigureThem()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("delegate response");
        var pool = new DelegatePool(_ => fake, NullLogger<DelegatePool>.Instance);

        await pool.RunAsync([CreateTask("analyst", "default")]);

        var request = Assert.Single(fake.ReceivedRequests);
        Assert.Equal("default", request.ProviderAlias);
        Assert.Null(request.Temperature);
    }

    [Fact]
    public async Task RunAsync_UsesExplicitTemperature_WhenTaskConfiguresIt()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("delegate response");
        var pool = new DelegatePool(_ => fake, NullLogger<DelegatePool>.Instance);

        await pool.RunAsync([CreateTask("analyst", "default") with { Temperature = 0.4f }]);

        var request = Assert.Single(fake.ReceivedRequests);
        Assert.Equal(0.4, request.Temperature!.Value, precision: 5);
    }

    [Fact]
    public async Task RunAsync_UsesResolvedProviderAlias_WhenAliasResolverMapsAlias()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("delegate response");

        var requestedAliases = new List<string>();
        var serviceAliases = new List<string>();
        var pool = new DelegatePool(
            alias =>
            {
                serviceAliases.Add(alias);
                return fake;
            },
            alias =>
            {
                requestedAliases.Add(alias);
                return ProviderAliasResolution.Resolved(alias, "claude");
            },
            NullLogger<DelegatePool>.Instance);

        var results = await pool.RunAsync([CreateTask("analyst", "anthropic")]);

        Assert.Equal(["anthropic"], requestedAliases);
        Assert.Equal(["claude"], serviceAliases);
        var result = Assert.Single(results.Values);
        Assert.Equal("claude", result.ProviderAlias);
        Assert.Equal("claude", Assert.Single(fake.ReceivedRequests).ProviderAlias);
        Assert.Null(result.Error);
        Assert.Equal("delegate response", result.Content);
    }

    private static DelegateTask CreateTask(string id, string providerAlias) => new()
    {
        Id = id,
        SystemPrompt = "You are a test delegate.",
        UserPrompt = "Test question",
        ProviderAlias = providerAlias,
        ModelOverride = "test-model",
    };
}
