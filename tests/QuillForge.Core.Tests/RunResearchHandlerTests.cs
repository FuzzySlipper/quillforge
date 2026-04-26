using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Agents.Tools;
using QuillForge.Core.Models;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

public class RunResearchHandlerTests
{
    private static readonly AgentContext DefaultContext = new()
    {
        SessionId = Guid.CreateVersion7(),
        ActiveMode = Mode.Research,
    };

    [Fact]
    public async Task NonRetryableResearchToolFailure_PropagatesToRunResearchToolResult()
    {
        var appConfig = new AppConfig();
        var completion = new FakeCompletionService();
        completion.EnqueueToolCall("web_search", "call_1", """{"query":"brave failure"}""");

        var failingWebSearch = new FakeToolHandler("web_search",
            (_, _, _) => Task.FromResult(ToolResult.FailNonRetryable(
                "Brave Search returned HTTP 429. Do not retry this same web_search during the current tool loop.")));
        var toolLoop = CreateToolLoop(completion, appConfig);
        var researchAgent = new ResearchAgent(
            toolLoop,
            [failingWebSearch],
            appConfig,
            NullLogger<ResearchAgent>.Instance);
        var researchPool = new ResearchPool(
            researchAgent,
            appConfig,
            NullLogger<ResearchPool>.Instance);
        var handler = new RunResearchHandler(
            researchPool,
            appConfig,
            NullLogger<RunResearchHandler>.Instance);
        using var doc = JsonDocument.Parse(
            """
            {
                "topics": [
                    { "topic": "Brave failure" }
                ],
                "project": "synthetic"
            }
            """);
        var input = new ToolInput(doc.RootElement);

        var result = await handler.HandleAsync(input, DefaultContext);

        Assert.False(result.Success);
        Assert.False(result.Retryable);
        Assert.Contains("Brave Search returned HTTP 429", result.Error!, StringComparison.Ordinal);
        Assert.Contains("ERROR", result.Error!, StringComparison.Ordinal);
        Assert.Single(completion.ReceivedRequests);
    }

    private static ToolLoop CreateToolLoop(FakeCompletionService completion, AppConfig appConfig)
    {
        var continuation = new ContinuationStrategy(NullLogger<ContinuationStrategy>.Instance);
        return new ToolLoop(
            completion,
            continuation,
            NullLogger<ToolLoop>.Instance,
            appConfig);
    }
}
