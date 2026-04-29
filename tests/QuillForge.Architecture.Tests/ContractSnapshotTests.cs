using System.Text.Json;
using System.Text.Json.Serialization;
using Den.RulesEngine;
using Den.RulesEngine.Werewolf;
using QuillForge.Core.Models;
using QuillForge.Web.Contracts;
using QuillForge.Web.Services;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace QuillForge.Architecture.Tests;

/// <summary>
/// Golden-file snapshot tests for serialized shapes of API responses, SSE events,
/// session state, and profile config.
///
/// These tests detect accidental changes to wire-format shapes that would break
/// the frontend, debug bridge callers, or persisted session/profile files.
///
/// Update workflow:
///   1. Delete the relevant .approved.json (or .approved.yaml) file in Snapshots/.
///   2. Re-run the test. It will create a new golden file from the current shape.
///   3. Review the new file, commit it, and the test locks that shape going forward.
///
/// To intentionally change a shape:
///   1. Make the code change.
///   2. Run the test — it will fail with a diff showing old vs new.
///   3. Delete the old .approved file and re-run to regenerate.
///   4. Commit the updated golden file alongside the code change.
/// </summary>
public sealed class ContractSnapshotTests
{
    // -----------------------------------------------------------------------
    // JSON options matching the SSE serialization in ChatEndpoints.cs
    // -----------------------------------------------------------------------
    private static readonly JsonSerializerOptions SseJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    // -----------------------------------------------------------------------
    // JSON options matching FileSystemSessionRuntimeStore serialization
    // -----------------------------------------------------------------------
    private static readonly JsonSerializerOptions SessionStateJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    // -----------------------------------------------------------------------
    // JSON options matching the debug bridge (Web defaults = camelCase)
    // -----------------------------------------------------------------------
    private static readonly JsonSerializerOptions DebugBridgeJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    // -----------------------------------------------------------------------
    // YAML serializer matching FileSystemProfileConfigStore
    // -----------------------------------------------------------------------
    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    private static readonly string SnapshotsDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Snapshots"));

    // =======================================================================
    // SSE Event Shapes
    // =======================================================================

    [Fact]
    public void ReasoningArtifactDto_MatchesApprovedSnapshot()
    {
        var dto = new ReasoningArtifactDto
        {
            AgentId = "prose-writer",
            AgentLabel = "Prose Writer",
            Content = "Keep the voice intimate and grounded.",
            Sequence = 2,
        };
        AssertJsonSnapshot("ReasoningArtifactDto", dto, SseJsonOptions);
    }

    [Fact]
    public void ChatTextDeltaDto_MatchesApprovedSnapshot()
    {
        var dto = new ChatTextDeltaDto
        {
            Text = "Once upon a time in a land far away",
        };
        AssertJsonSnapshot("ChatTextDeltaDto", dto, SseJsonOptions);
    }

    [Fact]
    public void ChatToolDto_MatchesApprovedSnapshot()
    {
        var dto = new ChatToolDto
        {
            Name = "query_lore",
            Id = "tool_call_001",
        };
        AssertJsonSnapshot("ChatToolDto", dto, SseJsonOptions);
    }

    [Fact]
    public void ChatDoneDto_MatchesApprovedSnapshot()
    {
        var dto = new ChatDoneDto
        {
            SessionId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
            ParentId = Guid.Parse("fedcba98-7654-3210-fedc-ba9876543210"),
            Content = "The dragon breathed fire across the valley.",
            StopReason = "end_turn",
            ResponseType = "Standard",
            Usage = new ChatUsageDto { Input = 1500, Output = 350 },
            SessionUsage = new SessionUsageDto
            {
                TotalInput = 3000,
                TotalOutput = 700,
                TotalRequests = 2,
                ByAgent = [new AgentUsageDto { Agent = "orchestrator", Input = 1500, Output = 350, Requests = 1 },
                           new AgentUsageDto { Agent = "librarian", Input = 1500, Output = 350, Requests = 1 }],
            },
            Portrait = "/portraits/ai-guide.png",
            UserPortrait = "/portraits/user-avatar.png",
            Reasoning = "I should surface the old prophecy first.",
            ReasoningArtifacts =
            [
                new ReasoningArtifactDto
                {
                    AgentId = "orchestrator",
                    AgentLabel = "Orchestrator",
                    Content = "I should surface the old prophecy first.",
                    Sequence = 0,
                },
                new ReasoningArtifactDto
                {
                    AgentId = "prose-writer",
                    AgentLabel = "Prose Writer",
                    Content = "Lead with the valley fire.",
                    Sequence = 1,
                },
            ],
        };
        AssertJsonSnapshot("ChatDoneDto", dto, SseJsonOptions);
    }

    [Fact]
    public void ChatReasoningDeltaDto_MatchesApprovedSnapshot()
    {
        var dto = new ChatReasoningDeltaDto
        {
            Text = "I should consider the lore context before responding...",
        };
        AssertJsonSnapshot("ChatReasoningDeltaDto", dto, SseJsonOptions);
    }

    [Fact]
    public void ChatDiagnosticDto_MatchesApprovedSnapshot()
    {
        var dto = new ChatDiagnosticDto
        {
            Category = "tool_dispatch",
            Message = "query_lore returned 3 results in 120ms",
            Level = "info",
        };
        AssertJsonSnapshot("ChatDiagnosticDto", dto, SseJsonOptions);
    }

