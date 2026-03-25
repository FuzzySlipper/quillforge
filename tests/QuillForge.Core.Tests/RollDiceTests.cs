using QuillForge.Core.Agents.Tools;

namespace QuillForge.Core.Tests;

public class RollDiceTests
{
    [Fact]
    public void SimpleRoll_2d6()
    {
        var result = RollDiceHandler.Roll("2d6");

        Assert.Equal(2, result.Rolls.Count);
        Assert.All(result.Rolls, r => Assert.InRange(r, 1, 6));
        Assert.Equal(result.Rolls.Sum(), result.Total);
    }

    [Fact]
    public void SingleDie_d20()
    {
        var result = RollDiceHandler.Roll("d20");

        Assert.Single(result.Rolls);
        Assert.InRange(result.Rolls[0], 1, 20);
    }

    [Fact]
    public void WithModifier_1d20Plus5()
    {
        var result = RollDiceHandler.Roll("1d20+5");

        Assert.Single(result.Rolls);
        Assert.Equal(result.Rolls[0] + 5, result.Total);
        Assert.Equal(5, result.Modifier);
    }

    [Fact]
    public void NegativeModifier_1d20Minus3()
    {
        var result = RollDiceHandler.Roll("1d20-3");

        Assert.Equal(-3, result.Modifier);
        Assert.Equal(result.Rolls[0] - 3, result.Total);
    }

    [Fact]
    public void KeepHighest_4d6kh3()
    {
        var result = RollDiceHandler.Roll("4d6kh3");

        Assert.Equal(4, result.Rolls.Count);
        Assert.Equal(3, result.Kept.Count);
        // Kept should be the 3 highest
        var sorted = result.Rolls.OrderByDescending(r => r).Take(3).ToList();
        Assert.Equal(sorted, result.Kept);
        Assert.Equal(result.Kept.Sum(), result.Total);
    }

    [Fact]
    public void InvalidNotation_Throws()
    {
        Assert.Throws<FormatException>(() => RollDiceHandler.Roll("not_dice"));
    }

    [Fact]
    public void TooManyDice_Throws()
    {
        Assert.Throws<FormatException>(() => RollDiceHandler.Roll("999d6"));
    }

    [Fact]
    public void KeepMoreThanRolled_Throws()
    {
        Assert.Throws<FormatException>(() => RollDiceHandler.Roll("2d6kh5"));
    }
}
