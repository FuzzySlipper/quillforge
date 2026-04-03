using QuillForge.Core.Agents.Modes;

namespace QuillForge.Core.Tests;

public class WriterModeTests
{
    [Fact]
    public void SystemPromptSection_IncludesProjectName()
    {
        var mode = new WriterMode();
        var section = mode.BuildSystemPromptSection(new ModeContext { ProjectName = "My Novel" });

        Assert.Contains("My Novel", section);
        Assert.Contains("Writer", section);
    }

    [Fact]
    public void SystemPromptSection_IncludesPendingReviewNote()
    {
        var mode = new WriterMode();
        var section = mode.BuildSystemPromptSection(new ModeContext { WriterPendingContent = "Pending..." });

        Assert.Contains("pending content awaiting user review", section);
    }
}