    [Fact]
    public void ChatPersistedDto_MatchesApprovedSnapshot()
    {
        var dto = new ChatPersistedDto
        {
            NodeId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            UserNodeId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
        };
        AssertJsonSnapshot("ChatPersistedDto", dto, SseJsonOptions);
    }

    // =======================================================================
    // Debug Bridge Response Shapes
    // =======================================================================

    [Fact]
    public void DebugBridgeChatResponse_MatchesApprovedSnapshot()
    {
        var dto = new DebugBridgeChatResponse
        {
            SessionId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
            ResponseText = "The ancient tome reveals a prophecy about the northern keep.",
            StopReason = "end_turn",
            ToolRoundsUsed = 2,
            Usage = new DebugBridgeUsageDto { InputTokens = 2400, OutputTokens = 180 },
            Mode = "writer",
            MessageCount = 6,
            Reasoning = "The omen matters more than the wall carvings.",
            ReasoningArtifacts =
            [
                new ReasoningArtifactDto
                {
                    AgentId = "narrative-director",
                    AgentLabel = "Narrative Director",
                    Content = "The omen matters more than the wall carvings.",
                    Sequence = 0,
                },
            ],
        };
        AssertJsonSnapshot("DebugBridgeChatResponse", dto, DebugBridgeJsonOptions);
    }

    [Fact]
    public void DebugBridgeSessionResponse_MatchesApprovedSnapshot()
    {
        var dto = new DebugBridgeSessionResponse
        {
            SessionId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
            Name = "Guided Session",
            MessageCount = 2,
            Messages =
            [
                new DebugBridgeMessageDto
                {
                    Id = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                    Role = "assistant",
                    Content = "The archive opens at dawn.",
                    CreatedAt = DateTimeOffset.Parse("2026-03-15T14:31:00+00:00"),
                    Reasoning = "Lead with the concrete answer.",
                    ReasoningArtifacts =
                    [
                        new ReasoningArtifactDto
                        {
                            AgentId = "assistant",
                            AgentLabel = "Assistant",
                            Content = "Lead with the concrete answer.",
                            Sequence = 0,
                        },
                    ],
                },
            ],
        };
        AssertJsonSnapshot("DebugBridgeSessionResponse", dto, DebugBridgeJsonOptions);
    }

    [Fact]
    public void DebugBridgeModeResponse_MatchesApprovedSnapshot()
    {
        var dto = new DebugBridgeModeResponse
        {
            SessionId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
            Mode = "forge",
            Project = "novel-project",
            File = "chapter-03.md",
        };
        AssertJsonSnapshot("DebugBridgeModeResponse", dto, DebugBridgeJsonOptions);
    }

    [Fact]
    public void ForgeStatusResponse_MatchesApprovedSnapshot()
    {
        var dto = new ForgeStatusResponse
        {
            ProjectName = "ember-archive",
            Stage = "Writing",
            ChapterCount = 2,
            Paused = false,
            Chapters = new Dictionary<string, ForgeChapterStatusDto>
            {
                ["ch-01"] = new()
                {
                    State = "Done",
                    RevisionCount = 1,
                    WordCount = 2500,
                },
                ["ch-02"] = new()
                {
                    State = "Writing",
                    RevisionCount = 0,
                    WordCount = 800,
                },
            },
            Stats = new ForgeStats
            {
                TotalInputTokens = 3000,
                TotalOutputTokens = 1200,
                AgentCalls = 4,
                ChaptersRevised = 1,
            },
            Documents =
            [
                new ForgeProjectDocumentDto
                {
                    Kind = "outline",
                    Label = "Outline",
                    RelativePath = "forge/ember-archive/plan/outline.md",
                    Href = "/content/forge/ember-archive/plan/outline.md",
                },
                new ForgeProjectDocumentDto
                {
                    Kind = "outputStory",
                    Label = "Output story",
                    RelativePath = "forge/ember-archive/output/story.md",
                    Href = "/content/forge/ember-archive/output/story.md",
                },
            ],
        };
        AssertJsonSnapshot("ForgeStatusResponse", dto, DebugBridgeJsonOptions);
    }

    [Fact]
    public void DebugBridgeStreamResponse_MatchesApprovedSnapshot()
    {
        var dto = new DebugBridgeStreamResponse
        {
            SessionId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
            Events =
            [
                new DebugBridgeStreamEventDto
                {
                    Type = "text_delta",
                    Text = "The castle loomed",
                },
                new DebugBridgeStreamEventDto
                {
                    Type = "tool",
                    ToolName = "query_lore",
                    ToolId = "tool_call_042",
                },
                new DebugBridgeStreamEventDto
                {
                    Type = "diagnostic",
                    Category = "tool_dispatch",
                    Message = "query_lore completed in 85ms",
                    Level = "info",
                },
                new DebugBridgeStreamEventDto
                {
                    Type = "done",
                    StopReason = "end_turn",
                    Usage = new DebugBridgeUsageDto { InputTokens = 3200, OutputTokens = 420 },
                },
            ],
            FinalContent = "The castle loomed over the forgotten valley.",
            FinalReasoning = "Lead with the silhouette, then the valley.",
            FinalReasoningArtifacts =
            [
                new ReasoningArtifactDto
                {
                    AgentId = "prose-writer",
                    AgentLabel = "Prose Writer",
                    Content = "Lead with the silhouette, then the valley.",
                    Sequence = 1,
                },
            ],
            NodeIds = new DebugBridgeNodeIds
            {
                User = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                Assistant = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            },
            Mode = "guide",
            MessageCount = 4,
            ToolRounds = 1,
            StopReason = "end_turn",
            Usage = new DebugBridgeUsageDto { InputTokens = 3200, OutputTokens = 420 },
            WriterState = "idle",
        };
        AssertJsonSnapshot("DebugBridgeStreamResponse", dto, DebugBridgeJsonOptions);
    }

