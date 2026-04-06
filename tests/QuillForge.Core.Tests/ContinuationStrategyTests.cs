using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;

namespace QuillForge.Core.Tests;

public class ContinuationStrategyTests
{
    private static readonly ContinuationStrategy Strategy = new(
        NullLoggerFactory.Instance.CreateLogger<ContinuationStrategy>());

    [Theory]
    [InlineData(StopReason.MaxTokens, true)]
    [InlineData(StopReason.EndTurn, false)]
    [InlineData(StopReason.ToolUse, false)]
    [InlineData(StopReason.Error, false)]
    public void ShouldContinue_DetectsMaxTokens(StopReason stopReason, bool expected)
    {
        var response = new CompletionResponse
        {
            Content = new MessageContent("partial"),
            StopReason = stopReason,
            Usage = new TokenUsage(10, 20),
        };

        Assert.Equal(expected, Strategy.ShouldContinue(response));
    }

    [Fact]
    public void MergeResponses_ConcatenatesText()
    {
        var first = new CompletionResponse
        {
            Content = new MessageContent("Hello "),
            StopReason = StopReason.MaxTokens,
            Usage = new TokenUsage(10, 20),
        };
        var second = new CompletionResponse
        {
            Content = new MessageContent("World!"),
            StopReason = StopReason.EndTurn,
            Usage = new TokenUsage(15, 25),
        };

        var merged = Strategy.MergeResponses(first, second);
        Assert.Equal("Hello World!", merged.GetText());
    }

    [Fact]
    public void AggregateUsage_SumsTokens()
    {
        var first = new TokenUsage(100, 200);
        var second = new TokenUsage(50, 150);

        var total = Strategy.AggregateUsage(first, second);

        Assert.Equal(150, total.InputTokens);
        Assert.Equal(350, total.OutputTokens);
        Assert.Equal(500, total.TotalTokens);
    }
}
