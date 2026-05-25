using QuillForge.Core.Models;
using Xunit;

namespace QuillForge.LibrarianEval.Tests;

public sealed class LibrarianEvalScorerTests
{
    private readonly LibrarianEvalScorer _scorer = new();

    [Fact]
    public void Score_CorrectSourceIncluded_MatchesExpected()
    {
        var question = new LibrarianEvalQuestion
        {
            Id = "q1",
            Query = "test",
            ExpectedSources = ["characters/link.md"],
        };

        var result = MakeResult(sources: ["characters/link.md"]);
        var scores = _scorer.Score(result, question);

        Assert.Equal(1.0, scores.CorrectSourceIncluded);
    }

    [Fact]
    public void Score_CorrectSourceIncluded_MissingSource_ReturnsZero()
    {
        var question = new LibrarianEvalQuestion
        {
            Id = "q1",
            Query = "test",
            ExpectedSources = ["characters/link.md", "world/weapons.md"],
        };

        var result = MakeResult(sources: ["characters/link.md"]);
        var scores = _scorer.Score(result, question);

        Assert.Equal(0.5, scores.CorrectSourceIncluded);
    }

    [Fact]
    public void Score_OffCharacterSourceExcluded_NoViolation_ReturnsOne()
    {
        var question = new LibrarianEvalQuestion
        {
            Id = "q1",
            Query = "test",
            ForbiddenSources = ["characters/link-dark.md"],
        };

        var result = MakeResult(sources: ["characters/link.md"]);
        var scores = _scorer.Score(result, question);

        Assert.Equal(1.0, scores.OffCharacterSourceExcluded);
    }

    [Fact]
    public void Score_OffCharacterSourceExcluded_WithViolation_ReturnsPartial()
    {
        var question = new LibrarianEvalQuestion
        {
            Id = "q1",
            Query = "test",
            ForbiddenSources = ["characters/link-dark.md", "organizations/black-flame.md"],
        };

        var result = MakeResult(sources: ["characters/link.md", "characters/link-dark.md"]);
        var scores = _scorer.Score(result, question);

        Assert.Equal(0.5, scores.OffCharacterSourceExcluded);
    }

    [Fact]
    public void Score_NoForbiddenGraft_NoViolation_ReturnsOne()
    {
        var question = new LibrarianEvalQuestion
        {
            Id = "q1",
            Query = "test",
            ForbiddenFacts = ["cursed blade"],
        };

        var result = MakeResult(passages: ["Link wields the hero's blade."]);
        var scores = _scorer.Score(result, question);

        Assert.Equal(1.0, scores.NoForbiddenGraft);
    }

    [Fact]
    public void Score_NoForbiddenGraft_WithViolation_ReturnsZero()
    {
        var question = new LibrarianEvalQuestion
        {
            Id = "q1",
            Query = "test",
            ForbiddenFacts = ["cursed blade"],
        };

        var result = MakeResult(passages: ["Dark Link wields a cursed blade."]);
        var scores = _scorer.Score(result, question);

        Assert.Equal(0.0, scores.NoForbiddenGraft);
    }

    [Fact]
    public void Score_Clarification_Required_AndProvided_ReturnsOne()
    {
        var question = new LibrarianEvalQuestion
        {
            Id = "q1",
            Query = "test",
            RequiresClarification = true,
        };

        var result = MakeResult(
            passages: [],
            confidence: LoreConfidence.Low,
            sources: []);
        var scores = _scorer.Score(result, question);

        Assert.Equal(1.0, scores.AskedForClarification);
    }

    [Fact]
    public void Score_Clarification_Required_ButNotProvided_ReturnsZero()
    {
        var question = new LibrarianEvalQuestion
        {
            Id = "q1",
            Query = "test",
            RequiresClarification = true,
        };

        var result = MakeResult(
            passages: ["The captain is a brave leader."],
            confidence: LoreConfidence.High);
        var scores = _scorer.Score(result, question);

        Assert.Equal(0.0, scores.AskedForClarification);
    }

    [Fact]
    public void Score_SharedFactsAccessible_ReturnsOne()
    {
        var question = new LibrarianEvalQuestion
        {
            Id = "q1",
            Query = "test",
            ExpectedSources = ["characters/link.md"],
            SharedFactSources = ["world/weapons.md"],
        };

        var result = MakeResult(sources: ["characters/link.md", "world/weapons.md"]);
        var scores = _scorer.Score(result, question);

        Assert.Equal(1.0, scores.SharedFactsAccessible);
    }

    [Fact]
    public void Score_ExpectedPassagesPresent_Matches()
    {
        var question = new LibrarianEvalQuestion
        {
            Id = "q1",
            Query = "test",
            ExpectedPassageSubstrings = ["hero's blade", "Hylia"],
        };

        var result = MakeResult(passages: ["Link wields the hero's blade, blessed by Hylia."]);
        var scores = _scorer.Score(result, question);

        Assert.Equal(1.0, scores.ExpectedPassagesPresent);
    }

    [Fact]
    public void Score_OverallScore_AveragesApplicableScores()
    {
        var question = new LibrarianEvalQuestion
        {
            Id = "q1",
            Query = "test",
            ExpectedSources = ["characters/link.md"],
            ForbiddenSources = ["characters/link-dark.md"],
        };

        var result = MakeResult(
            sources: ["characters/link.md"],
            passages: ["Link is the hero."]);
        var scores = _scorer.Score(result, question);

        // 1.0 for correct source, 1.0 for forbidden source excluded, null for others -> average = 1.0
        Assert.Equal(1.0, scores.OverallScore);
    }

    [Fact]
    public void Score_PathNormalization_HandlesBackslashes()
    {
        var question = new LibrarianEvalQuestion
        {
            Id = "q1",
            Query = "test",
            ExpectedSources = ["characters/link.md"],
        };

        var result = MakeResult(sources: ["characters\\link.md"]);
        var scores = _scorer.Score(result, question);

        Assert.Equal(1.0, scores.CorrectSourceIncluded);
    }

    [Fact]
    public void Score_NullWhenCriteriaEmpty()
    {
        var question = new LibrarianEvalQuestion
        {
            Id = "q1",
            Query = "test",
        };

        var result = MakeResult();
        var scores = _scorer.Score(result, question);

        Assert.Null(scores.CorrectSourceIncluded);
        Assert.Null(scores.OffCharacterSourceExcluded);
        Assert.Null(scores.NoForbiddenGraft);
        Assert.Null(scores.AskedForClarification);
        Assert.Null(scores.SharedFactsAccessible);
        Assert.Null(scores.ExpectedPassagesPresent);
        Assert.Equal(0.0, scores.OverallScore);
    }

    private static LibrarianEvalResult MakeResult(
        IReadOnlyList<string>? passages = null,
        IReadOnlyList<string>? sources = null,
        LoreConfidence confidence = LoreConfidence.High,
        bool parseFailed = false)
    {
        return new LibrarianEvalResult
        {
            QuestionId = "q1",
            Query = "test",
            RawResponse = "{}",
            ParsedBundle = new LoreBundle
            {
                RelevantPassages = passages ?? [],
                SourceFiles = sources ?? [],
                Confidence = confidence,
            },
            ParseFailed = parseFailed,
            Usage = new TokenUsage(0, 0),
            Scores = new LibrarianEvalScores(),
        };
    }
}
