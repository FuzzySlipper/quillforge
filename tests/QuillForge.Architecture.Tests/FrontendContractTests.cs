using System.Text.Json;
using QuillForge.Web.Contracts;

namespace QuillForge.Architecture.Tests;

public sealed class FrontendContractTests
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void ReasoningArtifactDto_StaysInSyncWith_ReasoningArtifactInterface()
    {
        var shape = GetTypeScriptInterfaceShape("ReasoningArtifact");
        var jsonKeys = SerializeTopLevelKeys(new ReasoningArtifactDto
        {
            AgentId = "prose-writer",
            AgentLabel = "Prose Writer",
            Content = "Keep the image concrete.",
            Sequence = 1,
        });

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("string", shape["agentId"]);
        Assert.Equal("string", shape["agentLabel"]);
        Assert.Equal("string", shape["content"]);
        Assert.Equal("number", shape["sequence"]);
    }

    [Fact]
    public void StatusResponse_StaysInSyncWith_StatusInterface()
    {
        var shape = GetTypeScriptInterfaceShape("Status");
        var jsonKeys = SerializeTopLevelKeys(new StatusResponse
        {
            Status = "ready",
            Version = "1.2.3",
            Build = "1.2.3+abc",
            Mode = "guide",
            Profile = "default",
            Project = "novel",
            File = "chapter-01.md",
            LoreSet = "world",
            WritingStyle = "literary",
            Model = "gpt-test",
            Layout = "standard",
            AiCharacter = "guide",
            UserCharacter = "author",
            ConversationTurns = 12,
            LoreFiles = 3,
            ContextLimit = 16000,
            LoreTokens = 1200,
            HistoryTokens = 800,
            DiagnosticsLivePanel = true,
            Update = new UpdateInfoDto
            {
                Available = true,
                Version = "1.2.4",
                Url = "https://example.test/download",
            },
        });

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("string", shape["version"]);
        Assert.Equal("string", shape["build"]);
        Assert.Equal("Mode", shape["mode"]);
        Assert.Equal("string", shape["profile"]);
        Assert.Equal("number", shape["loreFiles"]);
        Assert.Equal("boolean", shape["diagnosticsLivePanel"]);
    }

    [Fact]
    public void ModeResponse_StaysInSyncWith_ModeInfoInterface()
    {
        var shape = GetTypeScriptInterfaceShape("ModeInfo");
        var jsonKeys = SerializeTopLevelKeys(new ModeResponse
        {
            SessionId = Guid.CreateVersion7(),
            Mode = "writer",
            Project = "novel",
            File = "scene.md",
            Character = "guide",
            PendingContent = "Pending review.",
            PendingProject = "novel",
            PendingFile = "scene.md",
            Notice = "Created roleplay workspace at story/roleplay-abc/scene-01.md",
        });

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("Mode", shape["mode"]);
        Assert.Equal("string | null", shape["pendingContent"]);
        Assert.Equal("string | null", shape["pendingProject"]);
        Assert.Equal("string | null", shape["pendingFile"]);
        Assert.Equal("string | null", shape["notice"]);
    }

    [Fact]
    public void ProfilesResponse_StaysInSyncWith_ProfilesInterface()
    {
        var shape = GetTypeScriptInterfaceShape("Profiles");
        var jsonKeys = SerializeTopLevelKeys(new ProfilesResponse
        {
            ProfileIds = ["default"],
            DefaultProfileId = "default",
            ActiveProfileId = "grim",
            LoreSets = ["world"],
            NarrativeRules = ["rules"],
            WritingStyles = ["style"],
            LibrarianPrompts = ["default"],
            ActiveLore = "world",
            ActiveNarrativeRules = "rules",
            ActiveWritingStyle = "style",
            ActiveLibrarianPrompt = "default",
        });

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("string[]", shape["profileIds"]);
        Assert.Equal("string[]", shape["loreSets"]);
    }

    [Fact]
    public void ProfileSwitchResponse_StaysInSyncWith_ProfileSwitchResultInterface()
    {
        var shape = GetTypeScriptInterfaceShape("ProfileSwitchResult");
        var jsonKeys = SerializeTopLevelKeys(new ProfileSwitchResponse
        {
            Status = "ok",
            SessionId = Guid.CreateVersion7(),
            ActiveProfileId = "grim",
            ActiveLore = "grim-lore",
            ActiveNarrativeRules = "grim-rules",
            ActiveWritingStyle = "grim-style",
            ActiveLibrarianPrompt = "default",
            LoreFiles = 42,
        });

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("number", shape["loreFiles"]);
        Assert.Equal("string", shape["status"]);
    }

    [Fact]
    public void FrontendModeUnion_ListsAllSupportedModes()
    {
        var modeValues = GetTypeScriptUnionValues("Mode");

        var expectedModes = new[] { "guide", "writer", "roleplay", "forge", "council", "research" }
            .OrderBy(value => value)
            .ToList();

        Assert.Equal(expectedModes, modeValues.OrderBy(value => value).ToList());
    }

    [Fact]
    public void StreamEventUnion_IncludesAllChatSseEventTypes()
    {
        var streamEventTypes = GetApiStreamEventUnionValues();
        var expectedChatEventTypes = new[]
        {
            "text_delta",
            "tool",
            "done",
            "reasoning_delta",
            "diagnostic",
            "persisted",
        };

        foreach (var expectedType in expectedChatEventTypes)
        {
            Assert.Contains(expectedType, streamEventTypes);
        }
    }


    [Fact]
    public void LoreCanonizationPreviewResponse_StaysInSyncWith_LoreCanonizationPreviewResultInterface()
    {
        var shape = GetTypeScriptInterfaceShape("LoreCanonizationPreviewResult");
        var jsonKeys = SerializeTopLevelKeys(new LoreCanonizationPreviewResponse
        {
            SessionId = Guid.CreateVersion7(),
            Status = "preview_ready",
            Proposal = new LoreCanonizationProposalDto
            {
                SessionId = Guid.CreateVersion7(),
                LoreSet = "shadow-realm",
                TargetFilePath = "imports/session.md",
                Summary = "Captured one safe update.",
                NewFacts = ["The bells cracked during the ash storm."],
                ModifiedFacts = [],
                Conflicts = [],
                ProposedMarkdown = """
                    ### Ash Storm

                    - The bells cracked during the ash storm.
                    """,
                ProposedFileContent = """
                    ### Ash Storm

                    - The bells cracked during the ash storm.
                    """,
                CanApply = true,
                GeneratedAt = DateTimeOffset.Parse("2026-04-17T12:00:00+00:00"),
            },
        });

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("LoreCanonizationProposal", shape["proposal"]);
        Assert.Equal("string", shape["status"]);
    }

    [Fact]
    public void LoreCanonizationProposalDto_StaysInSyncWith_LoreCanonizationProposalInterface()
    {
        var shape = GetTypeScriptInterfaceShape("LoreCanonizationProposal");
        var jsonKeys = SerializeTopLevelKeys(new LoreCanonizationProposalDto
        {
            SessionId = Guid.CreateVersion7(),
            LoreSet = "shadow-realm",
            TargetFilePath = "imports/session.md",
            Summary = "Captured one safe update.",
            NewFacts = ["The bells cracked during the ash storm."],
            ModifiedFacts = [],
            Conflicts = [],
            ProposedMarkdown = """
                ### Ash Storm

                - The bells cracked during the ash storm.
                """,
            ProposedFileContent = """
                ### Ash Storm

                - The bells cracked during the ash storm.
                """,
            CanApply = true,
            GeneratedAt = DateTimeOffset.Parse("2026-04-17T12:00:00+00:00"),
        });

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("string[]", shape["newFacts"]);
        Assert.Equal("boolean", shape["canApply"]);
    }

    [Fact]
    public void CommandsTs_RegistersCanonizeCommand()
    {
        var source = File.ReadAllText(GetFrontendFilePath("commands.ts"));

        Assert.Contains("canonize:", source, StringComparison.Ordinal);
        Assert.Contains("/canonize apply", source, StringComparison.Ordinal);
        Assert.Contains("previewLoreCanonization", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DebugBridgeChatResponse_UsesStableCamelCaseShape()
    {
        var jsonKeys = SerializeTopLevelKeys(new DebugBridgeChatResponse
        {
            SessionId = Guid.CreateVersion7(),
            ResponseText = "hello",
            StopReason = "end_turn",
            ToolRoundsUsed = 1,
            Usage = new DebugBridgeUsageDto
            {
                InputTokens = 10,
                OutputTokens = 20,
            },
            Mode = "guide",
            MessageCount = 2,
            Reasoning = "Check the lore thread before answering.",
            ReasoningArtifacts =
            [
                new ReasoningArtifactDto
                {
                    AgentId = "assistant",
                    AgentLabel = "Assistant",
                    Content = "Check the lore thread before answering.",
                    Sequence = 0,
                },
            ],
        });

        Assert.Equal(
            ["messageCount", "mode", "reasoning", "reasoningArtifacts", "responseText", "sessionId", "stopReason", "toolRoundsUsed", "usage"],
            jsonKeys.OrderBy(key => key));
    }

    private static Dictionary<string, string> GetTypeScriptInterfaceShape(string interfaceName)
    {
        var source = File.ReadAllText(GetFrontendFilePath("types.ts"));
        var marker = $"export interface {interfaceName}";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find interface {interfaceName} in types.ts");

        var bodyStart = source.IndexOf('{', start);
        var bodyEnd = FindMatchingBrace(source, bodyStart);
        Assert.True(bodyStart >= 0 && bodyEnd > bodyStart, $"Could not parse interface {interfaceName} body");

        var body = source[(bodyStart + 1)..bodyEnd];
        var shape = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var rawLine in body.Split('\n'))
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("/**", StringComparison.Ordinal) || line.StartsWith("*", StringComparison.Ordinal))
            {
                continue;
            }

            var colonIndex = line.IndexOf(':');
            if (colonIndex < 0)
            {
                continue;
            }

            var name = line[..colonIndex].Trim().TrimEnd('?');
            var type = line[(colonIndex + 1)..].Trim().TrimEnd(';');
            shape[name] = type;
        }

        return shape;
    }

    private static List<string> GetTypeScriptUnionValues(string typeName)
    {
        var source = File.ReadAllText(GetFrontendFilePath("types.ts"));
        var marker = $"export type {typeName} =";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find type alias {typeName} in types.ts");

        var end = source.IndexOf(';', start);
        Assert.True(end > start, $"Could not parse type alias {typeName}");

        var declaration = source[(start + marker.Length)..end];
        return declaration
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim().Trim('"'))
            .ToList();
    }

    private static List<string> GetApiStreamEventUnionValues()
    {
        var source = File.ReadAllText(GetFrontendFilePath("api.ts"));
        var marker = "type: ";
        var streamEventStart = source.IndexOf("export interface StreamEvent", StringComparison.Ordinal);
        Assert.True(streamEventStart >= 0, "Could not find StreamEvent interface in api.ts");

        var typeStart = source.IndexOf(marker, streamEventStart, StringComparison.Ordinal);
        var lineEnd = source.IndexOf('\n', typeStart);
        Assert.True(typeStart >= 0 && lineEnd > typeStart, "Could not parse StreamEvent type union");

        var declaration = source[(typeStart + marker.Length)..lineEnd].Trim().TrimEnd(';');
        return declaration
            .Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(value => value.Trim().Trim('"'))
            .ToList();
    }

    private static List<string> SerializeTopLevelKeys<T>(T value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, WebJson));
        return document.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .ToList();
    }

    private static string GetFrontendFilePath(string fileName)
    {
        return Path.GetFullPath(
            Path.Combine(
                AppContext.BaseDirectory,
                "..",
                "..",
                "..",
                "..",
                "..",
                "src",
                "QuillForge.Web",
                "Client",
                "src",
                fileName));
    }

    private static int FindMatchingBrace(string source, int openingBraceIndex)
    {
        var depth = 0;
        for (var i = openingBraceIndex; i < source.Length; i++)
        {
            if (source[i] == '{')
            {
                depth++;
            }
            else if (source[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }
}
