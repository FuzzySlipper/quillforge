using System.Text.Json;
using System.Text.Json.Serialization;
using QuillForge.Core.Models;
using QuillForge.Web.Contracts;
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
            LastModified = DateTimeOffset.Parse("2026-03-15T14:30:00+00:00"),
        };
        AssertJsonSnapshot("SessionState", state, SessionStateJsonOptions);
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
