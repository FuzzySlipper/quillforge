using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Agents;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Agents.Tools;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Core.Tests.Fakes;

namespace QuillForge.Core.Tests;

public sealed class NarrativeDirectorAgentTests
{
    [Fact]
    public async Task DirectSceneAsync_BuildsPromptWithNarrativeRulesAndSessionContext()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("The captain steps aside and lets her enter.");

        var continuation = new ContinuationStrategy(NullLogger<ContinuationStrategy>.Instance);
        var toolLoop = new ToolLoop(fake, continuation, NullLogger<ToolLoop>.Instance, new AppConfig());
        var loreStore = new ConfigurableLoreStore(new Dictionary<string, string>
        {
            ["gate.md"] = "The city gate closes at curfew.",
        });
        var fileService = new FakeContentFileService();
        var guard = new CanonPrerequisiteGuard(
            loreStore,
            fileService,
            new FakeNarrativeRulesStore(),
            new FakeWritingStyleStore(),
            NullLogger<CanonPrerequisiteGuard>.Instance);
        var agent = new NarrativeDirectorAgent(
            toolLoop,
            new QueryLoreHandler(null!, loreStore, fileService, guard, NullLogger<QueryLoreHandler>.Instance),
            new UpdateStoryStateHandler(new TrackingStoryStateService(), new FakeInteractiveSessionContextService(), NullLogger<UpdateStoryStateHandler>.Instance),
            new UpdateNarrativeStateHandler(new FakeSessionRuntimeService(), NullLogger<UpdateNarrativeStateHandler>.Instance),
            new WriteProseHandler(null!, new FakeInteractiveSessionContextService(), new TrackingStoryStateService(), NullLogger<WriteProseHandler>.Instance),
            guard,
            new FakeNarrativeRulesStore(),
            new AppConfig(),
            NullLogger<NarrativeDirectorAgent>.Instance);

        var result = await agent.DirectSceneAsync(
            new NarrativeDirectionRequest
            {
                UserMessage = "I ask the captain to open the gate.",
            },
            new AgentContext
            {
                SessionId = Guid.CreateVersion7(),
                ActiveMode = Mode.Roleplay,
                ActiveLoreSet = "default",
                ActiveNarrativeRules = "default",
                LastAssistantResponse = "The captain narrows his eyes and keeps the gate shut.",
                SessionContext = new InteractiveSessionContext
                {
                    ActiveMode = Mode.Roleplay,
                    ProjectName = "gatehouse",
                    CurrentFile = "chapter-01.md",
                    CharacterSection = "Captain Elian guards the city gate.",
                    StoryStateSummary = "The gate is closed due to curfew.",
                    StoryStatePath = "gatehouse/.state.yaml",
                    DirectorNotes = "The captain is suspicious but not hostile.",
                    StickySessionCanon = "- Captain Elian believes the visitor is hiding something.",
                    RecentConversationSummary = "User: I mention Captain Rowe and the tide tunnels.\nAssistant: Elian's grip tightens on the key ring.",
                    ActivePlotFile = "gate-arc",
                    ActivePlotContent = "# Gate Arc\n\n- Beat: let the guard test her resolve.",
                    PlotProgressSummary = "Current beat: gate-confrontation",
                },
            });

        Assert.Equal("The captain steps aside and lets her enter.", result.ResponseText);

