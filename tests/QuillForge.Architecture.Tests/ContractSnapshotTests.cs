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
        .Build();

    private static readonly string SnapshotsDir = Path.GetFullPath(
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Snapshots"));

    // =======================================================================
    // SSE Event Shapes
    // =======================================================================

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
            Portrait = "/portraits/ai-guide.png",
            UserPortrait = "/portraits/user-avatar.png",
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
        };
        AssertJsonSnapshot("DebugBridgeChatResponse", dto, DebugBridgeJsonOptions);
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
            NodeIds = new DebugBridgeNodeIds
            {
                User = Guid.Parse("11111111-2222-3333-4444-555555555555"),
                Assistant = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"),
            },
            Mode = "general",
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
                ActiveConductor = "grim-narrator",
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
            LastModified = DateTimeOffset.Parse("2026-03-15T14:30:00+00:00"),
        };
        AssertJsonSnapshot("SessionState", state, SessionStateJsonOptions);
    }

    // =======================================================================
    // Profile Config YAML Shape
    // =======================================================================

    [Fact]
    public void ProfileConfig_MatchesApprovedSnapshot()
    {
        var config = new ProfileConfig
        {
            Conductor = "grim-narrator",
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
        var actual = JsonSerializer.Serialize(value, options);
        var approvedPath = Path.Combine(SnapshotsDir, $"{name}.approved.json");

        if (!File.Exists(approvedPath))
        {
            Directory.CreateDirectory(SnapshotsDir);
            File.WriteAllText(approvedPath, actual);
            // First run: file created; test passes so the CI pipeline can bootstrap.
            return;
        }

        var approved = File.ReadAllText(approvedPath);
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
        var actual = YamlSerializer.Serialize(value);
        var approvedPath = Path.Combine(SnapshotsDir, $"{name}.approved.yaml");

        if (!File.Exists(approvedPath))
        {
            Directory.CreateDirectory(SnapshotsDir);
            File.WriteAllText(approvedPath, actual);
            return;
        }

        var approved = File.ReadAllText(approvedPath);
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
}
