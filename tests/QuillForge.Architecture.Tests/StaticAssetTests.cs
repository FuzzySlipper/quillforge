namespace QuillForge.Architecture.Tests;

public sealed class StaticAssetTests
{
    private static readonly string RepoRoot = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));

    [Theory]
    [InlineData("frame-left.png")]
    [InlineData("frame-right.png")]
    public void FramedLayoutSideImages_AreShipped(string fileName)
    {
        var path = Path.Combine(RepoRoot, "src", "QuillForge.Web", "wwwroot", "layout-images", fileName);

        Assert.True(File.Exists(path), $"Expected static asset to exist: {path}");
        Assert.True(new FileInfo(path).Length > 0, $"Expected static asset to be non-empty: {path}");
    }

    [Fact]
    public void FramedLayout_DefaultReferencesShippedSideImages()
    {
        var layoutPath = Path.Combine(RepoRoot, "dev", "defaults", "layouts", "framed.md");
        var content = File.ReadAllText(layoutPath);

        Assert.Contains("src: /layout-images/frame-left.png", content, StringComparison.Ordinal);
        Assert.Contains("src: /layout-images/frame-right.png", content, StringComparison.Ordinal);
    }
}
