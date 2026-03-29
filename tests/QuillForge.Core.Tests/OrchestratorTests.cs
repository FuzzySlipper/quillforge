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
            new WriterMode(LogFactory.CreateLogger<WriterMode>()),
            new RoleplayMode(),
            new ForgeMode(),
            new CouncilMode(),
        ];

        var personaStore = new FakePersonaStore();
        var characterStore = new FakeCharacterCardStore();
        var storyStateService = new FakeStoryStateService();
        var contentFileService = new FakeContentFileService();

        return new OrchestratorAgent(
            toolLoop, modes, personaStore, characterStore, storyStateService,
            contentFileService, new AppConfig(), LogFactory.CreateLogger<OrchestratorAgent>());
    }

    [Fact]
    public void DefaultState_IsGeneral()
    {
        var state = new SessionRuntimeState();
        Assert.Equal("general", state.Mode.ActiveModeName);
    }

    [Fact]
    public void SetMode_SwitchesMode()
    {
        var fake = new FakeCompletionService();
        var orchestrator = CreateOrchestrator(fake);
        var state = new SessionRuntimeState();

        orchestrator.SetMode(state, "writer", "my-novel", "chapter1.md");

        Assert.Equal("writer", state.Mode.ActiveModeName);
        Assert.Equal("my-novel", state.Mode.ProjectName);
        Assert.Equal("chapter1.md", state.Mode.CurrentFile);
    }

    [Fact]
    public void SetMode_InvalidMode_Throws()
    {
        var fake = new FakeCompletionService();
        var orchestrator = CreateOrchestrator(fake);
        var state = new SessionRuntimeState();

        Assert.Throws<ArgumentException>(() => orchestrator.SetMode(state, "nonexistent"));
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
    public void SetMode_ToWriter_ThenBack_ResetsWriterState()
    {
        var fake = new FakeCompletionService();
        var orchestrator = CreateOrchestrator(fake);
        var state = new SessionRuntimeState();

        orchestrator.SetMode(state, "writer", "novel");
        // Switching away from writer should reset its state
        orchestrator.SetMode(state, "general");

        Assert.Equal("general", state.Mode.ActiveModeName);
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

internal sealed class FakeCharacterCardStore : ICharacterCardStore
{
    public Task<CharacterCard?> LoadAsync(string fileName, CancellationToken ct = default)
        => Task.FromResult<CharacterCard?>(null);

    public Task SaveAsync(string fileName, CharacterCard card, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<CharacterCard>> ListAsync(CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<CharacterCard>>([]);

    public string CardToPrompt(CharacterCard card) => $"Character: {card.Name}";

    public CharacterCard NewTemplate(string name = "New Character")
        => new() { Name = name };

    public Task<CharacterCard> ImportTavernCardAsync(string pngPath, CancellationToken ct = default)
        => Task.FromResult(new CharacterCard { Name = "Imported" });
}

internal sealed class FakeStoryStateService : IStoryStateService
{
    public Task<IReadOnlyDictionary<string, object>> LoadAsync(string stateFilePath, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyDictionary<string, object>>(new Dictionary<string, object>());

    public Task SaveAsync(string stateFilePath, IReadOnlyDictionary<string, object> state, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task<IReadOnlyDictionary<string, object>> MergeAsync(string stateFilePath, IReadOnlyDictionary<string, object> updates, CancellationToken ct = default)
        => Task.FromResult(updates);

    public Task IncrementCounterAsync(string stateFilePath, string counterKey, CancellationToken ct = default)
        => Task.CompletedTask;

    public Task RemoveKeyAsync(string stateFilePath, string key, CancellationToken ct = default)
        => Task.CompletedTask;
}
