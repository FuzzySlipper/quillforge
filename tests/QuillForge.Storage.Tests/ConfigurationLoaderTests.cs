using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Storage.Configuration;

namespace QuillForge.Storage.Tests;

public class ConfigurationLoaderTests : IDisposable
{
    private readonly string _tempDir;
    private readonly ConfigurationLoader _loader;

    public ConfigurationLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "quillforge-config-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _loader = new ConfigurationLoader(NullLoggerFactory.Instance.CreateLogger<ConfigurationLoader>());
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true);
    }

    private AppConfig SaveAndReload(AppConfig config)
    {
        var path = Path.Combine(_tempDir, $"config-{Guid.NewGuid():N}.yaml");
        File.WriteAllText(path, ConfigurationLoader.Serialize(config));
        return _loader.Load(path);
    }

    // ── Loading tests ──

    [Fact]
    public void MissingFile_ReturnsDefaults()
    {
        var config = _loader.Load(Path.Combine(_tempDir, "nonexistent.yaml"));

        Assert.Equal("default", config.Models.Orchestrator);
        Assert.Equal("default", config.Lore.Active);
        Assert.Equal(7.0, config.Forge.ReviewPassThreshold);
    }

    [Fact]
    public void ValidYaml_ParsesCorrectly()
    {
        var yaml = """
            models:
              orchestrator: claude
              prose_writer: gpt-4o
            lore:
              active: fantasy-world
            forge:
              review_pass_threshold: 8.0
              max_revisions: 5
            web_search:
              enabled: true
              provider: searxng
              searxng_url: http://localhost:8080
            """;

        var path = Path.Combine(_tempDir, "config.yaml");
        File.WriteAllText(path, yaml);

        var config = _loader.Load(path);

        Assert.Equal("claude", config.Models.Orchestrator);
        Assert.Equal("gpt-4o", config.Models.ProseWriter);
        Assert.Equal("fantasy-world", config.Lore.Active);
        Assert.Equal(8.0, config.Forge.ReviewPassThreshold);
        Assert.Equal(5, config.Forge.MaxRevisions);
        Assert.True(config.WebSearch.Enabled);
        Assert.Equal("http://localhost:8080", config.WebSearch.SearxngUrl);
    }

    [Fact]
    public void PartialYaml_MergesWithDefaults()
    {
        var yaml = """
            models:
              orchestrator: claude
            """;

        var path = Path.Combine(_tempDir, "config.yaml");
        File.WriteAllText(path, yaml);

        var config = _loader.Load(path);

        Assert.Equal("claude", config.Models.Orchestrator);
        Assert.Equal("default", config.Models.ProseWriter);
        Assert.Equal("default", config.Lore.Active);
    }

    [Fact]
    public void MalformedYaml_ReturnsDefaults()
    {
        var path = Path.Combine(_tempDir, "config.yaml");
        File.WriteAllText(path, ": this is not valid yaml [[[");

        var config = _loader.Load(path);

        Assert.Equal("default", config.Models.Orchestrator);
    }

    [Fact]
    public void WriteDefaults_CreatesValidFile()
    {
        var path = Path.Combine(_tempDir, "config.yaml");
        _loader.WriteDefaults(path);

        Assert.True(File.Exists(path));

        var config = _loader.Load(path);
        Assert.Equal("default", config.Models.Orchestrator);
    }

    // ── Round-trip serialization tests (Serialize → Load) ──

    [Fact]
    public void RoundTrip_Models_AllFieldsPersisted()
    {
        var config = new AppConfig
        {
            Models = new ModelsConfig
            {
                Orchestrator = "claude-opus",
                ProseWriter = "gpt-4o",
                Librarian = "llama3",
                ForgeWriter = "mistral",
                ForgePlanner = "gemini-pro",
                ForgeReviewer = "claude-haiku",
            }
        };

        var loaded = SaveAndReload(config);

        Assert.Equal("claude-opus", loaded.Models.Orchestrator);
        Assert.Equal("gpt-4o", loaded.Models.ProseWriter);
        Assert.Equal("llama3", loaded.Models.Librarian);
        Assert.Equal("mistral", loaded.Models.ForgeWriter);
        Assert.Equal("gemini-pro", loaded.Models.ForgePlanner);
        Assert.Equal("claude-haiku", loaded.Models.ForgeReviewer);
    }

    [Fact]
    public void RoundTrip_Persona_ActiveAndMaxTokensPersisted()
    {
        var config = new AppConfig
        {
            Persona = new PersonaConfig { Active = "novelist", MaxTokens = 12000 }
        };

        var loaded = SaveAndReload(config);

        Assert.Equal("novelist", loaded.Persona.Active);
        Assert.Equal(12000, loaded.Persona.MaxTokens);
    }

    [Fact]
    public void RoundTrip_Lore_ActivePersisted()
    {
        var config = new AppConfig
        {
            Lore = new LoreConfig { Active = "sci-fi-universe" }
        };

        var loaded = SaveAndReload(config);

        Assert.Equal("sci-fi-universe", loaded.Lore.Active);
    }

    [Fact]
    public void RoundTrip_WritingStyle_ActivePersisted()
    {
        var config = new AppConfig
        {
            WritingStyle = new WritingStyleConfig { Active = "hemingway" }
        };

        var loaded = SaveAndReload(config);

        Assert.Equal("hemingway", loaded.WritingStyle.Active);
    }

    [Fact]
    public void RoundTrip_Layout_ActivePersisted()
    {
        var config = new AppConfig
        {
            Layout = new LayoutConfig { Active = "split" }
        };

        var loaded = SaveAndReload(config);

        Assert.Equal("split", loaded.Layout.Active);
    }

    [Fact]
    public void RoundTrip_Roleplay_CharactersPersisted()
    {
        var config = new AppConfig
        {
            Roleplay = new RoleplayConfig
            {
                AiCharacter = "dragon-sage",
                UserCharacter = "wandering-knight",
            }
        };

        var loaded = SaveAndReload(config);

        Assert.Equal("dragon-sage", loaded.Roleplay.AiCharacter);
        Assert.Equal("wandering-knight", loaded.Roleplay.UserCharacter);
    }

    [Fact]
    public void RoundTrip_Roleplay_NullCharactersStayNull()
    {
        var config = new AppConfig
        {
            Roleplay = new RoleplayConfig { AiCharacter = null, UserCharacter = null }
        };

        var loaded = SaveAndReload(config);

        Assert.Null(loaded.Roleplay.AiCharacter);
        Assert.Null(loaded.Roleplay.UserCharacter);
    }

    [Fact]
    public void RoundTrip_Forge_AllFieldsPersisted()
    {
        var config = new AppConfig
        {
            Forge = new ForgeConfig
            {
                ReviewPassThreshold = 8.5,
                MaxRevisions = 10,
                PauseAfterChapter1 = false,
                StageTimeoutMinutes = 60,
            }
        };

        var loaded = SaveAndReload(config);

        Assert.Equal(8.5, loaded.Forge.ReviewPassThreshold);
        Assert.Equal(10, loaded.Forge.MaxRevisions);
        Assert.False(loaded.Forge.PauseAfterChapter1);
        Assert.Equal(60, loaded.Forge.StageTimeoutMinutes);
    }

    [Fact]
    public void RoundTrip_WebSearch_AllFieldsPersisted()
    {
        var config = new AppConfig
        {
            WebSearch = new WebSearchConfig
            {
                Enabled = true,
                Provider = "tavily",
                SearxngUrl = "http://search.local:8080",
                TavilyApiKey = "tvly-abc123",
                BraveApiKey = "brave-xyz",
                GoogleApiKey = "goog-key",
                GoogleCxId = "cx-123",
                MaxResults = 25,
            }
        };

        var loaded = SaveAndReload(config);

        Assert.True(loaded.WebSearch.Enabled);
        Assert.Equal("tavily", loaded.WebSearch.Provider);
        Assert.Equal("http://search.local:8080", loaded.WebSearch.SearxngUrl);
        Assert.Equal("tvly-abc123", loaded.WebSearch.TavilyApiKey);
        Assert.Equal("brave-xyz", loaded.WebSearch.BraveApiKey);
        Assert.Equal("goog-key", loaded.WebSearch.GoogleApiKey);
        Assert.Equal("cx-123", loaded.WebSearch.GoogleCxId);
        Assert.Equal(25, loaded.WebSearch.MaxResults);
    }

    [Fact]
    public void RoundTrip_Email_AllFieldsPersisted()
    {
        var config = new AppConfig
        {
            Email = new EmailConfig
            {
                ResendApiKey = "re_abc123",
                DeveloperEmail = "dev@example.com",
            }
        };

        var loaded = SaveAndReload(config);

        Assert.Equal("re_abc123", loaded.Email.ResendApiKey);
        Assert.Equal("dev@example.com", loaded.Email.DeveloperEmail);
    }

    [Fact]
    public void RoundTrip_FullConfig_AllSectionsPreserved()
    {
        var config = new AppConfig
        {
            Models = new ModelsConfig
            {
                Orchestrator = "claude-opus",
                ProseWriter = "gpt-4o",
                Librarian = "llama3",
                ForgeWriter = "mistral",
                ForgePlanner = "gemini",
                ForgeReviewer = "haiku",
            },
            Persona = new PersonaConfig { Active = "editor", MaxTokens = 8000 },
            Lore = new LoreConfig { Active = "fantasy" },
            WritingStyle = new WritingStyleConfig { Active = "poetic" },
            Layout = new LayoutConfig { Active = "rpg" },
            Roleplay = new RoleplayConfig { AiCharacter = "sage", UserCharacter = "hero" },
            Forge = new ForgeConfig
            {
                ReviewPassThreshold = 9.0,
                MaxRevisions = 2,
                PauseAfterChapter1 = false,
                StageTimeoutMinutes = 30,
            },
            WebSearch = new WebSearchConfig
            {
                Enabled = true,
                Provider = "searxng",
                SearxngUrl = "http://localhost:8080",
                MaxResults = 100,
            },
            Email = new EmailConfig { DeveloperEmail = "test@test.com" },
        };

        var loaded = SaveAndReload(config);

        // Models
        Assert.Equal("claude-opus", loaded.Models.Orchestrator);
        Assert.Equal("gpt-4o", loaded.Models.ProseWriter);
        Assert.Equal("llama3", loaded.Models.Librarian);
        Assert.Equal("mistral", loaded.Models.ForgeWriter);
        Assert.Equal("gemini", loaded.Models.ForgePlanner);
        Assert.Equal("haiku", loaded.Models.ForgeReviewer);
        // Persona
        Assert.Equal("editor", loaded.Persona.Active);
        Assert.Equal(8000, loaded.Persona.MaxTokens);
        // Lore
        Assert.Equal("fantasy", loaded.Lore.Active);
        // WritingStyle
        Assert.Equal("poetic", loaded.WritingStyle.Active);
        // Layout
        Assert.Equal("rpg", loaded.Layout.Active);
        // Roleplay
        Assert.Equal("sage", loaded.Roleplay.AiCharacter);
        Assert.Equal("hero", loaded.Roleplay.UserCharacter);
        // Forge
        Assert.Equal(9.0, loaded.Forge.ReviewPassThreshold);
        Assert.Equal(2, loaded.Forge.MaxRevisions);
        Assert.False(loaded.Forge.PauseAfterChapter1);
        Assert.Equal(30, loaded.Forge.StageTimeoutMinutes);
        // WebSearch
        Assert.True(loaded.WebSearch.Enabled);
        Assert.Equal("searxng", loaded.WebSearch.Provider);
        Assert.Equal("http://localhost:8080", loaded.WebSearch.SearxngUrl);
        Assert.Equal(100, loaded.WebSearch.MaxResults);
        // Email
        Assert.Equal("test@test.com", loaded.Email.DeveloperEmail);
    }

    [Fact]
    public void RoundTrip_LayoutChange_OnlyLayoutDiffers()
    {
        var original = new AppConfig
        {
            Models = new ModelsConfig { Orchestrator = "claude" },
            Persona = new PersonaConfig { Active = "editor", MaxTokens = 6000 },
            Layout = new LayoutConfig { Active = "default" },
        };

        // Simulate what the /api/layout endpoint does: change layout, serialize, reload
        original.Layout = new LayoutConfig { Active = "split" };
        var loaded = SaveAndReload(original);

        Assert.Equal("split", loaded.Layout.Active);
        Assert.Equal("claude", loaded.Models.Orchestrator);
        Assert.Equal("editor", loaded.Persona.Active);
    }

    [Fact]
    public void RoundTrip_DefaultConfig_RoundTripsCleanly()
    {
        var defaults = new AppConfig();
        var loaded = SaveAndReload(defaults);

        Assert.Equal(defaults.Models.Orchestrator, loaded.Models.Orchestrator);
        Assert.Equal(defaults.Persona.Active, loaded.Persona.Active);
        Assert.Equal(defaults.Persona.MaxTokens, loaded.Persona.MaxTokens);
        Assert.Equal(defaults.Lore.Active, loaded.Lore.Active);
        Assert.Equal(defaults.WritingStyle.Active, loaded.WritingStyle.Active);
        Assert.Equal(defaults.Layout.Active, loaded.Layout.Active);
        Assert.Equal(defaults.Forge.ReviewPassThreshold, loaded.Forge.ReviewPassThreshold);
        Assert.Equal(defaults.Forge.MaxRevisions, loaded.Forge.MaxRevisions);
        Assert.Equal(defaults.Forge.PauseAfterChapter1, loaded.Forge.PauseAfterChapter1);
        Assert.Equal(defaults.Forge.StageTimeoutMinutes, loaded.Forge.StageTimeoutMinutes);
        Assert.Equal(defaults.WebSearch.Enabled, loaded.WebSearch.Enabled);
        Assert.Equal(defaults.WebSearch.MaxResults, loaded.WebSearch.MaxResults);
    }

    // ── FirstRunSetup tests ──

    [Fact]
    public void FirstRunSetup_CreatesDirectoryStructure()
    {
        var contentRoot = Path.Combine(_tempDir, "build");
        var setup = new FirstRunSetup(NullLoggerFactory.Instance.CreateLogger<FirstRunSetup>());

        var isFirstRun = setup.EnsureContentDirectory(contentRoot);

        Assert.True(isFirstRun);
        Assert.True(Directory.Exists(Path.Combine(contentRoot, "lore", "default")));
        Assert.True(Directory.Exists(Path.Combine(contentRoot, "conductor")));
        Assert.False(Directory.Exists(Path.Combine(contentRoot, "persona")));
        Assert.True(Directory.Exists(Path.Combine(contentRoot, "data", "sessions")));
        Assert.True(File.Exists(Path.Combine(contentRoot, "conductor", "default.md")));
        Assert.True(File.Exists(Path.Combine(contentRoot, "writing-styles", "default.md")));
        Assert.True(File.Exists(Path.Combine(contentRoot, "config.yaml")));
    }

    [Fact]
    public void FirstRunSetup_SecondRun_ReturnsFalse()
    {
        var contentRoot = Path.Combine(_tempDir, "build");
        var setup = new FirstRunSetup(NullLoggerFactory.Instance.CreateLogger<FirstRunSetup>());

        setup.EnsureContentDirectory(contentRoot);
        var isSecondRun = setup.EnsureContentDirectory(contentRoot);

        Assert.False(isSecondRun);
    }
}
