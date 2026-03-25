using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Models;

namespace QuillForge.Core.Tests;

public class WriterModeTests
{
    private static WriterMode CreateMode()
    {
        return new WriterMode(NullLoggerFactory.Instance.CreateLogger<WriterMode>());
    }

    [Fact]
    public void InitialState_IsIdle()
    {
        var mode = CreateMode();
        Assert.Equal(WriterState.Idle, mode.State);
        Assert.Null(mode.PendingContent);
    }

    [Fact]
    public async Task LongResponse_TransitionsToPendingReview()
    {
        var mode = CreateMode();
        var longText = new string('x', 300);
        var response = new AgentResponse
        {
            Content = new MessageContent(longText),
            StopReason = "end_turn",
            Usage = new TokenUsage(10, 20),
            ToolRoundsUsed = 0,
        };

        await mode.OnResponseAsync(response, new ModeContext());

        Assert.Equal(WriterState.PendingReview, mode.State);
        Assert.Equal(longText, mode.PendingContent);
    }

    [Fact]
    public async Task ShortResponse_StaysIdle()
    {
        var mode = CreateMode();
        var response = new AgentResponse
        {
            Content = new MessageContent("Sure, I'll write that for you."),
            StopReason = "end_turn",
            Usage = new TokenUsage(10, 20),
            ToolRoundsUsed = 0,
        };

        await mode.OnResponseAsync(response, new ModeContext());

        Assert.Equal(WriterState.Idle, mode.State);
        Assert.Null(mode.PendingContent);
    }

    [Fact]
    public async Task Accept_ReturnsPendingContent_AndResetsToIdle()
    {
        var mode = CreateMode();
        var longText = new string('x', 300);
        var response = new AgentResponse
        {
            Content = new MessageContent(longText),
            StopReason = "end_turn",
            Usage = new TokenUsage(10, 20),
            ToolRoundsUsed = 0,
        };
        await mode.OnResponseAsync(response, new ModeContext());

        var accepted = mode.Accept();

        Assert.Equal(longText, accepted);
        Assert.Equal(WriterState.Idle, mode.State);
        Assert.Null(mode.PendingContent);
    }

    [Fact]
    public void Accept_WhenIdle_ReturnsNull()
    {
        var mode = CreateMode();
        Assert.Null(mode.Accept());
    }

    [Fact]
    public async Task Reject_ClearsPending_AndResetsToIdle()
    {
        var mode = CreateMode();
        var response = new AgentResponse
        {
            Content = new MessageContent(new string('x', 300)),
            StopReason = "end_turn",
            Usage = new TokenUsage(10, 20),
            ToolRoundsUsed = 0,
        };
        await mode.OnResponseAsync(response, new ModeContext());

        mode.Reject();

        Assert.Equal(WriterState.Idle, mode.State);
        Assert.Null(mode.PendingContent);
    }

    [Fact]
    public async Task Reset_ClearsEverything()
    {
        var mode = CreateMode();
        // Simulate having pending content
        var longText = new string('x', 300);
        var response = new AgentResponse
        {
            Content = new MessageContent(longText),
            StopReason = "end_turn",
            Usage = new TokenUsage(10, 20),
            ToolRoundsUsed = 0,
        };
        await mode.OnResponseAsync(response, new ModeContext());

        mode.Reset();

        Assert.Equal(WriterState.Idle, mode.State);
        Assert.Null(mode.PendingContent);
    }

    [Fact]
    public void SystemPromptSection_IncludesProjectName()
    {
        var mode = CreateMode();
        var section = mode.BuildSystemPromptSection(new ModeContext { ProjectName = "My Novel" });

        Assert.Contains("My Novel", section);
        Assert.Contains("Writer", section);
    }
}
