using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

public class OrchestratorTests
{
    private static readonly ILoggerFactory LogFactory = NullLoggerFactory.Instance;

    private static OrchestratorAgent CreateOrchestrator(FakeCompletionService fake)
    {
        var continuation = new ContinuationStrategy(LogFactory.CreateLogger<ContinuationStrategy>());
        var toolLoop = new ToolLoop(fake, continuation, LogFactory.CreateLogger<ToolLoop>(), new AppConfig());

        IMode[] modes =
        [
            new GeneralMode(),
            new WriterMode(),
            new RoleplayMode(),
            new ForgeMode(),
            new CouncilMode(),
        ];

        var personaStore = new FakePersonaStore();
        var sessionContextService = new FakeInteractiveSessionContextService();

        return new OrchestratorAgent(
            toolLoop, modes, personaStore, sessionContextService,
            new AppConfig(), LogFactory.CreateLogger<OrchestratorAgent>());
    }

    [Fact]
    public void DefaultState_IsGeneral()
    {
        var state = new SessionRuntimeState();
        Assert.Equal("general", state.Mode.ActiveModeName);
    }

    [Fact]
    public async Task HandleAsync_BuildsPromptWithPersonaAndMode()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("Hello from the orchestrator!");
        var orchestrator = CreateOrchestrator(fake);
        var state = new SessionRuntimeState();

        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("hi")),
        };
        var context = new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = "general" };

        await orchestrator.HandleAsync(state, "default", "test-model", 1024, [], messages, context);

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
            ActiveLoreSet = "builder",
        };

        var generalMode = orchestrator.ResolveMode("general");
        var prompt = orchestrator.BuildSystemPrompt("I am a helpful persona.", generalMode, modeContext);

        Assert.Contains("I am a helpful persona.", prompt);
        Assert.Contains("General", prompt);
        Assert.Contains("dragon approaches", prompt);
        Assert.Contains("builder", prompt);
    }

    [Fact]
    public void GeneralMode_Prompt_IsNeutralAndNonPersonalityDriven()
    {
        var mode = new GeneralMode();
        var prompt = mode.BuildSystemPromptSection(new ModeContext());

        Assert.Contains("no built-in assistant personality", prompt);
        Assert.Contains("no narrative-direction role", prompt);
        Assert.Contains("neutral coordination layer", prompt);
        Assert.DoesNotContain("helpful creative writing assistant", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RoleplayMode_Prompt_RoutesThroughDirectScene()
    {
        var mode = new RoleplayMode();
        var prompt = mode.BuildSystemPromptSection(new ModeContext
        {
            ProjectName = "gatehouse",
            CurrentFile = "scene-01.md",
            CharacterSection = "Captain Elian guards the gate.",
        });

        Assert.Contains("Use direct_scene for in-scene narrative responses", prompt);
        Assert.Contains("direct_scene owns scene direction", prompt);
        Assert.Contains("Prose returned from direct_scene", prompt);
        Assert.Contains("Do not add assistant framing", prompt);
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
