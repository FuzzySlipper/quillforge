using QuillForge.Core;
using QuillForge.Core.Models;

namespace QuillForge.ProviderHarness.Tests;

public sealed class HarnessDebugBridgeDriverTests
{
    [Fact]
    public async Task Driver_CreatesSession_SetsMode_AndLoadsSessionDeterministically()
    {
        var scenario = new HarnessProviderScenario
        {
            Name = "bridge-driver-session-setup",
        };

        await using var providerHost = await HarnessProviderHost.StartAsync(scenario);
        await using var runner = new HarnessInteractiveScenarioRunner(providerHost);

        var created = await runner.Bridge.CreateSessionAsync();
        Assert.NotEqual(Guid.Empty, created.SessionId);
        Assert.Equal("Debug Session", created.Name);

        var mode = await runner.Bridge.SetModeAsync(
            Mode.Writer.ToWireString(),
            created.SessionId,
            project: "moon-heist");
        Assert.Equal("writer", mode.Mode);
        Assert.Equal("moon-heist", mode.Project);

        var session = await runner.Bridge.GetSessionAsync(created.SessionId);
        Assert.Equal(created.SessionId, session.SessionId);
        Assert.Equal("Debug Session", session.Name);
        Assert.Equal(0, session.MessageCount);
        Assert.Empty(session.Messages);
    }
}
