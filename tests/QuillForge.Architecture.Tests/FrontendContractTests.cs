using System.Text.Json;
using QuillForge.Core.Models;
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
    public void AppSettingsResponse_StaysInSyncWith_ApiInterface()
    {
        var shape = GetTypeScriptInterfaceShape("api.ts", "AppSettings");
        var jsonKeys = SerializeTopLevelKeys(new AppSettingsResponse
        {
            WebSearch = new WebSearchSettingsResponse
            {
                Enabled = true,
                Provider = "zai",
                SearxngUrl = "http://localhost:8080",
                TavilyApiKeySet = true,
                BraveApiKeySet = false,
                GoogleApiKeySet = false,
                GoogleCxId = "cx-123",
                ZaiApiKeySet = true,
                ZaiMcpEndpoint = "https://api.z.ai/api/mcp/web_search_prime/mcp",
                ZaiMcpToolName = "webSearchPrime",
                MaxResults = 10,
                SupportedProviders = ["searxng", "tavily", "brave", "google", "zai"],
            },
        });

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("WebSearchSettings", shape["webSearch"]);
    }

    [Fact]
    public void WebSearchSettingsResponse_StaysInSyncWith_ApiInterface()
    {
        var shape = GetTypeScriptInterfaceShape("api.ts", "WebSearchSettings");
        var jsonKeys = SerializeTopLevelKeys(new WebSearchSettingsResponse
        {
            Enabled = true,
            Provider = "zai",
            SearxngUrl = "http://localhost:8080",
            TavilyApiKeySet = true,
            BraveApiKeySet = false,
            GoogleApiKeySet = false,
            GoogleCxId = "cx-123",
            ZaiApiKeySet = true,
            ZaiMcpEndpoint = "https://api.z.ai/api/mcp/web_search_prime/mcp",
            ZaiMcpToolName = "webSearchPrime",
            MaxResults = 10,
            SupportedProviders = ["searxng", "tavily", "brave", "google", "zai"],
        });

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("boolean", shape["enabled"]);
        Assert.Equal("string", shape["provider"]);
        Assert.Equal("string | null", shape["searxngUrl"]);
        Assert.Equal("number", shape["maxResults"]);
        Assert.Equal("string[]", shape["supportedProviders"]);
    }

    [Fact]
    public void ForgeProjectStatusResponse_StaysInSyncWith_ApiInterface()
    {
        var shape = GetTypeScriptInterfaceShape("api.ts", "ForgeProjectStatus");
        var jsonKeys = SerializeTopLevelKeys(new ForgeStatusResponse
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
            },
            Stats = new ForgeStats
            {
                TotalInputTokens = 100,
                TotalOutputTokens = 200,
                AgentCalls = 3,
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
            ],
        });

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("ForgeProjectDocumentInfo[]", shape["documents"]);
        Assert.Equal("Record<string, ForgeChapterStatusInfo>", shape["chapters"]);
        Assert.Equal("ForgeStatsInfo", shape["stats"]);
    }

    [Fact]
    public void ForgeProjectDocumentDto_StaysInSyncWith_ApiInterface()
    {
        var shape = GetTypeScriptInterfaceShape("api.ts", "ForgeProjectDocumentInfo");
        var jsonKeys = SerializeTopLevelKeys(new ForgeProjectDocumentDto
        {
            Kind = "outputStory",
            Label = "Output story",
            RelativePath = "forge/ember-archive/output/story.md",
            Href = "/content/forge/ember-archive/output/story.md",
        });

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("string", shape["kind"]);
        Assert.Equal("string", shape["label"]);
        Assert.Equal("string", shape["relativePath"]);
        Assert.Equal("string", shape["href"]);
    }

    [Fact]
    public void ForgeWorkspace_UsesStatusDocumentContractInsteadOfHeadProbes()
    {
        var source = File.ReadAllText(GetFrontendFilePath("components", "ForgeWorkspace.tsx"));

        Assert.DoesNotContain("method: \"HEAD\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("method: 'HEAD'", source, StringComparison.Ordinal);
        Assert.DoesNotContain("/content/forge/", source, StringComparison.Ordinal);
        Assert.Contains("forgeStatus?.documents", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AppRail_ExposesAppSettingsBesideProviders()
    {
        var source = File.ReadAllText(GetFrontendFilePath("components", "AppRail.tsx"));

        Assert.Contains("onOpenProviders", source, StringComparison.Ordinal);
        Assert.Contains("onOpenAppSettings", source, StringComparison.Ordinal);
        Assert.Contains("title=\"App settings\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FrontendModeUnion_ListsAllSupportedModes()
    {
        var modeValues = GetTypeScriptUnionValues("Mode");

        var expectedModes = new[] { "guide", "writer", "roleplay", "lore", "forge", "council", "research", "games" }
            .OrderBy(value => value)
            .ToList();

        Assert.Equal(expectedModes, modeValues.OrderBy(value => value).ToList());
    }

    [Fact]
    public void GameViewResponse_StaysInSyncWith_GameViewResponseInterface()
    {
        var shape = GetTypeScriptInterfaceShape("GameViewResponse");
        var jsonKeys = SerializeTopLevelKeys(new GameViewResponse
        {
            View = EmptyGameBridgeView(),
        });

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("GameBridgeView", shape["view"]);
    }

    [Fact]
    public void GameBridgeView_StaysInSyncWith_GameBridgeViewInterface()
    {
        var shape = GetTypeScriptInterfaceShape("GameBridgeView");
        var jsonKeys = SerializeTopLevelKeys(EmptyGameBridgeView());

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("GameRuntimeStatus", shape["status"]);
        Assert.Equal("number | null", shape["roundNumber"]);
        Assert.Equal("GameBridgeParticipantView[]", shape["roster"]);
        Assert.Equal("GameBridgePlayerView | null", shape["player"]);
    }

    [Fact]
    public void GameDiagnosticLogResponse_StaysInSyncWith_GameDiagnosticLogResponseInterface()
    {
        var shape = GetTypeScriptInterfaceShape("GameDiagnosticLogResponse");
        var jsonKeys = SerializeTopLevelKeys(new GameDiagnosticLogResponse
        {
            Log = SampleGameDiagnosticLogProjection(),
        });

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("GameDiagnosticLogProjection", shape["log"]);
    }

    [Fact]
    public void GameDiagnosticLogProjection_StaysInSyncWith_GameDiagnosticLogProjectionInterface()
    {
        var shape = GetTypeScriptInterfaceShape("GameDiagnosticLogProjection");
        var jsonKeys = SerializeTopLevelKeys(SampleGameDiagnosticLogProjection());

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("string", shape["sessionId"]);
        Assert.Equal("boolean", shape["hasGame"]);
        Assert.Equal("string | null", shape["requestedGameInstanceId"]);
        Assert.Equal("boolean", shape["scopeMatchesActiveGame"]);
        Assert.Equal("number | null", shape["limit"]);
        Assert.Equal("number | null", shape["beforeSequence"]);
        Assert.Equal("GameDiagnosticLogCategory[]", shape["categories"]);
        Assert.Equal("number", shape["totalEventCount"]);
        Assert.Equal("number", shape["filteredEventCount"]);
        Assert.Equal("number", shape["returnedEventCount"]);
        Assert.Equal("boolean", shape["hasMore"]);
        Assert.Equal("number | null", shape["nextBeforeSequence"]);
        Assert.Equal("GameDiagnosticLogEvent[]", shape["events"]);
    }

    [Fact]
    public void GameDiagnosticLogEvent_StaysInSyncWith_GameDiagnosticLogEventInterface()
    {
        var shape = GetTypeScriptInterfaceShape("GameDiagnosticLogEvent");
        var jsonKeys = SerializeTopLevelKeys(new GameDiagnosticLogEvent
        {
            Sequence = 1,
            Timestamp = DateTimeOffset.Parse("2026-04-29T20:00:00+00:00"),
            Level = GameDiagnosticLogLevel.Warning,
            Category = GameDiagnosticLogCategory.Rejection,
            Source = "GameRuntimeService",
            Operation = "CommunicationRejected",
            Summary = "Public messages are disabled.",
            ReasonCode = "public_messages_disabled",
            ParticipantId = "seat-1",
            ProviderAlias = "local",
            Model = "llama3.2",
            PromptTokens = 10,
            ResponseTokens = 2,
            PromptPreview = "prompt",
            ResponsePreview = "response",
            Details = new Dictionary<string, string?> { ["endpoint"] = "post-public-message" },
        });

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("GameDiagnosticLogLevel", shape["level"]);
        Assert.Equal("GameDiagnosticLogCategory", shape["category"]);
        Assert.Equal("Record<string, string | null>", shape["details"]);
    }

    [Fact]
    public void GameTemplateListResponse_StaysInSyncWith_GameTemplateListResponseInterface()
    {
        var shape = GetTypeScriptInterfaceShape("GameTemplateListResponse");
        var jsonKeys = SerializeTopLevelKeys(new GameTemplateListResponse
        {
            Templates = [new GameTemplateSummary
            {
                TemplateId = "village",
                DisplayName = "Village Werewolf",
                ModuleId = "werewolf",
                MinimumModuleVersion = "0.1.0",
                MaximumModuleVersion = "0.1.0",
            }],
        });

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("GameTemplateSummary[]", shape["templates"]);
    }

    [Fact]
    public void GameTemplateCatalogResponse_StaysInSyncWith_GameTemplateCatalogResponseInterface()
    {
        var shape = GetTypeScriptInterfaceShape("GameTemplateCatalogResponse");
        var jsonKeys = SerializeTopLevelKeys(new GameTemplateCatalogResponse
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
                    SetupFields = [],
                    Stages = [],
                    ActionForms = [],
                    PromptAssets = [],
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
            Providers = [new GameTemplateProviderOption { Alias = "local", Type = "Ollama", Model = "llama3.2", DefaultModel = "llama3.2", ContextLimit = 8192 }],
        });

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("GameTemplateModuleOption[]", shape["modules"]);
        Assert.Equal("GameTemplateProviderOption[]", shape["providers"]);
    }

    [Fact]
    public void GameTemplateProviderOption_StaysInSyncWith_GameTemplateProviderOptionInterface()
    {
        var shape = GetTypeScriptInterfaceShape("GameTemplateProviderOption");
        var jsonKeys = SerializeTopLevelKeys(new GameTemplateProviderOption
        {
            Alias = "local",
            Type = "Ollama",
            Model = "llama3.2",
            DefaultModel = "llama3.2",
            ContextLimit = 8192,
        });

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("string", shape["alias"]);
        Assert.Equal("string", shape["type"]);
        Assert.Equal("string | null", shape["model"]);
        Assert.Equal("string | null", shape["defaultModel"]);
        Assert.Equal("number | null", shape["contextLimit"]);
    }

    [Fact]
    public void GameTemplateResponse_StaysInSyncWith_GameTemplateResponseInterface()
    {
        var shape = GetTypeScriptInterfaceShape("GameTemplateResponse");
        var jsonKeys = SerializeTopLevelKeys(new GameTemplateResponse
        {
            Template = SampleGameTemplate(),
            Validation = GameTemplateValidationResult.Valid,
        });

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("GameTemplate", shape["template"]);
        Assert.Equal("GameTemplateValidationResult", shape["validation"]);
    }

    [Fact]
    public void GameTemplate_StaysInSyncWith_GameTemplateInterface()
    {
        var shape = GetTypeScriptInterfaceShape("GameTemplate");
        var jsonKeys = SerializeTopLevelKeys(SampleGameTemplate());

        Assert.Equal(shape.Keys.OrderBy(key => key), jsonKeys.OrderBy(key => key));
        Assert.Equal("GameTemplateModuleSelection", shape["module"]);
        Assert.Equal("GameTemplateRosterSettings", shape["roster"]);
        Assert.Equal("GameTemplateAgentPlayerConfig[]", GetTypeScriptInterfaceShape("GameTemplateRosterSettings")["agentPlayers"]);
    }

    [Fact]
    public void GameTemplateEditor_UsesTypedTemplateApisAndSurfacesServiceValidation()
    {
        var source = File.ReadAllText(GetFrontendFilePath("components", "GameTemplateEditor.tsx"));
        var normalizedSource = source.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("getGameTemplateCatalog", source, StringComparison.Ordinal);
        Assert.Contains("getGameTemplate", source, StringComparison.Ordinal);
        Assert.Contains("saveGameTemplate", source, StringComparison.Ordinal);
        Assert.Contains("cloneGameTemplate", source, StringComparison.Ordinal);
        Assert.Contains("deleteGameTemplate", source, StringComparison.Ordinal);
        Assert.Contains("validateGameTemplate", source, StringComparison.Ordinal);
        Assert.Contains("listGamePersonaPrompts", source, StringComparison.Ordinal);
        Assert.Contains("openGamePersonaPrompt", source, StringComparison.Ordinal);
        Assert.Contains("writeGamePersonaPrompt", source, StringComparison.Ordinal);
        Assert.Contains("listGamePromptTemplates", source, StringComparison.Ordinal);
        Assert.Contains("openGamePromptTemplate", source, StringComparison.Ordinal);
        Assert.Contains("writeGamePromptTemplate", source, StringComparison.Ordinal);
        Assert.Contains("AI player system prompt", source, StringComparison.Ordinal);
        Assert.Contains("Editing Default creates a user-owned markdown copy", source, StringComparison.Ordinal);
        Assert.Contains("promptSelectionValueForOptions", source, StringComparison.Ordinal);
        Assert.Contains("promptSelectionIsAvailable", source, StringComparison.Ordinal);
        Assert.Contains("promptOptionsForSelect", source, StringComparison.Ordinal);
        Assert.Contains("promptOptionsModuleId", source, StringComparison.Ordinal);
        Assert.Contains("promptOptionsLoadedForSelectedModule", source, StringComparison.Ordinal);
        Assert.Contains("systemPromptTemplate: DEFAULT_PROMPT_SELECTION", source, StringComparison.Ordinal);
        Assert.Contains("Module changed. AI player system prompt selections were reset to Default", source, StringComparison.Ordinal);
        Assert.Contains("Saved prompt selection is unavailable for this module", source, StringComparison.Ordinal);
        Assert.Contains("value={promptSelectValue}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("value={promptSelectionValue(agent.systemPromptTemplate)}", source, StringComparison.Ordinal);
        Assert.Contains("<MDEditor", source, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"game-prompt-editor-overlay\"", source, StringComparison.Ordinal);
        Assert.Contains("role=\"dialog\"", source, StringComparison.Ordinal);
        Assert.Contains("aria-modal=\"true\"", source, StringComparison.Ordinal);
        Assert.Contains("Save or discard the prompt changes before closing the editor.", source, StringComparison.Ordinal);
        Assert.Contains("selected for {promptEditor.agentParticipantId}", source, StringComparison.Ordinal);
        Assert.Contains("fixed inset-0", source, StringComparison.Ordinal);
        Assert.Contains("setError(null);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<section className=\"rounded-xl border border-border/60 bg-surface/60 px-3 py-3\">\n          <div className=\"mb-3 flex items-center justify-between gap-3\">\n            <div>\n              <div className=\"qf-shell-folio\">Game Prompt Editor", normalizedSource, StringComparison.Ordinal);
        Assert.Contains("Validation issues from template service", source, StringComparison.Ordinal);
        Assert.Contains("Provider alias", source, StringComparison.Ordinal);
        Assert.Contains("Provider model", source, StringComparison.Ordinal);
        Assert.Contains("provider.model ?? provider.defaultModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Default model", source, StringComparison.Ordinal);
        Assert.Contains("Persona/character prompt", source, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"game-persona-editor-overlay\"", source, StringComparison.Ordinal);
        Assert.Contains("Legacy personality text still loads", source, StringComparison.Ordinal);
        Assert.Contains("characterPrompt", source, StringComparison.Ordinal);
        Assert.Contains("personality", source, StringComparison.Ordinal);
        Assert.DoesNotContain("<input\n                    value={agent.personality", source, StringComparison.Ordinal);
        Assert.Contains("...current", source, StringComparison.Ordinal);
        Assert.Contains("...current.roster", source, StringComparison.Ordinal);
        Assert.Contains("...current.communication", source, StringComparison.Ordinal);
        Assert.Contains("...current.naming", source, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"game-template-editor\"", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ProviderManager_DoesNotExposeDefaultModelAssignmentOptions()
    {
        var source = File.ReadAllText(GetFrontendFilePath("components", "ProviderManager.tsx"));

        Assert.Contains("Saving a provider fills unassigned agent rows", source, StringComparison.Ordinal);
        Assert.Contains("Choose provider...", source, StringComparison.Ordinal);
        Assert.Contains("model-id (or click Fetch Models)", source, StringComparison.Ordinal);
        Assert.Contains("Leave blank while editing to keep the current provider model", source, StringComparison.Ordinal);
        Assert.Contains("if (form.model) updates.model = form.model", source, StringComparison.Ordinal);
        Assert.DoesNotContain("default (primary model)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Default model", source, StringComparison.Ordinal);
        Assert.DoesNotContain("use provider default", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ApiClient_FormatsStructuredGameErrorsForUserVisibleAlerts()
    {
        var source = File.ReadAllText(GetFrontendFilePath("api.ts"));

        Assert.Contains("formatErrorBody", source, StringComparison.Ordinal);
        Assert.Contains("reasonCode", source, StringComparison.Ordinal);
        Assert.Contains("typeof parsed.ReasonCode === \"string\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain(": typeof parsed.error === \"string\"\n          ? parsed.error\n          : typeof parsed.Error === \"string\"\n            ? parsed.Error\n            : null;\n    const diagnosticHint", source.Replace("\r\n", "\n", StringComparison.Ordinal), StringComparison.Ordinal);
        Assert.Contains("diagnosticHint", source, StringComparison.Ordinal);
        Assert.Contains("JSON.parse(body)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GamesWorkspace_UsesTypedGameEndpointsAndRendersNoGameState()
    {
        var source = File.ReadAllText(GetFrontendFilePath("components", "GamesWorkspace.tsx"));

        Assert.Contains("getGameView", source, StringComparison.Ordinal);
        Assert.Contains("startGameFromTemplate", source, StringComparison.Ordinal);
        Assert.Contains("submitGameAction", source, StringComparison.Ordinal);
        Assert.Contains("postGamePublicMessage", source, StringComparison.Ordinal);
        Assert.Contains("getGameDiagnosticLog", source, StringComparison.Ordinal);
        Assert.Contains("diagnosticScopeGameInstanceId", source, StringComparison.Ordinal);
        Assert.Contains("scopeMatchesActiveGame", source, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"game-diagnostic-log-scope\"", source, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"game-diagnostic-log-filters\"", source, StringComparison.Ordinal);
        Assert.Contains("Load Older", source, StringComparison.Ordinal);
        Assert.Contains("nextBeforeSequence", source, StringComparison.Ordinal);
        Assert.Contains("diagnosticCategoryFilter", source, StringComparison.Ordinal);
        Assert.Contains("diagnosticLimitFilter", source, StringComparison.Ordinal);
        Assert.Contains("withMutation(action: () => Promise<string | null | undefined>)", source, StringComparison.Ordinal);
        Assert.Contains("refreshDiagnosticLog(hasNextDiagnosticScope ? nextDiagnosticScope : diagnosticScopeGameInstanceId)", source, StringComparison.Ordinal);
        Assert.Contains("Diagnostic Log", source, StringComparison.Ordinal);
        Assert.Contains("Copy JSON", source, StringComparison.Ordinal);
        Assert.Contains("Export JSON", source, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"game-diagnostic-log\"", source, StringComparison.Ordinal);
        Assert.Contains("<GameTemplateEditor", source, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"game-template-editor-dialog\"", source, StringComparison.Ordinal);
        Assert.Contains("Open Template Editor", source, StringComparison.Ordinal);
        Assert.Contains("<WerewolfGamePanel", source, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"games-no-game-state\"", source, StringComparison.Ordinal);
        Assert.Contains("No game is running in this session.", source, StringComparison.Ordinal);
        Assert.Contains("field.valueKind === \"ChoiceName\"", source, StringComparison.Ordinal);
        Assert.Contains("this workspace only submits typed legal choices", source, StringComparison.Ordinal);
        Assert.DoesNotContain("field.name === \"choiceName\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sendChatStream", source, StringComparison.Ordinal);
    }

    [Fact]
    public void GamesWorkspace_OpensTemplateEditorInStatefulDialogOutsideRosterSurface()
    {
        var source = File.ReadAllText(GetFrontendFilePath("components", "GamesWorkspace.tsx"));

        Assert.Contains("templateEditorOpen", source, StringComparison.Ordinal);
        Assert.Contains("templateEditorMounted", source, StringComparison.Ordinal);
        Assert.Contains("setTemplateEditorMounted(true)", source, StringComparison.Ordinal);
        Assert.Contains("const openTemplateEditor = useCallback", source, StringComparison.Ordinal);
        Assert.Contains("const closeTemplateEditor = useCallback", source, StringComparison.Ordinal);
        Assert.Contains("onClose={closeTemplateEditor}", source, StringComparison.Ordinal);
        Assert.Contains("if (!mounted) {", source, StringComparison.Ordinal);
        Assert.Contains("return null;", source, StringComparison.Ordinal);
        Assert.Contains("mounted={templateEditorMounted}", source, StringComparison.Ordinal);
        Assert.Contains("editorTemplateId", source, StringComparison.Ordinal);
        Assert.Contains("role=\"dialog\"", source, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"game-template-editor-dialog\"", source, StringComparison.Ordinal);
        Assert.Contains("Closing hides this panel and keeps unsaved edits in memory", source, StringComparison.Ordinal);
        Assert.Contains("className={open ? \"fixed inset-0", source, StringComparison.Ordinal);
        Assert.Contains(": \"hidden\"}", source, StringComparison.Ordinal);

        var setupStart = source.IndexOf("<aside className=\"qf-shell-card flex min-h-0 flex-col overflow-hidden\">", StringComparison.Ordinal);
        Assert.True(setupStart >= 0, "Could not locate Setup & Roster aside.");

        var tableStart = source.IndexOf("<section className=\"qf-shell-card qf-shell-card--sunken", setupStart, StringComparison.Ordinal);
        Assert.True(tableStart > setupStart, "Could not locate Table State section after Setup & Roster aside.");

        var setupSurface = source[setupStart..tableStart];
        Assert.DoesNotContain("<GameTemplateEditor", setupSurface, StringComparison.Ordinal);
        Assert.Contains("Open Template Editor", setupSurface, StringComparison.Ordinal);
        Assert.Contains("Template editing opens in a separate workshop panel", setupSurface, StringComparison.Ordinal);
    }

    [Fact]
    public void WerewolfGamePanel_UsesTypedGameViewAndKeepsLabelsIsolated()
    {
        var source = File.ReadAllText(GetFrontendFilePath("components", "WerewolfGamePanel.tsx"));

        Assert.Contains("view.moduleId !== \"werewolf\"", source, StringComparison.Ordinal);
        Assert.Contains("Your role is", source, StringComparison.Ordinal);
        Assert.Contains("Werewolf teammates:", source, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"werewolf-panel\"", source, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"werewolf-role-info\"", source, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"werewolf-pending-prompts\"", source, StringComparison.Ordinal);
        Assert.Contains("data-testid=\"werewolf-outcome\"", source, StringComparison.Ordinal);
        Assert.Contains("display-only projections from typed engine facts", source, StringComparison.Ordinal);
        Assert.DoesNotContain("startGameFromTemplate", source, StringComparison.Ordinal);
        Assert.DoesNotContain("submitGameAction", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ModeSwitcher_AndPresentation_RegisterGamesMode()
    {
        var presentation = File.ReadAllText(GetFrontendFilePath("modePresentation.ts"));
        var switcher = File.ReadAllText(GetFrontendFilePath("components", "ModeSwitcher.tsx"));
        var app = File.ReadAllText(GetFrontendFilePath("App.tsx"));

        Assert.Contains("games: \"Games\"", presentation, StringComparison.Ordinal);
        Assert.Contains("/mode-icons/games.svg", presentation, StringComparison.Ordinal);
        Assert.Contains("selectedMode !== \"games\"", switcher, StringComparison.Ordinal);
        var commands = File.ReadAllText(GetFrontendFilePath("commands.ts"));

        Assert.Contains("case \"games\":", app, StringComparison.Ordinal);
        Assert.Contains("<GamesWorkspace", app, StringComparison.Ordinal);
        Assert.Contains("research|games", commands, StringComparison.Ordinal);
        Assert.Contains("\"games\"", commands, StringComparison.Ordinal);
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
        return GetTypeScriptInterfaceShape("types.ts", interfaceName);
    }

    private static Dictionary<string, string> GetTypeScriptInterfaceShape(string fileName, string interfaceName)
    {
        var source = File.ReadAllText(GetFrontendFilePath(fileName));
        var marker = $"export interface {interfaceName} ";
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Could not find interface {interfaceName} in {fileName}");

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

    private static GameBridgeView EmptyGameBridgeView() =>
        new(
            GameRuntimeStatus.NotStarted,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [],
            new GameBridgePublicView([], []),
            null);

    private static GameDiagnosticLogProjection SampleGameDiagnosticLogProjection() =>
        new()
        {
            SessionId = Guid.CreateVersion7(),
            HasGame = true,
            GameInstanceId = "game-1",
            RequestedGameInstanceId = "game-1",
            ScopeMatchesActiveGame = true,
            TemplateId = "village",
            ModuleId = "werewolf",
            RuntimeStatus = "Running",
            PrivacyNotice = "Host-level diagnostic log includes private prompt previews.",
            Limit = 50,
            BeforeSequence = 100,
            Categories = [GameDiagnosticLogCategory.Rejection],
            TotalEventCount = 8,
            FilteredEventCount = 2,
            ReturnedEventCount = 1,
            HasMore = true,
            NextBeforeSequence = 40,
            Events =
            [
                new GameDiagnosticLogEvent
                {
                    Sequence = 1,
                    Timestamp = DateTimeOffset.Parse("2026-04-29T20:00:00+00:00"),
                    Level = GameDiagnosticLogLevel.Info,
                    Category = GameDiagnosticLogCategory.Endpoint,
                    Source = "GameEndpoints",
                    Operation = "DiagnosticsRequested",
                    Summary = "Diagnostic log requested.",
                    Details = new Dictionary<string, string?> { ["route"] = "/diagnostics" },
                },
            ],
        };

    private static GameTemplate SampleGameTemplate() =>
        new()
        {
            TemplateId = "village",
            DisplayName = "Village Werewolf",
            Description = "Baseline behavior-focused Werewolf setup.",
            Module = new GameTemplateModuleSelection
            {
                ModuleId = "werewolf",
                MinimumVersion = "0.1.0",
                MaximumVersion = "0.1.0",
            },
            TemplateVersion = "1.0.0",
            RulesOptions = new GameTemplateRulesOptions
            {
                Values = [new GameTemplateRuleOptionValue { Name = "werewolf_count", Kind = GameTemplateRuleOptionValueKind.Int, IntValue = 1 }],
            },
            Roster = new GameTemplateRosterSettings
            {
                RosterSize = 2,
                UserSeatParticipantId = "seat-1",
                AgentPlayers =
                [
                    new GameTemplateAgentPlayerConfig
                    {
                        ParticipantId = "seat-2",
                        ProviderAlias = "local",
                        ModelOverride = "llama3.2",
                        CharacterPrompt = "Keep claims concise.",
                        Personality = "skeptical villager",
                        FixedName = "Mira",
                        RandomNameBehavior = GameTemplateRandomNameBehavior.UseFixedNameWhenProvided,
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
        };

    private static List<string> SerializeTopLevelKeys<T>(T value)
    {
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(value, WebJson));
        return document.RootElement
            .EnumerateObject()
            .Select(property => property.Name)
            .ToList();
    }

    private static string GetFrontendFilePath(params string[] pathSegments)
    {
        var pathParts = new List<string>
        {
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
        };
        pathParts.AddRange(pathSegments);

        return Path.GetFullPath(Path.Combine(pathParts.ToArray()));
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
