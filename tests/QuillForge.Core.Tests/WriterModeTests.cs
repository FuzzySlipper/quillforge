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

    [Fact]
    public void SystemPromptSection_RoutesThroughDirectSceneInsteadOfWriteProse()
    {
        var mode = new WriterMode();
        var section = mode.BuildSystemPromptSection(new ModeContext { ProjectName = "My Novel" });

        Assert.Contains("Use direct_scene", section);
        Assert.Contains("mandatory grounding layer", section);
        Assert.DoesNotContain("Use write_prose to generate content", section);
    }

    [Fact]
    public void SystemPromptSection_TellsWriterToRegroundAfterCanonCorrections()
    {
        var mode = new WriterMode();
        var section = mode.BuildSystemPromptSection(new ModeContext { ProjectName = "My Novel" });

        Assert.Contains("If the user corrects canon", section);
        Assert.Contains("Do not patch only the single sentence", section);
    }
}