    [Fact]
    public void WerewolfNarrationComposer_MatchesApprovedSnapshot()
    {
        var gameId = new GameInstanceId("game-werewolf");
        var participant = new ParticipantId("player-1");
        var events = new IGameEvent[]
        {
            WerewolfRoleRevealedEvent.Create(gameId, participant, WerewolfRole.Werewolf),
            WerewolfTeamRevealedEvent.Create(gameId, [new ParticipantId("player-1"), new ParticipantId("player-3")]),
            WerewolfStageStartedEvent.Create(gameId, WerewolfConstants.NightStage.StageId, 1),
            WerewolfNightActionsResolvedEvent.Create(gameId, 1),
            WerewolfStageStartedEvent.Create(gameId, WerewolfConstants.DayDiscussionStage.StageId, 1),
            WerewolfVoteRecordedEvent.Create(gameId, participant, new ParticipantId("player-2")),
            WerewolfVoteResolvedEvent.Create(gameId, new ParticipantId("player-2"), isTie: false),
            WerewolfPlayerEliminatedEvent.Create(gameId, new ParticipantId("player-2"), WerewolfRole.Villager),
            WerewolfWinConditionResolvedEvent.Create(gameId, WerewolfWinner.Werewolves, "werewolves_reached_parity"),
            GameEndedEvent.Create(gameId, "werewolves_win"),
        };
        var composer = new WerewolfGameEventNarrationComposer();
        var dto = events.Select((gameEvent, index) => new
        {
            eventType = gameEvent.GetType().Name,
            visibility = gameEvent.Visibility.Kind.ToString(),
            text = composer.ComposeSummary(gameEvent.WithJournalMetadata(GameEventId.NewId(), index + 1, DateTimeOffset.Parse("2026-04-28T12:00:00+00:00")))
        }).ToArray();

        AssertJsonSnapshot("WerewolfNarrationComposer", dto, DebugBridgeJsonOptions);
    }

    // =======================================================================
    // Session State Shape
    // =======================================================================

    [Fact]
    public void SessionLoadResponse_MatchesApprovedSnapshot()
    {
        var dto = new SessionLoadResponse
        {
            SessionId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
            Name = "Archive Session",
            Messages =
            [
                new SessionMessageDto
                {
                    Id = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
                    Role = "assistant",
                    Content = "The archive keeps the old maps.",
                    CreatedAt = DateTimeOffset.Parse("2026-03-15T14:32:00+00:00"),
                    ParentId = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                    Reasoning = "Mention the maps first.",
                    ReasoningArtifacts =
                    [
                        new ReasoningArtifactDto
                        {
                            AgentId = "orchestrator",
                            AgentLabel = "Orchestrator",
                            Content = "Mention the maps first.",
                            Sequence = 0,
                        },
                    ],
                    Variants =
                    [
                        new MessageVariantDto
                        {
                            Content = "The archive keeps the old maps.",
                            CreatedAt = DateTimeOffset.Parse("2026-03-15T14:32:00+00:00"),
                            Reasoning = "Mention the maps first.",
                            ReasoningArtifacts =
                            [
                                new ReasoningArtifactDto
                                {
                                    AgentId = "orchestrator",
                                    AgentLabel = "Orchestrator",
                                    Content = "Mention the maps first.",
                                    Sequence = 0,
                                },
                            ],
                        },
                    ],
                },
            ],
        };
        AssertJsonSnapshot("SessionLoadResponse", dto, DebugBridgeJsonOptions);
    }

