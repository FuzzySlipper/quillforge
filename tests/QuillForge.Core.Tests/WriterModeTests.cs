using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Models;

namespace QuillForge.Core.Tests;

public class WriterModeTests
{
    private static readonly ILogger Logger = NullLoggerFactory.Instance.CreateLogger<WriterMode>();

    private static WriterMode CreateMode()
    {
        return new WriterMode(NullLoggerFactory.Instance.CreateLogger<WriterMode>());
    }

    [Fact]
    public void InitialState_IsIdle()
    {
        var state = new WriterRuntimeState();
        Assert.Equal(WriterState.Idle, state.State);
        Assert.Null(state.PendingContent);
    }

    [Fact]
    public void LongResponse_TransitionsToPendingReview()
    {
        var state = new WriterRuntimeState();
        var longText = new string('x', 300);
        var response = new AgentResponse
        {
            Content = new MessageContent(longText),
            StopReason = "end_turn",
            Usage = new TokenUsage(10, 20),
            ToolRoundsUsed = 0,
        };

        WriterMode.CaptureIfPending(response, state, Logger);

        Assert.Equal(WriterState.PendingReview, state.State);
        Assert.Equal(longText, state.PendingContent);
    }

    [Fact]
    public void ShortResponse_StaysIdle()
    {
        var state = new WriterRuntimeState();
        var response = new AgentResponse
        {
            Content = new MessageContent("Sure, I'll write that for you."),
            StopReason = "end_turn",
            Usage = new TokenUsage(10, 20),
            ToolRoundsUsed = 0,
        };

        WriterMode.CaptureIfPending(response, state, Logger);

        Assert.Equal(WriterState.Idle, state.State);
        Assert.Null(state.PendingContent);
    }

    [Fact]
    public void Accept_ReturnsPendingContent_AndResetsToIdle()
    {
        var state = new WriterRuntimeState();
        var longText = new string('x', 300);
        var response = new AgentResponse
        {
            Content = new MessageContent(longText),
            StopReason = "end_turn",
            Usage = new TokenUsage(10, 20),
            ToolRoundsUsed = 0,
        };
        WriterMode.CaptureIfPending(response, state, Logger);

        var accepted = WriterMode.Accept(state, Logger);

        Assert.Equal(longText, accepted);
        Assert.Equal(WriterState.Idle, state.State);
        Assert.Null(state.PendingContent);
    }

    [Fact]
    public void Accept_WhenIdle_ReturnsNull()
    {
        var state = new WriterRuntimeState();
        Assert.Null(WriterMode.Accept(state, Logger));
    }

    [Fact]
    public void Reject_ClearsPending_AndResetsToIdle()
    {
        var state = new WriterRuntimeState();
        var response = new AgentResponse
        {
            Content = new MessageContent(new string('x', 300)),
            StopReason = "end_turn",
            Usage = new TokenUsage(10, 20),
            ToolRoundsUsed = 0,
        };
        WriterMode.CaptureIfPending(response, state, Logger);

        WriterMode.Reject(state, Logger);

        Assert.Equal(WriterState.Idle, state.State);
        Assert.Null(state.PendingContent);
    }

    [Fact]
    public void Reset_ClearsEverything()
    {
        var state = new WriterRuntimeState();
        var longText = new string('x', 300);
        var response = new AgentResponse
        {
            Content = new MessageContent(longText),
            StopReason = "end_turn",
            Usage = new TokenUsage(10, 20),
            ToolRoundsUsed = 0,
        };
        WriterMode.CaptureIfPending(response, state, Logger);

        WriterMode.Reset(state);

        Assert.Equal(WriterState.Idle, state.State);
        Assert.Null(state.PendingContent);
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
