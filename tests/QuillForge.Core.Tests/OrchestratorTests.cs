using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Models;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

public class OrchestratorTests
{
    private static readonly ILoggerFactory LogFactory = NullLoggerFactory.Instance;

    private static OrchestratorAgent CreateOrchestrator(FakeCompletionService fake)
    {
        var continuation = new ContinuationStrategy(LogFactory.CreateLogger<ContinuationStrategy>());
        var toolLoop = new ToolLoop(fake, continuation, LogFactory.CreateLogger<ToolLoop>());

        IMode[] modes =
        [
            new GeneralMode(),
            new WriterMode(LogFactory.CreateLogger<WriterMode>()),
            new RoleplayMode(),
            new ForgeMode(),
            new CouncilMode(),
        ];

        var personaStore = new FakePersonaStore();

        return new OrchestratorAgent(toolLoop, modes, personaStore, LogFactory.CreateLogger<OrchestratorAgent>());
    }

    [Fact]
    public void DefaultMode_IsGeneral()
    {
        var fake = new FakeCompletionService();
        var orchestrator = CreateOrchestrator(fake);
        Assert.Equal("general", orchestrator.ActiveModeName);
    }

    [Fact]
    public void SetMode_SwitchesMode()
    {
        var fake = new FakeCompletionService();
        var orchestrator = CreateOrchestrator(fake);

        orchestrator.SetMode("writer", "my-novel", "chapter1.md");

        Assert.Equal("writer", orchestrator.ActiveModeName);
        Assert.Equal("my-novel", orchestrator.ProjectName);
        Assert.Equal("chapter1.md", orchestrator.CurrentFile);
    }

    [Fact]
    public void SetMode_InvalidMode_Throws()
    {
        var fake = new FakeCompletionService();
        var orchestrator = CreateOrchestrator(fake);

        Assert.Throws<ArgumentException>(() => orchestrator.SetMode("nonexistent"));
    }

    [Fact]
    public async Task HandleAsync_BuildsPromptWithPersonaAndMode()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("Hello from the orchestrator!");
        var orchestrator = CreateOrchestrator(fake);

        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("hi")),
        };
        var context = new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = "general" };

        await orchestrator.HandleAsync("default", "test-model", 1024, [], messages, context);

        var request = fake.ReceivedRequests[0];
        Assert.Contains("test persona", request.SystemPrompt!);
        Assert.Contains("General", request.SystemPrompt!);
    }

    [Fact]
    public void BuildSystemPrompt_CombinesPersonaModeAndState()
    {
        var fake = new FakeCompletionService();
        var orchestrator = CreateOrchestrator(fake);

        var modeContext = new ModeContext
        {
            ProjectName = "test-project",
            StoryStateSummary = "Tension is high. The dragon approaches.",
        };

        var prompt = orchestrator.BuildSystemPrompt("I am a helpful persona.", modeContext);

        Assert.Contains("I am a helpful persona.", prompt);
        Assert.Contains("General", prompt);
        Assert.Contains("dragon approaches", prompt);
    }

    [Fact]
    public void SetMode_ToWriter_ThenBack_ResetsWriterState()
    {
        var fake = new FakeCompletionService();
        var orchestrator = CreateOrchestrator(fake);

        orchestrator.SetMode("writer", "novel");
        // Switching away from writer should reset its state
        orchestrator.SetMode("general");

        Assert.Equal("general", orchestrator.ActiveModeName);
    }
}

/// <summary>
/// Simple fake persona store for testing.
/// </summary>
internal sealed class FakePersonaStore : QuillForge.Core.Services.IPersonaStore
{
    public Task<string> LoadAsync(string personaName, int? maxTokens = null, CancellationToken ct = default)
    {
        return Task.FromResult("You are a test persona. Be helpful and creative.");
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(["default"]);
    }
}