    [Fact]
    public void SessionState_MatchesApprovedSnapshot()
    {
        var state = new SessionState
        {
            SessionId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
            Mode = new ModeSelectionState
            {
                ActiveMode = Mode.Writer,
                ProjectName = "epic-fantasy",
                CurrentFile = "chapter-07.md",
                Character = "narrator",
            },
            Profile = new ProfileState
            {
                ProfileId = "dark-fantasy",
                ActiveLoreSet = "shadow-realm",
                ActiveNarrativeRules = "dark-rules",
                ActiveWritingStyle = "gothic-prose",
            },
            Roleplay = new RoleplayRuntimeState
            {
                HasExplicitAiCharacterSelection = true,
                ActiveAiCharacter = "ancient-dragon",
                HasExplicitUserCharacterSelection = true,
                ActiveUserCharacter = "wandering-knight",
            },
            Writer = new WriterRuntimeState
            {
                PendingContent = "The knight drew his sword as shadows gathered.",
                PendingProjectName = "epic-fantasy",
                PendingFileName = "chapter-07.md",
                State = WriterState.PendingReview,
            },
            Narrative = new NarrativeRuntimeState
            {
                DirectorNotes = "Building tension before the betrayal scene.",
                ActivePlotFile = "act-2-rising-action.yaml",
                PlotProgress = new PlotProgressState
                {
                    CurrentBeat = "the-betrayal",
                    CompletedBeats = ["the-call", "crossing-the-threshold", "allies-gathered"],
                    Deviations = ["skipped-mentor-death"],
                },
            },
            Canonization = new LoreCanonizationRuntimeState
            {
                PendingProposal = new LoreCanonizationProposalState
                {
                    SessionId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
                    LoreSet = "shadow-realm",
                    TargetFilePath = "history/ash-storm.md",
                    Summary = "One lore import is waiting for explicit apply.",
                    NewFacts = ["Warden Ilya guards the bell fragments in the archive vault."],
                    ModifiedFacts = ["The silver bells cracked during the ash storm."],
                    Conflicts = ["A later claim about recasting the bells remains unverified."],
                    ProposedMarkdown = """
                        ### Ash Storm Aftermath

                        - The silver bells cracked during the ash storm.
                        """,
                    ProposedFileContent = """
                        <!-- quillforge:canonize session=01234567-89ab-cdef-0123-456789abcdef generated=2026-04-17T12:00:00.0000000+00:00 -->
                        ### Ash Storm Aftermath

                        - The silver bells cracked during the ash storm.
                        <!-- /quillforge:canonize -->
                        """,
                    CanApply = true,
                    GeneratedAt = DateTimeOffset.Parse("2026-04-17T12:00:00+00:00"),
                },
            },
            Game = new GameRuntimeState
            {
                Status = GameRuntimeStatus.WaitingForInput,
                GameInstanceId = "game-001",
                TemplateId = "village-night",
                ModuleId = "werewolf",
                ModuleVersion = "0.1.0",
                Seed = 1234,
                StartedAt = DateTimeOffset.Parse("2026-04-27T11:00:00+00:00"),
                LastUpdatedAt = DateTimeOffset.Parse("2026-04-27T11:05:00+00:00"),
                EngineSnapshot = CreateGameRuntimeSnapshot(),
                ParticipantBindings =
                [
                    new GameRuntimeParticipantBinding
                    {
                        ParticipantId = "human-1",
                        DisplayName = "Human",
                        Kind = GameRuntimeParticipantKind.Human,
                        UserSeatId = "user",
                    },
                    new GameRuntimeParticipantBinding
                    {
                        ParticipantId = "agent-1",
                        DisplayName = "Mira",
                        Kind = GameRuntimeParticipantKind.Agent,
                        ProviderAlias = "local",
                        ModelOverride = "test-model",
                    },
                ],
                EventDeliveryCursors =
                [
                    new GameRuntimeEventDeliveryCursor
                    {
                        ParticipantId = "agent-1",
                        DeliveredThroughEngineEventSequence = 3,
                        DeliveredThroughCommunicationSequence = 2,
                        MemoryRevision = 1,
                        LastPromptEnvelopeId = "prompt-001",
                    },
                ],
                AgentMemories =
                [
                    new GameRuntimeAgentMemoryState
                    {
                        ParticipantId = "agent-1",
                        Revision = 1,
                        TokenBudget = 512,
                        Summary = "Mira remembers the public accusation.",
                        ContentHash = "sha256:test",
                        LastSummarizedRoundNumber = 1,
                        LastSummarizedPublicEngineEventSequence = 3,
                        LastSummarizedPrivateEventIds = ["private-event-1"],
                        LastSummarizedCommunicationSequence = 2,
                        UpdatedAt = DateTimeOffset.Parse("2026-04-27T11:04:00+00:00"),
                    },
                ],
                MemorySummaryDecisions =
                [
                    new MemorySummaryDecision(
                        "memory-decision-001",
                        "agent-1",
                        1,
                        DateTimeOffset.Parse("2026-04-27T11:04:00+00:00"),
                        new AgentVisibleEventsCursor(1, [], 1, 0),
                        new AgentVisibleEventsCursor(3, ["private-event-1"], 2, 1),
                        40,
                        12,
                        false,
                        false,
                        false,
                        "local",
                        "test-model",
                        "round-ended-event-1",
                        null,
                        "sha256:memory"),
                ],
                PromptCursors =
                [
                    new GameRuntimeAgentPromptDeliveryCursor
                    {
                        ParticipantId = "agent-1",
                        LastDeliveredPublicEngineEventSequence = 3,
                        DeliveredPrivateEventIds = ["private-event-1"],
                        CommunicationDeliveredThroughSequence = 2,
                        MemoryRevision = 1,
                        LastPromptEnvelopeId = "prompt-001",
                    },
                ],
                PromptEnvelopes =
                [
                    new GameRuntimeAgentPromptEnvelope
                    {
                        EnvelopeId = "prompt-001",
                        ParticipantId = "agent-1",
                        CreatedAt = DateTimeOffset.Parse("2026-04-27T11:04:00+00:00"),
                        EngineCursorSequence = 3,
                        CommunicationCursorSequence = 2,
                        MemoryRevision = 1,
                        ProviderAlias = "local",
                        Model = "test-model",
                        PromptTokens = 40,
                        ResponseTokens = 12,
                        PromptContentHash = "sha256:prompt",
                        ResponseContentHash = "sha256:response",
                        PromptText = "Prompt text retained for inspector.",
                        ResponseText = "Response text retained for inspector.",
                    },
                ],
                HostRecords =
                [
                    new GameRuntimeHostRecord
                    {
                        RecordId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
                        Sequence = 1,
                        Kind = GameRuntimeHostRecordKind.Started,
                        OccurredAt = DateTimeOffset.Parse("2026-04-27T11:00:00+00:00"),
                        ReasonCode = "game_started",
                        Summary = "Game runtime started.",
                    },
                ],
                NextHostRecordSequence = 2,
            },
            LastModified = DateTimeOffset.Parse("2026-03-15T14:30:00+00:00"),
        };
        AssertJsonSnapshot("SessionState", state, SessionStateJsonOptions);
    }

