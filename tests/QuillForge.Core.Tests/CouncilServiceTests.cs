using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

public sealed class CouncilServiceTests
{
    [Fact]
    public async Task RunCouncilAsync_DoesNotOverrideProviderSamplingOptions()
    {
        var files = new FakeContentFileService();
        files.SeedFile("council/analyst.md", "provider: default\nmodel: test-model\n\nYou are a careful analyst.");
        var fake = new FakeCompletionService();
        fake.EnqueueText("analysis");
        var pool = new DelegatePool(_ => fake, NullLogger<DelegatePool>.Instance);
        var service = new CouncilService(
            files,
            pool,
            new AppConfig
            {
                Agents = new AgentsConfig
                {
                    Council = new CouncilBudget
                    {
                        MaxTokens = 321,
                        Temperature = 1.7,
                    },
                },
            },
            NullLogger<CouncilService>.Instance);

        var result = await service.RunCouncilAsync("Should this preserve provider settings?");

        var member = Assert.Single(result.Members);
        Assert.Null(member.Error);
        Assert.Equal("analysis", member.Content);
        var request = Assert.Single(fake.ReceivedRequests);
        Assert.Equal("default", request.ProviderAlias);
        Assert.Equal("test-model", request.Model);
        Assert.Equal(321, request.MaxTokens);
        Assert.Null(request.Temperature);
    }
}