        var request = fake.ReceivedRequests.Single();
        Assert.Contains("Narrative Director", request.SystemPrompt!);
        Assert.Contains("Let user actions matter", request.SystemPrompt!);
        Assert.Contains("curfew", request.SystemPrompt!);
        Assert.Contains("Captain Elian", request.SystemPrompt!);
        Assert.Contains("suspicious but not hostile", request.SystemPrompt!);
        Assert.Contains("Sticky Session Canon", request.SystemPrompt!);
        Assert.Contains("Captain Elian believes the visitor is hiding something.", request.SystemPrompt!);
        Assert.Contains("Recent Session Conversation", request.SystemPrompt!);
        Assert.Contains("Captain Rowe and the tide tunnels", request.SystemPrompt!);
        Assert.Contains("Gate Arc", request.SystemPrompt!);
        Assert.Contains("Current beat: gate-confrontation", request.SystemPrompt!);
        Assert.Contains("keeps the gate shut", request.Messages.Single().Content.GetText());
        Assert.Contains("write_prose", request.Tools!.Select(t => t.Name));
        Assert.Contains("update_narrative_state", request.Tools!.Select(t => t.Name));
    }

    [Fact]
    public async Task DirectSceneAsync_MultiTurnStickyCanonCarriesIntoLaterPrompt()
    {
        var sessionId = Guid.CreateVersion7();
        var fake = new FakeCompletionService();
        fake.EnqueueToolCall(
            "update_narrative_state",
            "call_1",
            """{"director_notes":"Rowan is cautious but no longer deflecting.","sticky_session_canon":"- Captain Rowe suspects contraband in the tide tunnels.\n- Rowan still carries the lighthouse keeper's ring."}""");
        fake.EnqueueText("Rowan lowers his voice and admits he has already seen the trapdoor used.");
        fake.EnqueueText("At Captain Rowe's name, Rowan touches the ring and glances toward the tide tunnels.");

        var runtimeStore = new InMemorySessionRuntimeStore();
        await runtimeStore.SaveAsync(new SessionState
        {
            SessionId = sessionId,
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Roleplay,
                ProjectName = "harbor",
                CurrentFile = "scene-01.md",
            },
        });

        var runtimeService = new SessionRuntimeService(
            runtimeStore,
            new InMemorySessionMutationGate(NullLogger<InMemorySessionMutationGate>.Instance),
            new FakeProfileConfigService(),
            new InMemoryStoryStore(),
            [new GuideMode(), new WriterMode(), new RoleplayMode(), new LoreBuilderMode(), new ForgeMode(), new CouncilMode()],
            NullLogger<SessionRuntimeService>.Instance);

        var sessionStore = new InMemoryInteractiveSessionStore();
        var tree = new ConversationTree(sessionId, "Harbor Session", NullLogger<ConversationTree>.Instance);
        tree.Append(
            tree.RootId,
            "user",
            new MessageContent("Captain Rowe thinks someone is moving contraband through the tide tunnels."));
        await sessionStore.SaveAsync(tree);

        var contextService = new InteractiveSessionContextService(
            runtimeService,
            sessionStore,
            new FakeCharacterCardStoreForContext(),
            new StoryStateServiceWithData(new Dictionary<string, object>()),
            new FakeContentFileService(),
            new FakePlotStore(),
            NullLogger<InteractiveSessionContextService>.Instance);

        var continuation = new ContinuationStrategy(NullLogger<ContinuationStrategy>.Instance);
        var toolLoop = new ToolLoop(fake, continuation, NullLogger<ToolLoop>.Instance, new AppConfig());
        var loreStore = new ConfigurableLoreStore(new Dictionary<string, string>
        {
            ["harbor.md"] = "The tide tunnels run beneath the old harbor.",
        });
        var fileService = new FakeContentFileService();
        var guard = new CanonPrerequisiteGuard(
            loreStore,
            fileService,
            new FakeNarrativeRulesStore(),
            new FakeWritingStyleStore(),
            NullLogger<CanonPrerequisiteGuard>.Instance);
        var agent = new NarrativeDirectorAgent(
            toolLoop,
            new QueryLoreHandler(null!, loreStore, fileService, guard, NullLogger<QueryLoreHandler>.Instance),
            new UpdateStoryStateHandler(new TrackingStoryStateService(), new FakeInteractiveSessionContextService(), NullLogger<UpdateStoryStateHandler>.Instance),
            new UpdateNarrativeStateHandler(runtimeService, NullLogger<UpdateNarrativeStateHandler>.Instance),
            new WriteProseHandler(null!, new FakeInteractiveSessionContextService(), new TrackingStoryStateService(), NullLogger<WriteProseHandler>.Instance),
            guard,
            new FakeNarrativeRulesStore(),
            new AppConfig(),
            NullLogger<NarrativeDirectorAgent>.Instance);

        var firstTurnContext = await contextService.LoadAsync(sessionId);
        var firstTurn = await agent.DirectSceneAsync(
            new NarrativeDirectionRequest
            {
                UserMessage = "How does Rowan react when I mention Captain Rowe and the tide tunnels?",
            },
            new AgentContext
            {
                SessionId = sessionId,
                ActiveMode = Mode.Roleplay,
                ActiveLoreSet = "default",
                ActiveNarrativeRules = "default",
                SessionContext = firstTurnContext,
            });

        var runtimeAfterFirstTurn = await runtimeStore.LoadAsync(sessionId);
        Assert.Contains("Captain Rowe suspects contraband", runtimeAfterFirstTurn.Narrative.StickySessionCanon);

        var updatedTree = await sessionStore.LoadAsync(sessionId);
        updatedTree.Append(
            updatedTree.ActiveLeafId,
            "assistant",
            new MessageContent(firstTurn.ResponseText),
            new MessageMetadata
            {
                ConversationMode = Mode.Roleplay,
            });
        updatedTree.Append(
            updatedTree.ActiveLeafId,
            "user",
            new MessageContent("Before we move, I ask Rowan whose side he is really on."));
        await sessionStore.SaveAsync(updatedTree);

        var secondTurnContext = await contextService.LoadAsync(sessionId);
        Assert.Contains("lighthouse keeper's ring", secondTurnContext.StickySessionCanon);

        await agent.DirectSceneAsync(
            new NarrativeDirectionRequest
            {
                UserMessage = "Before we move, I ask Rowan whose side he is really on.",
            },
            new AgentContext
            {
                SessionId = sessionId,
                ActiveMode = Mode.Roleplay,
                ActiveLoreSet = "default",
                ActiveNarrativeRules = "default",
                LastAssistantResponse = firstTurn.ResponseText,
                SessionContext = secondTurnContext,
            });

        var secondTurnRequest = fake.ReceivedRequests[2];
        Assert.Contains("Sticky Session Canon", secondTurnRequest.SystemPrompt!);
        Assert.Contains("Captain Rowe suspects contraband in the tide tunnels.", secondTurnRequest.SystemPrompt!);
        Assert.Contains("Rowan still carries the lighthouse keeper's ring.", secondTurnRequest.SystemPrompt!);
        Assert.Contains("Recent Session Conversation", secondTurnRequest.SystemPrompt!);
        Assert.Contains("Captain Rowe thinks someone is moving contraband", secondTurnRequest.SystemPrompt!);
        Assert.Contains(firstTurn.ResponseText, secondTurnRequest.SystemPrompt!);
    }

    [Fact]
    public async Task GeneratePlotAsync_BuildsReusablePlotMarkdown()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("# Moonfall Arc\n\n## Premise\nThe court turns on itself.");

        var continuation = new ContinuationStrategy(NullLogger<ContinuationStrategy>.Instance);
        var toolLoop = new ToolLoop(fake, continuation, NullLogger<ToolLoop>.Instance, new AppConfig());
        var loreStore = new ConfigurableLoreStore(new Dictionary<string, string>
        {
            ["court.md"] = "The court is hungry for scandal.",
        });
        var fileService = new FakeContentFileService();
        var guard = new CanonPrerequisiteGuard(
            loreStore,
            fileService,
            new FakeNarrativeRulesStore(),
            new FakeWritingStyleStore(),
            NullLogger<CanonPrerequisiteGuard>.Instance);
        var agent = new NarrativeDirectorAgent(
            toolLoop,
            new QueryLoreHandler(null!, loreStore, fileService, guard, NullLogger<QueryLoreHandler>.Instance),
            new UpdateStoryStateHandler(new TrackingStoryStateService(), new FakeInteractiveSessionContextService(), NullLogger<UpdateStoryStateHandler>.Instance),
            new UpdateNarrativeStateHandler(new FakeSessionRuntimeService(), NullLogger<UpdateNarrativeStateHandler>.Instance),
            new WriteProseHandler(null!, new FakeInteractiveSessionContextService(), new TrackingStoryStateService(), NullLogger<WriteProseHandler>.Instance),
            guard,
            new FakeNarrativeRulesStore(),
            new AppConfig(),
            NullLogger<NarrativeDirectorAgent>.Instance);

        var result = await agent.GeneratePlotAsync(
            new PlotGenerationRequest { Prompt = "court intrigue tragedy" },
            new AgentContext
            {
                SessionId = Guid.CreateVersion7(),
                ActiveMode = Mode.Roleplay,
                ActiveLoreSet = "default",
                ActiveNarrativeRules = "default",
                SessionContext = new InteractiveSessionContext
                {
                    ActiveMode = Mode.Roleplay,
                    ProjectName = "moonfall",
                    StoryStatePath = "moonfall/.state.yaml",
                    CharacterSection = "Princess Ilya is brilliant and reckless.",
                },
            });

        Assert.Contains("Moonfall Arc", result.Markdown);
        var request = fake.ReceivedRequests.Single();
        Assert.Contains("reusable plot arc document", request.SystemPrompt!);
        Assert.Contains("Princess Ilya", request.SystemPrompt!);
        Assert.Contains("court intrigue tragedy", request.Messages.Single().Content.GetText());
        Assert.Contains("query_lore", request.Tools!.Select(t => t.Name));
    }

    [Fact]
    public async Task DirectSceneAsync_WriterMode_UsesGroundedDraftingPrompt()
    {
        var fake = new FakeCompletionService();
        fake.EnqueueText("She finally enters the archive at dawn.");

        var continuation = new ContinuationStrategy(NullLogger<ContinuationStrategy>.Instance);
        var toolLoop = new ToolLoop(fake, continuation, NullLogger<ToolLoop>.Instance, new AppConfig());
        var loreStore = new ConfigurableLoreStore(new Dictionary<string, string>
        {
            ["archive.md"] = "The archive opens at dawn.",
        });
        var fileService = new FakeContentFileService();
        var guard = new CanonPrerequisiteGuard(
            loreStore,
            fileService,
            new FakeNarrativeRulesStore(),
            new FakeWritingStyleStore(),
            NullLogger<CanonPrerequisiteGuard>.Instance);
        var agent = new NarrativeDirectorAgent(
            toolLoop,
            new QueryLoreHandler(null!, loreStore, fileService, guard, NullLogger<QueryLoreHandler>.Instance),
            new UpdateStoryStateHandler(new TrackingStoryStateService(), new FakeInteractiveSessionContextService(), NullLogger<UpdateStoryStateHandler>.Instance),
            new UpdateNarrativeStateHandler(new FakeSessionRuntimeService(), NullLogger<UpdateNarrativeStateHandler>.Instance),
            new WriteProseHandler(null!, new FakeInteractiveSessionContextService(), new TrackingStoryStateService(), NullLogger<WriteProseHandler>.Instance),
            guard,
            new FakeNarrativeRulesStore(),
            new AppConfig(),
            NullLogger<NarrativeDirectorAgent>.Instance);

        await agent.DirectSceneAsync(
            new NarrativeDirectionRequest
            {
                UserMessage = "Draft the moment she finally enters the archive.",
            },
            new AgentContext
            {
                SessionId = Guid.CreateVersion7(),
                ActiveMode = Mode.Writer,
                ActiveLoreSet = "default",
                ActiveNarrativeRules = "default",
                LastAssistantResponse = "She stops outside the door, unsure whether to continue.",
                SessionContext = new InteractiveSessionContext
                {
                    ActiveMode = Mode.Writer,
                    ProjectName = "archive-novel",
                    CurrentFile = "chapter-03.md",
                    StoryStatePath = "archive-novel/.state.yaml",
                },
            });

        var request = fake.ReceivedRequests.Single();
        Assert.Contains("For writer turns, the final response must be only the grounded draft prose", request.SystemPrompt!);
        Assert.Contains("grounded writing turn", request.Messages.Single().Content.GetText());
        Assert.Contains("chapter-03.md", request.Messages.Single().Content.GetText());
        Assert.Contains("shown to the user for review", request.Messages.Single().Content.GetText());
    }

    [Fact]
    public async Task DirectSceneAsync_ThrowsWhenNarrativeRulesAreMissing()
    {
        var fake = new FakeCompletionService();
        var continuation = new ContinuationStrategy(NullLogger<ContinuationStrategy>.Instance);
        var toolLoop = new ToolLoop(fake, continuation, NullLogger<ToolLoop>.Instance, new AppConfig());
        var loreStore = new ConfigurableLoreStore(new Dictionary<string, string>
        {
            ["scene.md"] = "Canon exists.",
        });
        var fileService = new FakeContentFileService();
        var guard = new CanonPrerequisiteGuard(
            loreStore,
            fileService,
            new FakeNarrativeRulesStore(""),
            new FakeWritingStyleStore(),
            NullLogger<CanonPrerequisiteGuard>.Instance);
        var agent = new NarrativeDirectorAgent(
            toolLoop,
            new QueryLoreHandler(null!, loreStore, fileService, guard, NullLogger<QueryLoreHandler>.Instance),
            new UpdateStoryStateHandler(new TrackingStoryStateService(), new FakeInteractiveSessionContextService(), NullLogger<UpdateStoryStateHandler>.Instance),
            new UpdateNarrativeStateHandler(new FakeSessionRuntimeService(), NullLogger<UpdateNarrativeStateHandler>.Instance),
            new WriteProseHandler(null!, new FakeInteractiveSessionContextService(), new TrackingStoryStateService(), NullLogger<WriteProseHandler>.Instance),
            guard,
            new FakeNarrativeRulesStore(""),
            new AppConfig(),
            NullLogger<NarrativeDirectorAgent>.Instance);

        var ex = await Assert.ThrowsAsync<CanonPrerequisiteException>(() =>
            agent.DirectSceneAsync(
                new NarrativeDirectionRequest { UserMessage = "Continue the scene." },
                new AgentContext
                {
                    SessionId = Guid.CreateVersion7(),
                    ActiveMode = Mode.Writer,
                    ActiveLoreSet = "default",
                    ActiveNarrativeRules = "missing-rules",
                    SessionContext = new InteractiveSessionContext
                    {
                        ActiveMode = Mode.Writer,
                        ProjectName = "novel",
                        StoryStatePath = "novel/.state.yaml",
                    },
                }));

        Assert.Contains("narrative-rules", ex.Message);
        Assert.Empty(fake.ReceivedRequests);
    }

    [Fact]
    public async Task DirectSceneAsync_ThrowsWhenLoreIsMissing()
    {
        var fake = new FakeCompletionService();
        var continuation = new ContinuationStrategy(NullLogger<ContinuationStrategy>.Instance);
        var toolLoop = new ToolLoop(fake, continuation, NullLogger<ToolLoop>.Instance, new AppConfig());
        var loreStore = new ConfigurableLoreStore();
        var fileService = new FakeContentFileService();
        var guard = new CanonPrerequisiteGuard(
            loreStore,
            fileService,
            new FakeNarrativeRulesStore(),
            new FakeWritingStyleStore(),
            NullLogger<CanonPrerequisiteGuard>.Instance);
        var agent = new NarrativeDirectorAgent(
            toolLoop,
            new QueryLoreHandler(null!, loreStore, fileService, guard, NullLogger<QueryLoreHandler>.Instance),
            new UpdateStoryStateHandler(new TrackingStoryStateService(), new FakeInteractiveSessionContextService(), NullLogger<UpdateStoryStateHandler>.Instance),
            new UpdateNarrativeStateHandler(new FakeSessionRuntimeService(), NullLogger<UpdateNarrativeStateHandler>.Instance),
            new WriteProseHandler(null!, new FakeInteractiveSessionContextService(), new TrackingStoryStateService(), NullLogger<WriteProseHandler>.Instance),
            guard,
            new FakeNarrativeRulesStore(),
            new AppConfig(),
            NullLogger<NarrativeDirectorAgent>.Instance);

        var ex = await Assert.ThrowsAsync<CanonPrerequisiteException>(() =>
            agent.DirectSceneAsync(
                new NarrativeDirectionRequest { UserMessage = "Continue the scene." },
                new AgentContext
                {
                    SessionId = Guid.CreateVersion7(),
                    ActiveMode = Mode.Writer,
                    ActiveLoreSet = "missing-lore",
                    ActiveNarrativeRules = "default",
                    SessionContext = new InteractiveSessionContext
                    {
                        ActiveMode = Mode.Writer,
                        ProjectName = "novel",
                        StoryStatePath = "novel/.state.yaml",
                    },
                }));

        Assert.Contains("active lore set", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("missing or empty", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(fake.ReceivedRequests);
    }

    [Fact]
    public async Task DirectSceneHandler_ReturnsFailure_WhenRoleplayCharacterContextIsMissing()
    {
        var fake = new FakeCompletionService();
        var continuation = new ContinuationStrategy(NullLogger<ContinuationStrategy>.Instance);
        var toolLoop = new ToolLoop(fake, continuation, NullLogger<ToolLoop>.Instance, new AppConfig());
        var loreStore = new ConfigurableLoreStore(new Dictionary<string, string>
        {
            ["scene.md"] = "Canon exists.",
        });
        var fileService = new FakeContentFileService();
        var guard = new CanonPrerequisiteGuard(
            loreStore,
            fileService,
            new FakeNarrativeRulesStore(),
            new FakeWritingStyleStore(),
            NullLogger<CanonPrerequisiteGuard>.Instance);
        var agent = new NarrativeDirectorAgent(
            toolLoop,
            new QueryLoreHandler(null!, loreStore, fileService, guard, NullLogger<QueryLoreHandler>.Instance),
            new UpdateStoryStateHandler(new TrackingStoryStateService(), new FakeInteractiveSessionContextService(), NullLogger<UpdateStoryStateHandler>.Instance),
            new UpdateNarrativeStateHandler(new FakeSessionRuntimeService(), NullLogger<UpdateNarrativeStateHandler>.Instance),
            new WriteProseHandler(null!, new FakeInteractiveSessionContextService(), new TrackingStoryStateService(), NullLogger<WriteProseHandler>.Instance),
            guard,
            new FakeNarrativeRulesStore(),
            new AppConfig(),
            NullLogger<NarrativeDirectorAgent>.Instance);
        var handler = new DirectSceneHandler(agent, NullLogger<DirectSceneHandler>.Instance);

        var result = await handler.HandleAsync(
            new ToolInput(System.Text.Json.JsonDocument.Parse("""{"user_message":"Continue the scene."}""").RootElement),
            new AgentContext
            {
                SessionId = Guid.CreateVersion7(),
                ActiveMode = Mode.Roleplay,
                ActiveLoreSet = "default",
                ActiveNarrativeRules = "default",
                SessionContext = new InteractiveSessionContext
                {
                    ActiveMode = Mode.Roleplay,
                    ProjectName = "novel",
                    StoryStatePath = "novel/.state.yaml",
                    Character = "missing-hero",
                    CharacterSection = null,
                },
            });

        Assert.False(result.Success);
        Assert.Contains("selected roleplay character", result.Error);
        Assert.Empty(fake.ReceivedRequests);
    }
}

internal sealed class FakeNarrativeRulesStore : INarrativeRulesStore
{
    private readonly string _content;

    public FakeNarrativeRulesStore(string content = "Keep tension rising. Let user actions matter.")
    {
        _content = content;
    }

    public Task<string> LoadAsync(string rulesName, CancellationToken ct = default)
    {
        return Task.FromResult(_content);
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(["default"]);
    }
}

internal sealed class ConfigurableLoreStore : ILoreStore
{
    private readonly IReadOnlyDictionary<string, string> _content;

    public ConfigurableLoreStore(IReadOnlyDictionary<string, string>? content = null)
    {
        _content = content ?? new Dictionary<string, string>();
    }

    public Task<IReadOnlyDictionary<string, string>> LoadLoreSetAsync(string loreSetName, CancellationToken ct = default)
    {
        return Task.FromResult(_content);
    }

    public Task<IReadOnlyList<string>> ListLoreSetsAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>([]);
    }

    public Task<IReadOnlyList<(string FilePath, string Snippet)>> SearchAsync(string loreSetName, string query, CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<(string FilePath, string Snippet)>>([]);
    }
}