    private static RulesGameStateSnapshot CreateGameRuntimeSnapshot()
    {
        var gameInstanceId = new GameInstanceId("game-001");
        var moduleId = new GameModuleId("werewolf");
        var moduleVersion = new GameModuleVersion("0.1.0");
        var participant = ParticipantState.Agent(new ParticipantId("agent-1"), "Mira");
        var state = RulesGameState.CreateNotStarted(
            gameInstanceId,
            new GameModuleDescriptor(
                moduleId,
                moduleVersion,
                new GameTemplateVersion("1.0.0"),
                new GameTemplateVersion("1.0.0"),
                "Werewolf",
                new PlayerCountRange(4, 16),
                []),
            1234,
            [participant]);
        state = state with
        {
            Status = RulesGameStatus.WaitingForInput,
            EventJournal = state.EventJournal.Append(GameStartedEvent.Create(gameInstanceId, moduleId, moduleVersion, 1234)),
        };

        return RulesGameStateSnapshot.FromState(state);
    }

    [Fact]
    public void LoreCanonizationPreviewResponse_MatchesApprovedSnapshot()
    {
        var dto = new LoreCanonizationPreviewResponse
        {
            SessionId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
            Status = "preview_ready",
            Proposal = new LoreCanonizationProposalDto
            {
                SessionId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
                LoreSet = "shadow-realm",
                TargetFilePath = "session-imports/2026-04-17-01234567.md",
                Summary = "Found one new fact and one lore refinement.",
                NewFacts = ["Warden Ilya keeps the bell fragments in the archive vault."],
                ModifiedFacts = ["The silver bells cracked during the ash storm."],
                Conflicts = ["One speaker claimed the bells were later recast, but the session never confirmed it."],
                ProposedMarkdown = """
                    ### Ash Storm Aftermath

                    - The silver bells cracked during the ash storm.
                    """,
                ProposedFileContent = """
                    <!-- quillforge:canonize session=01234567-89ab-cdef-0123-456789abcdef generated=2026-04-17T12:00:00.0000000+00:00 -->
                    ### Ash Storm Aftermath

                    - The silver bells cracked during the ash storm.
                    <!-- /quillforge:canonize -->
                    """,
                CanApply = true,
                GeneratedAt = DateTimeOffset.Parse("2026-04-17T12:00:00+00:00"),
            },
        };
        AssertJsonSnapshot("LoreCanonizationPreviewResponse", dto, DebugBridgeJsonOptions);
    }

    // =======================================================================
    // Game API Shape
    // =======================================================================

    [Fact]
    public void GameViewResponse_MatchesApprovedSnapshot()
    {
        var dto = new GameViewResponse
        {
            View = new GameBridgeView(
                GameRuntimeStatus.WaitingForInput,
                "game-001",
                "village",
                "werewolf",
                "0.1.0",
                2,
                "day-vote",
                "Day vote",
                [
                    new GameBridgeParticipantView("human-1", "Human", GameRuntimeParticipantKind.Human, true, true),
                    new GameBridgeParticipantView("agent-1", "Mira", GameRuntimeParticipantKind.Agent, true, false),
                ],
                new GameBridgePublicView(
                    [
                        new GameBridgeNarrationEntry(
                            "11111111-1111-1111-1111-111111111111",
                            4,
                            "RoundStartedEvent",
                            "RoundStartedEvent occurred.",
                            DateTimeOffset.Parse("2026-04-28T10:00:00+00:00")),
                    ],
                    [
                        new ParticipantFeedEntry(
                            3,
                            ParticipantFeedEntryKind.PublicChannelMessage,
                            Guid.Parse("22222222-2222-2222-2222-222222222222"),
                            null,
                            new ParticipantMessageAuthor(new GameParticipantId("human-1"), ParticipantMessageAuthorKind.Human),
                            [],
                            "I nominate Mira.",
                            null,
                            null,
                            null,
                            DateTimeOffset.Parse("2026-04-28T10:01:00+00:00")),
                    ]),
                new GameBridgePlayerView(
                    "human-1",
                    "Human",
                    [
                        new VisibleGameEvent(
                            new GameEventId(Guid.Parse("33333333-3333-3333-3333-333333333333")),
                            2,
                            "RoleAssignedEvent",
                            DateTimeOffset.Parse("2026-04-28T09:59:00+00:00")),
                    ],
                    [
                        new PendingInputState(
                            new PendingInputId("pending-1"),
                            new ParticipantId("human-1"),
                            new GameStageId("day-vote"),
                            "SubmitVote",
                            PendingInputStatus.Waiting,
                            [new LegalIntentOption("vote-agent-1", "Vote Mira", "Cast a vote for Mira.")]),
                    ],
                    [],
                    new GameRuntimeEventDeliveryCursor
                    {
                        ParticipantId = "human-1",
                        DeliveredThroughEngineEventSequence = 4,
                        DeliveredThroughCommunicationSequence = 3,
                        MemoryRevision = 0,
                    })
                    {
                        ActionForms =
                        [
                            new GameBridgeActionFormView(
                                "SubmitVote",
                                "day-vote",
                                "Village vote",
                                "Choose an active participant to eliminate or abstain.",
                                "ButtonList",
                                [
                                    new GameBridgeActionFieldView("choiceName", "ChoiceName", true, "Vote target", "Choose one legal vote target."),
                                ]),
                        ],
                    })
            {
                ModuleAuthoring = new GameBridgeModuleAuthoringView(
                    [],
                    [
                        new GameBridgeStageHookView("day-vote", "Day vote", "Active participants vote to eliminate someone or abstain.", 3, true, false),
                    ],
                    [
                        new GameBridgeActionFormView(
                            "SubmitVote",
                            "day-vote",
                            "Village vote",
                            "Choose an active participant to eliminate or abstain.",
                            "ButtonList",
                            [
                                new GameBridgeActionFieldView("choiceName", "ChoiceName", true, "Vote target", "Choose one legal vote target."),
                            ]),
                    ],
                    [],
                    new GameBridgeCommunicationCapabilitiesView(true, false),
                    new GameBridgeMemoryExpectationsView(true, 512, 3),
                    new GameBridgeProjectionCapabilitiesView(true, true, true)),
            }
        };

        AssertJsonSnapshot("GameViewResponse", dto, DebugBridgeJsonOptions);
    }

