using QuillForge.Core.Models;

namespace QuillForge.Core.Tests;

public class ToolResultTests
{
    [Fact]
    public void Ok_HasSuccessTrue_And_NoError()
    {
        var result = ToolResult.Ok("some output");

        Assert.True(result.Success);
        Assert.Equal("some output", result.Content);
        Assert.Null(result.Error);
    }

    [Fact]
    public void Fail_HasSuccessFalse_And_EmptyContent()
    {
        var result = ToolResult.Fail("something broke");

        Assert.False(result.Success);
        Assert.Equal(string.Empty, result.Content);
        Assert.Equal("something broke", result.Error);
    }
}
