using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Models;

namespace QuillForge.Core.Tests;

public class ForgeReviewerTests
{
    private static ForgeReviewerAgent CreateReviewer(double passThreshold = 7.0)
    {
        return new ForgeReviewerAgent(
            null!, // Not used for parsing tests
            new AppConfig(),
            NullLoggerFactory.Instance.CreateLogger<ForgeReviewerAgent>())
        { PassThreshold = passThreshold };
    }

    [Fact]
    public void ParsesCleanScores()
    {
        var json = """
            {
                "continuity": 8,
                "brief_adherence": 9,
                "voice_consistency": 7,
                "quality": 8,
                "feedback": "Strong chapter with minor pacing issues."
            }
            """;

        var reviewer = CreateReviewer();
        var result = reviewer.ParseReviewResult(json);

        Assert.Equal(8, result.Continuity);
        Assert.Equal(9, result.BriefAdherence);
        Assert.Equal(7, result.VoiceConsistency);
        Assert.Equal(8, result.Quality);
        Assert.Contains("pacing", result.Feedback);
        // Overall = 8*0.3 + 9*0.3 + 7*0.2 + 8*0.2 = 2.4 + 2.7 + 1.4 + 1.6 = 8.1
        Assert.Equal(8.1, result.Overall, 1);
        Assert.True(result.Passed);
    }

    [Fact]
    public void LowScores_FailReview()
    {
        var json = """
            {
                "continuity": 4,
                "brief_adherence": 5,
                "voice_consistency": 3,
                "quality": 4,
                "feedback": "Major issues with consistency."
            }
            """;

        var reviewer = CreateReviewer();
        var result = reviewer.ParseReviewResult(json);

        Assert.False(result.Passed);
        // Overall = 4*0.3 + 5*0.3 + 3*0.2 + 4*0.2 = 1.2 + 1.5 + 0.6 + 0.8 = 4.1
        Assert.Equal(4.1, result.Overall, 1);
    }

    [Fact]
    public void ParsesScoresInMarkdownFences()
    {
        var text = """
            Here is my review:
            ```json
            {"continuity": 7, "brief_adherence": 7, "voice_consistency": 7, "quality": 7, "feedback": "Solid."}
            ```
            """;

        var reviewer = CreateReviewer();
        var result = reviewer.ParseReviewResult(text);

        Assert.Equal(7, result.Continuity);
        Assert.True(result.Passed);
    }

    [Fact]
    public void UnparseableResponse_ReturnsFailing()
    {
        var reviewer = CreateReviewer();
        var result = reviewer.ParseReviewResult("This chapter was great! I loved the characters.");

        Assert.False(result.Passed);
        Assert.Equal(0, result.Overall);
        Assert.Contains("great", result.Feedback);
    }

    [Fact]
    public void CustomPassThreshold_IsRespected()
    {
        var json = """
            {"continuity": 6, "brief_adherence": 6, "voice_consistency": 6, "quality": 6, "feedback": "ok"}
            """;

        var reviewer = CreateReviewer(passThreshold: 5.0);
        var result = reviewer.ParseReviewResult(json);

        Assert.Equal(6.0, result.Overall, 1);
        Assert.True(result.Passed); // 6.0 >= 5.0
    }
}