    [Fact]
    public void GameInspectorResponse_MatchesApprovedSnapshot()
    {
        var dto = new GameInspectorResponse
        {
            Inspector = new GameInspectorProjection
            {
                SessionId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef"),
                HasGame = true,
                GameInstanceId = "game-001",
                TemplateId = "village",
                ModuleId = "werewolf",
                ModuleVersion = "0.1.0",
                Seed = 42,
                RuntimeStatus = GameRuntimeStatus.WaitingForInput.ToString(),
                Engine = new GameInspectorEngineProjection
                {
                    Status = RulesGameStatus.WaitingForInput.ToString(),
                    RoundNumber = 2,
                    StageId = "day-vote",
                    StageName = "Day vote",
                    StageAllowsPublicMessages = true,
                    StageAllowsDirectMessages = false,
                    EventJournalNextSequence = 5,
                    EventJournal =
                    [
                        new GameInspectorEventProjection
                        {
                            EventId = "11111111-1111-1111-1111-111111111111",
                            Sequence = 1,
                            EventType = nameof(GameStartedEvent),
                            OccurredAt = DateTimeOffset.Parse("2026-04-28T10:00:00+00:00"),
                            Visibility = GameEventVisibilityKind.Public.ToString(),
                        },
                        new GameInspectorEventProjection
                        {
                            EventId = "22222222-2222-2222-2222-222222222222",
                            Sequence = 2,
                            EventType = nameof(PlayerChoiceSubmittedEvent),
                            OccurredAt = DateTimeOffset.Parse("2026-04-28T10:01:00+00:00"),
                            Visibility = GameEventVisibilityKind.PrivateToParticipant.ToString(),
                            ParticipantId = "agent-1",
                            PendingInputId = "pending-agent-1",
                        },
                    ],
                    PendingInputs =
                    [
                        new GameInspectorPendingInputProjection
                        {
                            PendingInputId = "pending-human-1",
                            ParticipantId = "human-1",
                            StageId = "day-vote",
                            IntentName = "vote",
                            Status = PendingInputStatus.Waiting.ToString(),
                            LegalChoiceNames = ["agent-1", "abstain"],
                        },
                    ],
                },
                Participants =
                [
                    new GameInspectorParticipantProjection
                    {
                        ParticipantId = "human-1",
                        DisplayName = "Human",
                        Kind = GameRuntimeParticipantKind.Human.ToString(),
                        IsActive = true,
                    },
                    new GameInspectorParticipantProjection
                    {
                        ParticipantId = "agent-1",
                        DisplayName = "Mira",
                        Kind = GameRuntimeParticipantKind.Agent.ToString(),
                        IsActive = true,
                        ProviderAlias = "local",
                        Model = "llama3.2",
                    },
                ],
                PromptCursors =
                [
                    new GameInspectorPromptCursorProjection
                    {
                        ParticipantId = "agent-1",
                        LastDeliveredPublicEngineEventSequence = 4,
                        DeliveredPrivateEventIds = ["22222222-2222-2222-2222-222222222222"],
                        CommunicationDeliveredThroughSequence = 3,
                        MemoryRevision = 1,
                        LastPromptEnvelopeId = "env-1",
                    },
                ],
                EventDeliveryCursors =
                [
                    new GameInspectorEventDeliveryCursorProjection
                    {
                        ParticipantId = "agent-1",
                        DeliveredThroughEngineEventSequence = 4,
                        DeliveredThroughCommunicationSequence = 3,
                        MemoryRevision = 1,
                        LastPromptEnvelopeId = "env-1",
                    },
                ],
                AgentMemories =
                [
                    new GameInspectorMemoryProjection
                    {
                        ParticipantId = "agent-1",
                        Revision = 1,
                        TokenBudget = 512,
                        Summary = "Mira remembers the public vote pressure.",
                        ContentHash = "memory-hash",
                        LastSummarizedRoundNumber = 1,
                        LastSummarizedPublicEngineEventSequence = 4,
                        LastSummarizedPrivateEventIds = ["22222222-2222-2222-2222-222222222222"],
                        LastSummarizedCommunicationSequence = 3,
                        UpdatedAt = DateTimeOffset.Parse("2026-04-28T10:05:00+00:00"),
                    },
                ],
                PromptEnvelopes =
                [
                    new GameInspectorPromptEnvelopeProjection
                    {
                        EnvelopeId = "env-1",
                        ParticipantId = "agent-1",
                        CreatedAt = DateTimeOffset.Parse("2026-04-28T10:04:00+00:00"),
                        EngineCursorSequence = 4,
                        CommunicationCursorSequence = 3,
                        MemoryRevision = 1,
                        ProviderAlias = "local",
                        Model = "llama3.2",
                        PromptTokens = 120,
                        ResponseTokens = 18,
                        PromptContentHash = "prompt-hash",
                        ResponseContentHash = "response-hash",
                        PromptPreview = "Visible engine facts: public vote pressure.",
                        ResponsePreview = "{\"accepted\":true}",
                    },
                ],
                TokenUsage = new SessionUsageSummary
                {
                    TotalInputTokens = 120,
                    TotalOutputTokens = 18,
                    TotalRequests = 1,
                    ByAgent =
                    [
                        new AgentUsageEntry
                        {
                            AgentName = "game-agent:agent-1",
                            InputTokens = 120,
                            OutputTokens = 18,
                            RequestCount = 1,
                        },
                    ],
                },
            },
        };

        AssertJsonSnapshot("GameInspectorResponse", dto, DebugBridgeJsonOptions);
    }

