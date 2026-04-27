namespace Den.RulesEngine.Tests;

public sealed class DeterministicRandomStateTests
{
    [Fact]
    public void NextInt_ReplaysSameSequenceFromSameSeedAndDrawCount()
    {
        var first = DeterministicRandomState.Create(8675309);
        var firstDraw = first.NextInt(100);
        var secondDraw = firstDraw.State.NextInt(100);

        var replay = DeterministicRandomState.Create(8675309);
        var replayFirstDraw = replay.NextInt(100);
        var replaySecondDraw = replayFirstDraw.State.NextInt(100);

        Assert.Equal(firstDraw.Value, replayFirstDraw.Value);
        Assert.Equal(secondDraw.Value, replaySecondDraw.Value);
        Assert.Equal(2, replaySecondDraw.State.DrawCount);
    }
}
