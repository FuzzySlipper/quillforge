using QuillForge.RoleplayDriftHarness.Models;
using QuillForge.RoleplayDriftHarness.Runners;
using Xunit;

namespace QuillForge.RoleplayDriftHarness.Tests;

public sealed class DriftDetectorTests
{
    private readonly DriftDetector _detector = new();

    [Fact]
    public void Detect_NoForbiddenFacts_ReturnsNoDrift()
    {
        var events = new List<TraceEvent>
        {
            new()
            {
                Turn = 1,
                Component = "query_lore",
                Boundary = nameof(BoundaryType.QueryLore),
                Preview = "Xavier has standard gear",
                Content = "Xavier has standard gear. No prosthetic modifications.",
            },
        };

        var result = _detector.Detect(events, ["prosthetic arm"]);

        Assert.False(result.HasDrift);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void Detect_ForbiddenFactPresent_ReturnsDrift()
    {
        var events = new List<TraceEvent>
        {
            new()
            {
                Turn = 1,
                Component = "query_lore",
                Boundary = nameof(BoundaryType.QueryLore),
                Preview = "Xavier has a prosthetic arm",
                Content = "Xavier has a prosthetic arm with advanced functionality.",
            },
        };

        var result = _detector.Detect(events, ["prosthetic arm"]);

        Assert.True(result.HasDrift);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("prosthetic arm", finding.ForbiddenFact);
        Assert.Equal(1, finding.FirstAppearanceTurn);
        Assert.Equal(nameof(BoundaryType.QueryLore), finding.FirstAppearanceBoundary);
    }

    [Fact]
    public void Detect_ForbiddenFactInPreviewOnly_DetectsCorrectly()
    {
        // The content may be truncated but the preview contains the fact
        var events = new List<TraceEvent>
        {
            new()
            {
                Turn = 1,
                Component = "visible_response",
                Boundary = nameof(BoundaryType.VisibleResponse),
                Preview = "prosthetic arm modification is visible",
                Content = null,
            },
        };

        var result = _detector.Detect(events, ["prosthetic arm"]);

        Assert.True(result.HasDrift);
        Assert.Single(result.Findings);
    }

    [Fact]
    public void Detect_MultipleForbiddenFacts_ReturnsMultipleFindings()
    {
        var events = new List<TraceEvent>
        {
            new()
            {
                Turn = 1,
                Component = "query_lore",
                Boundary = nameof(BoundaryType.QueryLore),
                Preview = "lore query result",
                Content = "Xavier has a prosthetic arm with a Toring Chip interface.",
            },
        };

        var result = _detector.Detect(events, ["prosthetic arm", "Toring Chip"]);

        Assert.True(result.HasDrift);
        Assert.Equal(2, result.Findings.Count);
    }

    [Fact]
    public void Detect_EmptyForbiddenList_ReturnsNoDrift()
    {
        var events = new List<TraceEvent>
        {
            new()
            {
                Turn = 1,
                Component = "query_lore",
                Boundary = nameof(BoundaryType.QueryLore),
                Preview = "any content",
                Content = "any content",
            },
        };

        var result = _detector.Detect(events, []);

        Assert.False(result.HasDrift);
    }

    [Fact]
    public void Detect_EmptyEvents_ReturnsNoDrift()
    {
        var result = _detector.Detect([], ["prosthetic arm"]);

        Assert.False(result.HasDrift);
    }

    [Fact]
    public void ClassifyOrigin_QueryLore_ReturnsRetrieval()
    {
        var events = new List<TraceEvent>
        {
            new()
            {
                Turn = 1,
                Component = "query_lore",
                Boundary = nameof(BoundaryType.QueryLore),
                Preview = "preview",
                Content = "prosthetic arm",
            },
        };

        var result = _detector.Detect(events, ["prosthetic arm"]);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("retrieval", finding.LikelyOrigin);
    }

    [Fact]
    public void ClassifyOrigin_NarrativeDirector_ReturnsDirectorSynthesis()
    {
        var events = new List<TraceEvent>
        {
            new()
            {
                Turn = 1,
                Component = "scene_brief",
                Boundary = nameof(BoundaryType.NarrativeDirector),
                Preview = "preview",
                Content = "prosthetic arm",
            },
        };

        var result = _detector.Detect(events, ["prosthetic arm"]);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("director_synthesis", finding.LikelyOrigin);
    }

    [Fact]
    public void ClassifyOrigin_ProseWriter_ReturnsProseMisuse()
    {
        var events = new List<TraceEvent>
        {
            new()
            {
                Turn = 1,
                Component = "direct_scene",
                Boundary = nameof(BoundaryType.ProseWriter),
                Preview = "preview",
                Content = "prosthetic arm",
            },
        };

        var result = _detector.Detect(events, ["prosthetic arm"]);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("prose_misuse", finding.LikelyOrigin);
    }

    [Fact]
    public void ClassifyOrigin_UnknownComponent_ReturnsUncertain()
    {
        var events = new List<TraceEvent>
        {
            new()
            {
                Turn = 1,
                Component = "custom_middleware",
                Boundary = "CustomBoundary",
                Preview = "preview",
                Content = "prosthetic arm",
            },
        };

        var result = _detector.Detect(events, ["prosthetic arm"]);
        var finding = Assert.Single(result.Findings);
        Assert.Equal("uncertain", finding.LikelyOrigin);
    }

    [Fact]
    public void Detect_FirstAppearanceOnly_IgnoresLaterAppearances()
    {
        var events = new List<TraceEvent>
        {
            new()
            {
                Turn = 1,
                Component = "query_lore",
                Boundary = nameof(BoundaryType.QueryLore),
                Preview = "preview",
                Content = "Xavier has a prosthetic arm.",
            },
            new()
            {
                Turn = 2,
                Component = "visible_response",
                Boundary = nameof(BoundaryType.VisibleResponse),
                Preview = "preview 2",
                Content = "The prosthetic arm is clearly visible.",
            },
        };

        var result = _detector.Detect(events, ["prosthetic arm"]);
        var finding = Assert.Single(result.Findings);
        Assert.Equal(1, finding.FirstAppearanceTurn);
        Assert.Equal(nameof(BoundaryType.QueryLore), finding.FirstAppearanceBoundary);
    }
}