    // =======================================================================
    // Game Template API Shape
    // =======================================================================

    [Fact]
    public void GameTemplateResponse_MatchesApprovedSnapshot()
    {
        var dto = new GameTemplateResponse
        {
            Template = new GameTemplate
            {
                TemplateId = "village",
                DisplayName = "Village Werewolf",
                Description = "Baseline behavior-focused Werewolf setup.",
                Module = new GameTemplateModuleSelection
                {
                    ModuleId = "werewolf",
                    MinimumVersion = "1.0.0",
                    MaximumVersion = "1.0.0",
                },
                TemplateVersion = "1.0.0",
                RulesOptions = new GameTemplateRulesOptions
                {
                    Values =
                    [
                        new GameTemplateRuleOptionValue { Name = "werewolf_count", Kind = GameTemplateRuleOptionValueKind.Int, IntValue = 1 },
                        new GameTemplateRuleOptionValue { Name = "seer_enabled", Kind = GameTemplateRuleOptionValueKind.Bool, BoolValue = false },
                    ],
                },
                Roster = new GameTemplateRosterSettings
                {
                    RosterSize = 4,
                    UserSeatParticipantId = "user",
                    AgentPlayers =
                    [
                        new GameTemplateAgentPlayerConfig
                        {
                            ParticipantId = "agent-1",
                            ProviderAlias = "local",
                            ModelOverride = "llama3.2",
                            CharacterPrompt = "Keep claims concise.",
                            Personality = "skeptical villager",
                            FixedName = "Bob",
                            RandomNameBehavior = GameTemplateRandomNameBehavior.UseFixedNameWhenProvided,
                        },
                        new GameTemplateAgentPlayerConfig
                        {
                            ParticipantId = "agent-2",
                            ProviderAlias = "local",
                            FixedName = "Carol",
                        },
                        new GameTemplateAgentPlayerConfig
                        {
                            ParticipantId = "agent-3",
                            ProviderAlias = "local",
                            FixedName = "Drew",
                        },
                    ],
                },
                Memory = new GameTemplateMemorySettings { TokenBudget = 512 },
                Communication = new GameTemplateCommunicationSettings
                {
                    PublicChannelEnabled = true,
                    DirectMessagesEnabled = true,
                    HostMessagesEnabled = true,
                },
                Naming = new GameTemplateNamingSettings
                {
                    RandomizeAgentNames = true,
                    RandomNameSet = "village",
                    RandomSeed = 17,
                },
            },
            Validation = new GameTemplateValidationResult
            {
                Issues =
                [
                    new GameTemplateValidationIssue
                    {
                        Code = "unknown_provider_alias",
                        Field = "roster.agentPlayers[0].providerAlias",
                        Message = "Provider alias 'local' is not configured.",
                        Source = GameTemplateValidationSources.Provider,
                    },
                ],
            },
        };
        AssertJsonSnapshot("GameTemplateResponse", dto, DebugBridgeJsonOptions);
    }

