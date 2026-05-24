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
            new GuideMode(),
            new WriterMode(),
            new RoleplayMode(),
            new LoreBuilderMode(),
            new ForgeMode(),
            new CouncilMode(),
            new ResearchMode(),
            new GamesMode(),
        ];

        var assistantPromptStore = new FakeAssistantPromptStore();
        var sessionContextService = new FakeInteractiveSessionContextService();

        return new OrchestratorAgent(
            toolLoop, modes, assistantPromptStore, sessionContextService,
            new AppConfig(), LogFactory.CreateLogger<OrchestratorAgent>());
    }

    [Fact]
    public void DefaultState_IsGuide()
    {
        var state = new SessionState();
        Assert.Equal(Mode.Guide, state.Mode.ActiveMode);
    }

    [Fact]
    public async Task HandleAsync_WriterMode_BuildsPromptWithAppOwnedPreludeAndMode()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("Hello from the orchestrator!");
        var orchestrator = CreateOrchestrator(fake);
        var state = new SessionState
        {
            Mode = new ModeSelectionState { ActiveMode = Mode.Writer },
        };

        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent("hi")),
        };
        var context = new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = Mode.Writer };

        await orchestrator.HandleAsync(state, "test-model", 1024, [], messages, context);

        var request = fake.ReceivedRequests[0];
        Assert.Contains("app-owned interactive coordinator", request.SystemPrompt!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Writer", request.SystemPrompt!);
    }

    [Fact]
    public void BuildSystemPrompt_CombinesPreludeModeAndState()
    {
        var fake = new FakeCompletionService();
        var orchestrator = CreateOrchestrator(fake);

        var modeContext = new ModeContext
        {
            ProjectName = "test-project",
            StoryStateSummary = "Tension is high. The dragon approaches.",
            ActiveLoreSet = "builder",
        };

        var writerMode = orchestrator.ResolveMode(Mode.Writer);
        var prompt = orchestrator.BuildSystemPrompt("App-owned routing rules.", writerMode, modeContext);

        Assert.Contains("App-owned routing rules.", prompt);
        Assert.Contains("Writer", prompt);
        Assert.Contains("dragon approaches", prompt);
        Assert.Contains("builder", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_FallbackIdentifiesSpeakingLayer()
    {
        var fake = new FakeCompletionService();
        var orchestrator = CreateOrchestrator(fake);

        var modeContext = new ModeContext();
        var roleplayMode = orchestrator.ResolveMode(Mode.Roleplay);
        var prompt = orchestrator.BuildSystemPrompt("Prelude.", roleplayMode, modeContext);

        Assert.Contains("Narrative Director timed out", prompt);
        Assert.Contains("mode coordinator producing a fallback response", prompt);
        Assert.Contains("record it in the session canon for the next turn", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_RoleplayFallback_IncludesNarrativeContinuityContext()
    {
        var fake = new FakeCompletionService();
        var orchestrator = CreateOrchestrator(fake);

        var modeContext = new ModeContext
        {
            ProjectName = "caleb-test",
            CharacterSection = "Caleb is April's older brother.",
            StickySessionCanon = "- Zayne tutored April at Gran's house and therefore knew the address.\n- Anonymous racing forum users know April/Mildred/Gran's neighborhood.",
            RecentConversationSummary = "User: Zayne sent flowers.\nAssistant: The card reads only 'Mildred'.",
            DirectorNotes = "Keep the racing forum mystery separate from Zayne's known actions.",
            ActivePlotContent = "# Racing Forum Arc\n\n- Beat: anonymous users watching the house",
            PlotProgressSummary = "Current beat: forum-surveillance",
        };

        var roleplayMode = orchestrator.ResolveMode(Mode.Roleplay);
        var prompt = orchestrator.BuildSystemPrompt("Prelude.", roleplayMode, modeContext);

        // Regression: known-address fact and anonymity/forum fact must remain separate in prompt sections
        Assert.Contains("Sticky Session Canon", prompt);
        Assert.Contains("Zayne tutored April at Gran's house", prompt);
        Assert.Contains("Anonymous racing forum users know April/Mildred/Gran's neighborhood", prompt);
        Assert.Contains("Recent Session Conversation", prompt);
        Assert.Contains("Director Notes From Prior Turns", prompt);
        Assert.Contains("Active Plot Content", prompt);
        Assert.Contains("Plot Progress In This Session", prompt);
        Assert.Contains("Keep the racing forum mystery separate", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_WriterFallback_IncludesNarrativeContinuityContext()
    {
        var fake = new FakeCompletionService();
        var orchestrator = CreateOrchestrator(fake);

        var modeContext = new ModeContext
        {
            ProjectName = "moonfall",
            StickySessionCanon = "- Princess Ilya has the crescent moon blade.",
            RecentConversationSummary = "User: Describe the throne room.",
            DirectorNotes = "Build tension before the betrayal reveal.",
        };

        var writerMode = orchestrator.ResolveMode(Mode.Writer);
        var prompt = orchestrator.BuildSystemPrompt("Prelude.", writerMode, modeContext);

        Assert.Contains("Sticky Session Canon", prompt);
        Assert.Contains("Princess Ilya has the crescent moon blade", prompt);
        Assert.Contains("Recent Session Conversation", prompt);
        Assert.Contains("Director Notes From Prior Turns", prompt);
    }

    [Fact]
    public async Task HandleAsync_RoleplayMode_AllowsRecordSessionCorrectionAtTopLevel()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("Scene reply");
        var orchestrator = CreateOrchestrator(fake);
        var state = new SessionState
        {
            Mode = new ModeSelectionState { ActiveMode = Mode.Roleplay },
        };
        var context = new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = Mode.Roleplay };
        var tools = new IToolHandler[]
        {
            new StubToolHandler("direct_scene"),
            new StubToolHandler("record_session_correction"),
            new StubToolHandler("write_prose"),
            new StubToolHandler("update_story_state"),
            new StubToolHandler("update_narrative_state"),
        };

        await orchestrator.HandleAsync(
            state,
            "test-model",
            1024,
            tools,
            [new CompletionMessage("user", new MessageContent("continue the scene"))],
            context);

        var toolNames = fake.ReceivedRequests[0].Tools!.Select(t => t.Name).ToList();
        Assert.Contains("direct_scene", toolNames);
        Assert.Contains("record_session_correction", toolNames);
        Assert.DoesNotContain("write_prose", toolNames);
        Assert.DoesNotContain("update_story_state", toolNames);
        Assert.DoesNotContain("update_narrative_state", toolNames);
    }

    [Fact]
    public async Task HandleAsync_WriterMode_AllowsRecordSessionCorrectionAtTopLevel()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("Draft reply");
        var orchestrator = CreateOrchestrator(fake);
        var state = new SessionState
        {
            Mode = new ModeSelectionState { ActiveMode = Mode.Writer },
        };
        var context = new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = Mode.Writer };
        var tools = new IToolHandler[]
        {
            new StubToolHandler("direct_scene"),
            new StubToolHandler("record_session_correction"),
            new StubToolHandler("write_prose"),
            new StubToolHandler("update_story_state"),
            new StubToolHandler("update_narrative_state"),
        };

        await orchestrator.HandleAsync(
            state,
            "test-model",
            1024,
            tools,
            [new CompletionMessage("user", new MessageContent("draft this scene"))],
            context);

        var toolNames = fake.ReceivedRequests[0].Tools!.Select(t => t.Name).ToList();
        Assert.Contains("direct_scene", toolNames);
        Assert.Contains("record_session_correction", toolNames);
        Assert.DoesNotContain("write_prose", toolNames);
        Assert.DoesNotContain("update_story_state", toolNames);
        Assert.DoesNotContain("update_narrative_state", toolNames);
    }

    [Fact]
    public async Task HandleAsync_GuideMode_UsesAppOwnedPromptPrelude()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("Let's get you oriented.");
        var continuation = new ContinuationStrategy(LogFactory.CreateLogger<ContinuationStrategy>());
        var toolLoop = new ToolLoop(fake, continuation, LogFactory.CreateLogger<ToolLoop>(), new AppConfig());
        var orchestrator = new OrchestratorAgent(
            toolLoop,
            [new GuideMode(), new WriterMode(), new RoleplayMode(), new LoreBuilderMode(), new ForgeMode(), new CouncilMode(), new GamesMode()],
            new FakeAssistantPromptStore(),
            new FakeInteractiveSessionContextService(),
            new AppConfig(),
            LogFactory.CreateLogger<OrchestratorAgent>());

        var state = new SessionState();
        var messages = new List<CompletionMessage> { new("user", new MessageContent("hello")) };
        var context = new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = Mode.Guide };

        await orchestrator.HandleAsync(state, "test-model", 1024, [], messages, context);

        Assert.Contains("app-owned interactive coordinator", fake.ReceivedRequests[0].SystemPrompt!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Current Mode: Guide", fake.ReceivedRequests[0].SystemPrompt!);
    }

    [Fact]
    public async Task HandleAsync_CouncilMode_UsesAssistantPromptPrelude()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("Council synthesis");
        var assistantStore = new FakeAssistantPromptStore();
        var continuation = new ContinuationStrategy(LogFactory.CreateLogger<ContinuationStrategy>());
        var toolLoop = new ToolLoop(fake, continuation, LogFactory.CreateLogger<ToolLoop>(), new AppConfig());
        var orchestrator = new OrchestratorAgent(
            toolLoop,
            [new GuideMode(), new WriterMode(), new RoleplayMode(), new LoreBuilderMode(), new ForgeMode(), new CouncilMode(), new ResearchMode(), new GamesMode()],
            assistantStore,
            new FakeInteractiveSessionContextService(),
            new AppConfig(),
            LogFactory.CreateLogger<OrchestratorAgent>());

        var state = new SessionState
        {
            Mode = new ModeSelectionState { ActiveMode = Mode.Council },
        };
        var messages = new List<CompletionMessage> { new("user", new MessageContent("What should I do with this plot fork?")) };
        var context = new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = Mode.Council };

        await orchestrator.HandleAsync(state, "test-model", 1024, [], messages, context);

        Assert.Equal(1, assistantStore.LoadCallCount);
        Assert.Contains("Assistant surface for QuillForge", fake.ReceivedRequests[0].SystemPrompt!);
        Assert.Contains("Current Mode: Council", fake.ReceivedRequests[0].SystemPrompt!);
        Assert.Contains("synthesis-focused", fake.ReceivedRequests[0].SystemPrompt!);
    }

    [Fact]
    public async Task HandleAsync_ResearchMode_UsesAssistantPromptPrelude()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("Research synthesis");
        var assistantStore = new FakeAssistantPromptStore();
        var continuation = new ContinuationStrategy(LogFactory.CreateLogger<ContinuationStrategy>());
        var toolLoop = new ToolLoop(fake, continuation, LogFactory.CreateLogger<ToolLoop>(), new AppConfig());
        var orchestrator = new OrchestratorAgent(
            toolLoop,
            [new GuideMode(), new WriterMode(), new RoleplayMode(), new LoreBuilderMode(), new ForgeMode(), new CouncilMode(), new ResearchMode(), new GamesMode()],
            assistantStore,
            new FakeInteractiveSessionContextService(),
            new AppConfig(),
            LogFactory.CreateLogger<OrchestratorAgent>());

        var state = new SessionState
        {
            Mode = new ModeSelectionState { ActiveMode = Mode.Research },
        };
        var messages = new List<CompletionMessage> { new("user", new MessageContent("Research Byzantine harbor defenses")) };
        var context = new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = Mode.Research };

        await orchestrator.HandleAsync(state, "test-model", 1024, [], messages, context);

        Assert.Equal(1, assistantStore.LoadCallCount);
        Assert.Contains("Assistant surface for QuillForge", fake.ReceivedRequests[0].SystemPrompt!);
        Assert.Contains("Current Mode: Research", fake.ReceivedRequests[0].SystemPrompt!);
    }

    [Fact]
    public void GuideMode_Prompt_IsModeOrientedAndNonCreative()
    {
        var mode = new GuideMode();
        var prompt = mode.BuildSystemPromptSection(new ModeContext());

        Assert.Contains("front desk", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("switch into a task-specific mode", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Do not do creative writing", prompt);
        Assert.Contains("query_docs", prompt);
    }

    [Fact]
    public void CouncilMode_Prompt_RequiresOwningToolAndRejectsImpersonation()
    {
        var mode = new CouncilMode();
        var prompt = mode.BuildSystemPromptSection(new ModeContext());

        Assert.Contains("always call `run_council` before answering", prompt);
        Assert.Contains("Do not pretend to be the individual council members yourself.", prompt);
        Assert.Contains("Do not take over file or content-management work", prompt);
    }

    [Fact]
    public void ResearchMode_Prompt_RequiresOwningToolAndRejectsSelfResearch()
    {
        var mode = new ResearchMode();
        var prompt = mode.BuildSystemPromptSection(new ModeContext
        {
            ProjectName = "harbor-study",
        });

        Assert.Contains("always use `run_research` before answering", prompt);
        Assert.Contains("Do not browse or edit files directly from this mode.", prompt);
        Assert.Contains("Do not answer sourced factual questions from your own intuition", prompt);
        Assert.Contains("research/harbor-study/", prompt);
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
        Assert.Contains("If the user corrects canon or characterization", prompt);
    }

    [Fact]
    public void LoreBuilderMode_Prompt_IsLoreDocumentFocused()
    {
        var mode = new LoreBuilderMode();
        var prompt = mode.BuildSystemPromptSection(new ModeContext
        {
            ActiveLoreSet = "builder",
        });

        Assert.Contains("Current Mode: Lore Builder", prompt);
        Assert.Contains("lore/builder/", prompt);
        Assert.Contains("save_lore_file", prompt);
        Assert.Contains("web_search", prompt);
        Assert.Contains("Do not write story prose", prompt);
    }

    [Fact]
    public void ForgeMode_Prompt_IsCommandAndPipelineOwned()
    {
        var mode = new ForgeMode();
        var prompt = mode.BuildSystemPromptSection(new ModeContext
        {
            ProjectName = "archive-kingdom",
        });

        Assert.Contains("command- and pipeline-owned workflow", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/forge new", prompt);
        Assert.Contains("/forge design", prompt);
        Assert.Contains("Do NOT write premise, outline, brief, draft, review, or assembly content", prompt);
        Assert.Contains("Do NOT treat Forge mode like a second Writer mode", prompt);
    }

    [Fact]
    public void GamesMode_Prompt_IsTypedEndpointOwned()
    {
        var mode = new GamesMode();
        var prompt = mode.BuildSystemPromptSection(new ModeContext());

        Assert.Contains("typed game endpoints", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("not a game master", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("must not", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("workspace controls", prompt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_GamesMode_FiltersToDocumentationOnlyTools()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("Use the table controls.");
        var orchestrator = CreateOrchestrator(fake);
        var state = new SessionState
        {
            Mode = new ModeSelectionState { ActiveMode = Mode.Games },
        };
        var context = new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = Mode.Games };
        var tools = new IToolHandler[]
        {
            new StubToolHandler("query_docs"),
            new StubToolHandler("query_lore"),
            new StubToolHandler("direct_scene"),
            new StubToolHandler("write_prose"),
            new StubToolHandler("run_research"),
        };

        await orchestrator.HandleAsync(
            state,
            "test-model",
            1024,
            tools,
            [new CompletionMessage("user", new MessageContent("vote for Mira"))],
            context);

        var toolNames = fake.ReceivedRequests[0].Tools!.Select(t => t.Name).ToList();
        Assert.Equal(["query_docs"], toolNames);
        Assert.Contains("Current Mode: Games", fake.ReceivedRequests[0].SystemPrompt!);
    }

    [Fact]
    public async Task HandleAsync_WriterMode_FiltersTopLevelProseAndStateMutationTools()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("Draft reply");
        var orchestrator = CreateOrchestrator(fake);
        var state = new SessionState
        {
            Mode = new ModeSelectionState { ActiveMode = Mode.Writer },
        };
        var context = new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = Mode.Writer };
        var tools = new IToolHandler[]
        {
            new StubToolHandler("direct_scene"),
            new StubToolHandler("write_prose"),
            new StubToolHandler("update_story_state"),
            new StubToolHandler("update_narrative_state"),
            new StubToolHandler("query_lore"),
            new StubToolHandler("query_context"),
            new StubToolHandler("save_lore_file"),
        };

        await orchestrator.HandleAsync(
            state,
            "test-model",
            1024,
            tools,
            [new CompletionMessage("user", new MessageContent("draft this scene"))],
            context);

        var toolNames = fake.ReceivedRequests[0].Tools!.Select(t => t.Name).ToList();
        Assert.Contains("direct_scene", toolNames);
        Assert.Contains("query_lore", toolNames);
        Assert.Contains("query_context", toolNames);
        Assert.DoesNotContain("write_prose", toolNames);
        Assert.DoesNotContain("update_story_state", toolNames);
        Assert.DoesNotContain("update_narrative_state", toolNames);
        Assert.DoesNotContain("save_lore_file", toolNames);
    }

    [Fact]
    public async Task HandleAsync_RoleplayMode_FiltersTopLevelProseAndStateMutationTools()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("Scene reply");
        var orchestrator = CreateOrchestrator(fake);
        var state = new SessionState
        {
            Mode = new ModeSelectionState { ActiveMode = Mode.Roleplay },
        };
        var context = new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = Mode.Roleplay };
        var tools = new IToolHandler[]
        {
            new StubToolHandler("direct_scene"),
            new StubToolHandler("write_prose"),
            new StubToolHandler("update_story_state"),
            new StubToolHandler("update_narrative_state"),
            new StubToolHandler("roll_dice"),
            new StubToolHandler("query_context"),
            new StubToolHandler("save_lore_file"),
        };

        await orchestrator.HandleAsync(
            state,
            "test-model",
            1024,
            tools,
            [new CompletionMessage("user", new MessageContent("continue the scene"))],
            context);

        var toolNames = fake.ReceivedRequests[0].Tools!.Select(t => t.Name).ToList();
        Assert.Contains("direct_scene", toolNames);
        Assert.Contains("roll_dice", toolNames);
        Assert.Contains("query_context", toolNames);
        Assert.DoesNotContain("write_prose", toolNames);
        Assert.DoesNotContain("update_story_state", toolNames);
        Assert.DoesNotContain("update_narrative_state", toolNames);
        Assert.DoesNotContain("save_lore_file", toolNames);
    }

    [Fact]
    public async Task HandleAsync_LoreBuilderMode_FiltersToLoreBuildingTools()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("Lore helper reply");
        var orchestrator = CreateOrchestrator(fake);
        var state = new SessionState
        {
            Mode = new ModeSelectionState { ActiveMode = Mode.Lore },
        };
        var context = new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = Mode.Lore };
        var tools = new IToolHandler[]
        {
            new StubToolHandler("query_docs"),
            new StubToolHandler("query_context"),
            new StubToolHandler("query_lore"),
            new StubToolHandler("list_files"),
            new StubToolHandler("read_file"),
            new StubToolHandler("search_files"),
            new StubToolHandler("web_search"),
            new StubToolHandler("save_lore_file"),
            new StubToolHandler("direct_scene"),
            new StubToolHandler("write_prose"),
            new StubToolHandler("run_council"),
        };

        await orchestrator.HandleAsync(
            state,
            "test-model",
            1024,
            tools,
            [new CompletionMessage("user", new MessageContent("Help me create lore for Silverwatch"))],
            context);

        var toolNames = fake.ReceivedRequests[0].Tools!.Select(t => t.Name).ToList();
        Assert.Equal(
            ["query_docs", "query_context", "query_lore", "list_files", "read_file", "search_files", "web_search", "save_lore_file"],
            toolNames);
    }

    [Fact]
    public async Task HandleAsync_CouncilMode_FiltersToCouncilAndDocsTools()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("Council reply");
        var orchestrator = CreateOrchestrator(fake);
        var state = new SessionState
        {
            Mode = new ModeSelectionState { ActiveMode = Mode.Council },
        };
        var context = new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = Mode.Council };
        var tools = new IToolHandler[]
        {
            new StubToolHandler("run_council"),
            new StubToolHandler("query_docs"),
            new StubToolHandler("write_file"),
            new StubToolHandler("read_file"),
            new StubToolHandler("query_lore"),
        };

        await orchestrator.HandleAsync(
            state,
            "test-model",
            1024,
            tools,
            [new CompletionMessage("user", new MessageContent("Should these two characters marry?"))],
            context);

        var toolNames = fake.ReceivedRequests[0].Tools!.Select(t => t.Name).ToList();
        Assert.Equal(["run_council", "query_docs"], toolNames);
    }

    [Fact]
    public async Task HandleAsync_ResearchMode_FiltersToResearchAndDocsTools()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("Research reply");
        var orchestrator = CreateOrchestrator(fake);
        var state = new SessionState
        {
            Mode = new ModeSelectionState { ActiveMode = Mode.Research },
        };
        var context = new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = Mode.Research };
        var tools = new IToolHandler[]
        {
            new StubToolHandler("run_research"),
            new StubToolHandler("query_docs"),
            new StubToolHandler("web_search"),
            new StubToolHandler("write_file"),
            new StubToolHandler("read_file"),
        };

        await orchestrator.HandleAsync(
            state,
            "test-model",
            1024,
            tools,
            [new CompletionMessage("user", new MessageContent("Research medieval port taxes"))],
            context);

        var toolNames = fake.ReceivedRequests[0].Tools!.Select(t => t.Name).ToList();
        Assert.Equal(["run_research", "query_docs"], toolNames);
    }

    [Fact]
    public async Task HandleAsync_ForgeMode_FiltersToDocsAndReadOnlyInspectionTools()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("Forge helper reply");
        var orchestrator = CreateOrchestrator(fake);
        var state = new SessionState
        {
            Mode = new ModeSelectionState { ActiveMode = Mode.Forge },
        };
        var context = new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = Mode.Forge };
        var tools = new IToolHandler[]
        {
            new StubToolHandler("query_docs"),
            new StubToolHandler("list_files"),
            new StubToolHandler("read_file"),
            new StubToolHandler("search_files"),
            new StubToolHandler("write_file"),
            new StubToolHandler("direct_scene"),
            new StubToolHandler("write_prose"),
            new StubToolHandler("query_lore"),
        };

        await orchestrator.HandleAsync(
            state,
            "test-model",
            1024,
            tools,
            [new CompletionMessage("user", new MessageContent("How do I resume this forge project?"))],
            context);

        var toolNames = fake.ReceivedRequests[0].Tools!.Select(t => t.Name).ToList();
        Assert.Equal(["query_docs", "list_files", "read_file", "search_files"], toolNames);
    }

    [Fact]
    public async Task HandleAsync_GuideMode_FiltersOutReadFileAndKeepsDocsAndDiscoveryTools()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("Guide helper reply");
        var orchestrator = CreateOrchestrator(fake);
        var state = new SessionState
        {
            Mode = new ModeSelectionState { ActiveMode = Mode.Guide },
        };
        var context = new AgentContext { SessionId = Guid.CreateVersion7(), ActiveMode = Mode.Guide };
        var tools = new IToolHandler[]
        {
            new StubToolHandler("query_docs"),
            new StubToolHandler("list_files"),
            new StubToolHandler("read_file"),
            new StubToolHandler("search_files"),
            new StubToolHandler("write_file"),
            new StubToolHandler("direct_scene"),
            new StubToolHandler("run_council"),
            new StubToolHandler("run_research"),
        };

        await orchestrator.HandleAsync(
            state,
            "test-model",
            1024,
            tools,
            [new CompletionMessage("user", new MessageContent("How is this app organized?"))],
            context);

        var toolNames = fake.ReceivedRequests[0].Tools!.Select(t => t.Name).ToList();
        Assert.Equal(["query_docs", "list_files", "search_files"], toolNames);
    }
}

/// <summary>
internal sealed class FakeAssistantPromptStore : IAssistantPromptStore
{
    public int LoadCallCount { get; private set; }

    public Task<string> LoadAsync(string promptName, CancellationToken ct = default)
    {
        LoadCallCount++;
        return Task.FromResult("Be warm, calm, and synthesis-focused.");
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(["default"]);
    }
}

internal sealed class StubToolHandler : IToolHandler
{
    public StubToolHandler(string name)
    {
        Name = name;
        Definition = new ToolDefinition(name, $"Stub for {name}", System.Text.Json.JsonDocument.Parse("""{"type":"object"}""").RootElement);
    }

    public string Name { get; }
    public ToolDefinition Definition { get; }

    public Task<ToolResult> HandleAsync(ToolInput input, AgentContext context, CancellationToken ct = default)
    {
        return Task.FromResult(ToolResult.Ok("{}"));
    }
}