internal sealed class FakeWritingStyleStore : IWritingStyleStore
{
    private readonly string _content;

    public FakeWritingStyleStore(string content = "Write with clarity and grounded emotional detail.")
    {
        _content = content;
    }

    public Task<string> LoadAsync(string styleName, CancellationToken ct = default)
    {
        return Task.FromResult(_content);
    }

    public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default)
    {
        return Task.FromResult<IReadOnlyList<string>>(["default"]);
    }
}

internal sealed class FakeSessionRuntimeService : ISessionStateService
{
    public Task<SessionMutationResult<WriterPendingContentAcceptedEvent>> AcceptWriterPendingAsync(Guid? sessionId, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task<SessionMutationResult<WriterPendingCaptureEvent>> CaptureWriterPendingAsync(Guid? sessionId, CaptureWriterPendingCommand command, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task<SessionState> LoadViewAsync(Guid? sessionId, CancellationToken ct = default)
    {
        return Task.FromResult(new SessionState { SessionId = sessionId });
    }

    public Task<SessionMutationResult<SessionState>> SetProfileAsync(Guid? sessionId, SetSessionProfileCommand command, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task<SessionMutationResult<WriterPendingContentRejectedEvent>> RejectWriterPendingAsync(Guid? sessionId, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task<SessionMutationResult<SessionState>> SetRoleplayAsync(Guid? sessionId, SetSessionRoleplayCommand command, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task<SessionMutationResult<SessionState>> SetModeAsync(Guid? sessionId, SetSessionModeCommand command, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task<SessionMutationResult<SessionState>> SetActivePlotAsync(Guid? sessionId, SetActivePlotCommand command, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task<SessionMutationResult<SessionState>> ClearActivePlotAsync(Guid? sessionId, CancellationToken ct = default)
    {
        throw new NotSupportedException();
    }

    public Task<SessionMutationResult<SessionState>> UpdateNarrativeStateAsync(Guid? sessionId, UpdateNarrativeStateCommand command, CancellationToken ct = default)
    {
        return Task.FromResult(SessionMutationResult<SessionState>.Success(
            new SessionState
            {
                SessionId = sessionId,
                Narrative = new NarrativeRuntimeState
                {
                    DirectorNotes = command.DirectorNotes,
                    StickySessionCanon = command.StickySessionCanon,
                    ActivePlotFile = command.ActivePlotFile,
                    PlotProgress = new PlotProgressState
                    {
                        CurrentBeat = command.PlotProgress?.CurrentBeat,
                        CompletedBeats = command.PlotProgress?.CompletedBeats?.ToList() ?? [],
                        Deviations = command.PlotProgress?.Deviations?.ToList() ?? [],
                    },
                },
            }));
    }
}