    [Fact]
    public void GameTemplateCatalogResponse_MatchesApprovedSnapshot()
    {
        var dto = new GameTemplateCatalogResponse
        {
            Modules =
            [
                new GameTemplateModuleOption
                {
                    ModuleId = "werewolf",
                    ModuleVersion = "0.1.0",
                    DisplayName = "Werewolf",
                    MinimumTemplateVersion = "1.0.0",
                    MaximumTemplateVersion = "1.0.0",
                    MinimumPlayers = 4,
                    MaximumPlayers = 12,
                    SetupFields =
                    [
                        new GameTemplateSetupFieldOption
                        {
                            Name = "werewolf_count",
                            ValueKind = GameSetupValueKind.Int.ToString(),
                            IsRequired = true,
                            DisplayName = "Werewolf count",
                            Description = "Number of werewolves in the village.",
                        },
                    ],
                    Stages =
                    [
                        new GameTemplateStageHookOption
                        {
                            StageId = "night",
                            DisplayName = "Night",
                            Description = "Private role information is visible; night-only actions resolve before discussion.",
                            Sequence = 1,
                            AllowsPublicMessages = false,
                            AllowsDirectMessages = true,
                        },
                    ],
                    ActionForms =
                    [
                        new GameTemplateActionFormOption
                        {
                            IntentName = "vote",
                            StageId = "voting",
                            DisplayName = "Village vote",
                            Description = "Choose an active participant to eliminate or abstain.",
                            Layout = GameActionFormLayout.ButtonList.ToString(),
                            Fields =
                            [
                                new GameTemplateActionFieldOption
                                {
                                    Name = "choiceName",
                                    ValueKind = GameActionFieldKind.ChoiceName.ToString(),
                                    IsRequired = true,
                                    DisplayName = "Vote target",
                                    Description = "Choose one legal vote target.",
                                },
                            ],
                        },
                    ],
                    PromptAssets =
                    [
                        new GameTemplatePromptAssetOption
                        {
                            AssetId = "werewolf-rules",
                            Kind = GamePromptAssetKind.RulesText.ToString(),
                            IsRequired = true,
                        },
                    ],
                    CommunicationCapabilities = new GameTemplateCommunicationCapabilitiesOption
                    {
                        AllowsPublicChannelMessages = true,
                        AllowsDirectMessages = true,
                    },
                    MemoryExpectations = new GameTemplateMemoryExpectationsOption
                    {
                        UsesRoundSummaries = true,
                        SuggestedSummaryTokenBudget = 512,
                        MaximumRetainedRoundSummaries = 3,
                    },
                    ParticipantRequirements = new GameTemplateParticipantRequirementsOption
                    {
                        AllowsHumanParticipants = true,
                        AllowsAgentParticipants = true,
                        AllowsSystemParticipants = false,
                        MinimumHumanParticipants = 1,
                        MinimumAgentParticipants = 3,
                    },
                    ProjectionCapabilities = new GameTemplateProjectionCapabilitiesOption
                    {
                        SupportsPublicEventProjection = true,
                        SupportsParticipantPrivateProjection = true,
                        SupportsHostInspectorProjection = true,
                    },
                },
            ],
            Providers =
            [
                new GameTemplateProviderOption
                {
                    Alias = "local",
                    Type = "Ollama",
                    Model = "llama3.2",
                    DefaultModel = "llama3.2",
                    ContextLimit = 8192,
                },
            ],
        };

        AssertJsonSnapshot("GameTemplateCatalogResponse", dto, DebugBridgeJsonOptions);
    }

    // =======================================================================
    // Profile Config YAML Shape
    // =======================================================================

    [Fact]
    public void ProfileConfig_MatchesApprovedSnapshot()
    {
        var config = new ProfileConfig
        {
            LoreSet = "shadow-realm",
            NarrativeRules = "dark-rules",
            WritingStyle = "gothic-prose",
            Roleplay = new RoleplayConfig
            {
                AiCharacter = "ancient-dragon",
                UserCharacter = "wandering-knight",
            },
        };
        AssertYamlSnapshot("ProfileConfig", config);
    }

    // =======================================================================
    // Infrastructure
    // =======================================================================

    private static void AssertJsonSnapshot<T>(string name, T value, JsonSerializerOptions options)
    {
        var actual = NormalizeSnapshot(JsonSerializer.Serialize(value, options));
        var approvedPath = Path.Combine(SnapshotsDir, $"{name}.approved.json");

        if (!File.Exists(approvedPath))
        {
            Directory.CreateDirectory(SnapshotsDir);
            File.WriteAllText(approvedPath, actual);
            // First run: file created; test passes so the CI pipeline can bootstrap.
            return;
        }

        var approved = NormalizeSnapshot(File.ReadAllText(approvedPath));
        if (actual != approved)
        {
            var message =
                $"Snapshot mismatch for {name}.\n" +
                $"Approved file: {approvedPath}\n\n" +
                $"--- APPROVED ---\n{approved}\n\n" +
                $"--- ACTUAL ---\n{actual}\n\n" +
                "If the change is intentional, delete the .approved.json file and re-run to regenerate.";
            Assert.Fail(message);
        }
    }

    private static void AssertYamlSnapshot<T>(string name, T value)
    {
        var actual = NormalizeSnapshot(YamlSerializer.Serialize(value));
        var approvedPath = Path.Combine(SnapshotsDir, $"{name}.approved.yaml");

        if (!File.Exists(approvedPath))
        {
            Directory.CreateDirectory(SnapshotsDir);
            File.WriteAllText(approvedPath, actual);
            return;
        }

        var approved = NormalizeSnapshot(File.ReadAllText(approvedPath));
        if (actual != approved)
        {
            var message =
                $"Snapshot mismatch for {name}.\n" +
                $"Approved file: {approvedPath}\n\n" +
                $"--- APPROVED ---\n{approved}\n\n" +
                $"--- ACTUAL ---\n{actual}\n\n" +
                "If the change is intentional, delete the .approved.yaml file and re-run to regenerate.";
            Assert.Fail(message);
        }
    }

    private static string NormalizeSnapshot(string content)
    {
        return content.Replace("\r\n", "\n").TrimEnd('\n');
    }
}
