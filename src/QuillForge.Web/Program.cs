using QuillForge.Core;
using QuillForge.Core.Agents;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Agents.Tools;
using QuillForge.Core.Diagnostics;
using QuillForge.Core.Models;
using QuillForge.Core.Pipeline;
using QuillForge.Core.Services;
using QuillForge.Providers.Adapters;
using QuillForge.Providers.ImageGen;
using QuillForge.Providers.Registry;
using QuillForge.Providers.Tts;
using QuillForge.Storage.Configuration;
using QuillForge.Storage.FileSystem;
using QuillForge.Storage.Utilities;
using QuillForge.Web;
using QuillForge.Web.Endpoints;

var builder = WebApplication.CreateBuilder(args);

// --- Content paths and first-run setup ---
// Walk up from executable location or working directory to find the solution root.
// This handles both `dotnet run --project` (cwd = project dir) and published binaries.
var solutionRoot = FindSolutionRoot(AppContext.BaseDirectory)
    ?? FindSolutionRoot(Directory.GetCurrentDirectory());
var contentRoot = builder.Configuration.GetValue<string>("QuillForge:ContentRoot")
    ?? (solutionRoot is not null
        ? Path.Combine(solutionRoot, "build")
        : Path.Combine(AppContext.BaseDirectory, "build"));

var defaultsPath = solutionRoot is not null
    ? Path.Combine(solutionRoot, "dev", "defaults")
    : Path.Combine(AppContext.BaseDirectory, "dev", "defaults");

static string? FindSolutionRoot(string startDir)
{
    var dir = startDir;
    while (dir is not null)
    {
        if (File.Exists(Path.Combine(dir, "QuillForge.slnx")))
            return dir;
        dir = Path.GetDirectoryName(dir);
    }
    return null;
}

var firstRunSetup = new FirstRunSetup(
    LoggerFactory.Create(b => b.AddConsole()).CreateLogger<FirstRunSetup>());
firstRunSetup.EnsureContentDirectory(contentRoot,
    Directory.Exists(defaultsPath) ? defaultsPath : null);

// --- Load configuration (create defaults if missing, even on existing installs) ---
var configPath = Path.Combine(contentRoot, ContentPaths.ConfigFile);
var configLoader = new ConfigurationLoader(
    LoggerFactory.Create(b => b.AddConsole()).CreateLogger<ConfigurationLoader>());
if (!File.Exists(configPath))
{
    configLoader.WriteDefaults(configPath);
}
var appConfig = configLoader.Load(configPath);
builder.Services.AddSingleton(appConfig);

// --- Utilities ---
builder.Services.AddSingleton<AtomicFileWriter>();

// --- LLM debug logging ---
builder.Services.AddSingleton<ILlmDebugLogger>(new LlmDebugLogger(Path.Combine(contentRoot, ContentPaths.Data)));

// --- Storage services (explicit registration, no scanning) ---
builder.Services.AddStorageServices(contentRoot);

// --- Provider persistence (encrypted API key storage) ---
builder.Services.AddSingleton<EncryptedKeyStore>(sp =>
{
    var store = new EncryptedKeyStore(
        Path.Combine(contentRoot, ContentPaths.Data),
        sp.GetRequiredService<AtomicFileWriter>(),
        sp.GetRequiredService<ILogger<EncryptedKeyStore>>());
    store.Initialize();
    return store;
});
builder.Services.AddSingleton(sp =>
    new ProviderConfigStore(
        contentRoot,
        sp.GetRequiredService<EncryptedKeyStore>(),
        sp.GetRequiredService<AtomicFileWriter>(),
        sp.GetRequiredService<ILogger<ProviderConfigStore>>()));

// --- Provider registry and default completion service ---
builder.Services.AddSingleton<ProviderFactory>();
builder.Services.AddSingleton<ProviderRegistry>();
builder.Services.AddSingleton<ICompletionService, QuillForge.Web.Services.DefaultCompletionService>();

// --- Core agents ---
builder.Services.AddSingleton<ContinuationStrategy>();
builder.Services.AddSingleton<ToolLoop>();
builder.Services.AddSingleton<LibrarianAgent>();
builder.Services.AddSingleton<ProseWriterAgent>(sp =>
{
    var toolLoop = sp.GetRequiredService<ToolLoop>();
    var config = sp.GetRequiredService<AppConfig>();
    var loreStore = sp.GetRequiredService<ILoreStore>();
    var queryLore = new QueryLoreHandler(
        sp.GetRequiredService<LibrarianAgent>(),
        loreStore,
        sp.GetRequiredService<IContentFileService>(),
        sp.GetRequiredService<ILogger<QueryLoreHandler>>());
    return new ProseWriterAgent(toolLoop, queryLore, loreStore,
        sp.GetRequiredService<IWritingStyleStore>(),
        config,
        sp.GetRequiredService<ILogger<ProseWriterAgent>>());
});

builder.Services.AddSingleton<DelegatePool>(sp =>
{
    var registry = sp.GetRequiredService<ProviderRegistry>();
    var logger = sp.GetRequiredService<ILogger<DelegatePool>>();
    return new DelegatePool(alias => registry.GetCompletionService(alias), logger);
});

builder.Services.AddSingleton<ICouncilService, CouncilService>();
builder.Services.AddSingleton<RunCouncilHandler>();

builder.Services.AddSingleton<ForgePlannerAgent>();
builder.Services.AddSingleton<ForgeWriterAgent>();
builder.Services.AddSingleton<ForgeReviewerAgent>(sp =>
    new ForgeReviewerAgent(
        sp.GetRequiredService<ICompletionService>(),
        sp.GetRequiredService<AppConfig>(),
        sp.GetRequiredService<ILogger<ForgeReviewerAgent>>()));

// --- Tool handlers (available to orchestrator in all modes) ---
builder.Services.AddToolHandlers(appConfig);

// --- Research agents ---
// Construct ResearchAgent's tools directly to avoid circular DI
// (RunResearchHandler → ResearchPool → ResearchAgent → IEnumerable<IToolHandler> → RunResearchHandler)
builder.Services.AddSingleton<ResearchAgent>(sp =>
{
    var tools = new List<IToolHandler>
    {
        new WriteFileHandler(sp.GetRequiredService<IContentFileService>(),
            sp.GetRequiredService<ILogger<WriteFileHandler>>()),
        new ReadFileHandler(sp.GetRequiredService<IContentFileService>(),
            sp.GetRequiredService<ILogger<ReadFileHandler>>()),
        new ListFilesHandler(sp.GetRequiredService<IContentFileService>(),
            sp.GetRequiredService<ILogger<ListFilesHandler>>()),
    };
    if (appConfig.WebSearch.Enabled)
    {
        // Throttle web searches across all parallel research agents (1 req/sec for Brave, etc.)
        var webSearch = new WebSearchHandler(
            sp.GetRequiredService<IWebSearchService>(),
            sp.GetRequiredService<ILogger<WebSearchHandler>>());
        tools.Add(new ThrottledToolHandler(webSearch, TimeSpan.FromSeconds(1.5)));
    }
    return new ResearchAgent(
        sp.GetRequiredService<ToolLoop>(),
        tools,
        sp.GetRequiredService<AppConfig>(),
        sp.GetRequiredService<ILogger<ResearchAgent>>());
});
builder.Services.AddSingleton<ResearchPool>();
builder.Services.AddSingleton<IToolHandler>(sp =>
    new RunResearchHandler(
        sp.GetRequiredService<ResearchPool>(),
        sp.GetRequiredService<AppConfig>(),
        sp.GetRequiredService<ILogger<RunResearchHandler>>()));

// --- Modes (explicit, no scanning) ---
builder.Services.AddSingleton<IMode, GeneralMode>();
builder.Services.AddSingleton<IMode>(sp => new WriterMode(sp.GetRequiredService<ILogger<WriterMode>>()));
builder.Services.AddSingleton<IMode, RoleplayMode>();
builder.Services.AddSingleton<IMode, ForgeMode>();
builder.Services.AddSingleton<IMode, CouncilMode>();
builder.Services.AddSingleton<IMode, ResearchMode>();

// --- Orchestrator ---
builder.Services.AddSingleton<OrchestratorAgent>();

// --- Pipeline stages ---
builder.Services.AddSingleton<IPipelineStage, PlanningStage>();
builder.Services.AddSingleton<IPipelineStage, DesignStage>();
builder.Services.AddSingleton<IPipelineStage, WritingStage>();
builder.Services.AddSingleton<IPipelineStage, ReviewStage>();
builder.Services.AddSingleton<IPipelineStage, AssemblyStage>();
builder.Services.AddSingleton<ForgePipeline>();

// --- Web search provider ---
if (appConfig.WebSearch.Enabled)
{
    builder.Services.AddSingleton<IWebSearchService>(sp =>
    {
        var httpFactory = sp.GetRequiredService<IHttpClientFactory>();
        var cfg = sp.GetRequiredService<AppConfig>().WebSearch;
        var provider = cfg.Provider.ToLowerInvariant();

        return provider switch
        {
            "tavily" => new QuillForge.Providers.WebSearch.TavilySearchProvider(
                httpFactory.CreateClient("WebSearch"),
                cfg.TavilyApiKey ?? throw new InvalidOperationException("WebSearch provider 'tavily' requires TavilyApiKey"),
                cfg.MaxResults,
                sp.GetRequiredService<ILogger<QuillForge.Providers.WebSearch.TavilySearchProvider>>()),

            "brave" => new QuillForge.Providers.WebSearch.BraveSearchProvider(
                httpFactory.CreateClient("WebSearch"),
                cfg.BraveApiKey ?? throw new InvalidOperationException("WebSearch provider 'brave' requires BraveApiKey"),
                cfg.MaxResults,
                sp.GetRequiredService<ILogger<QuillForge.Providers.WebSearch.BraveSearchProvider>>()),

            "google" => new QuillForge.Providers.WebSearch.GoogleSearchProvider(
                httpFactory.CreateClient("WebSearch"),
                cfg.GoogleApiKey ?? throw new InvalidOperationException("WebSearch provider 'google' requires GoogleApiKey"),
                cfg.GoogleCxId ?? throw new InvalidOperationException("WebSearch provider 'google' requires GoogleCxId"),
                cfg.MaxResults,
                sp.GetRequiredService<ILogger<QuillForge.Providers.WebSearch.GoogleSearchProvider>>()),

            _ => new QuillForge.Providers.WebSearch.SearxngSearchProvider(
                httpFactory.CreateClient("WebSearch"),
                cfg.SearxngUrl ?? throw new InvalidOperationException("WebSearch provider 'searxng' requires SearxngUrl"),
                cfg.MaxResults,
                sp.GetRequiredService<ILogger<QuillForge.Providers.WebSearch.SearxngSearchProvider>>()),
        };
    });
}

// --- Auto-update checker ---
builder.Services.AddHttpClient();
builder.Services.AddSingleton<QuillForge.Web.Services.AutoUpdateService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<QuillForge.Web.Services.AutoUpdateService>());

// --- Image generation and TTS providers ---
builder.Services.AddMediaProviders(contentRoot, appConfig);

// --- CORS for local development ---
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

// --- Global JSON serialization: camelCase for all API responses ---
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
});

var app = builder.Build();

app.UseCors();

// --- Migrate legacy runtime state to session-scoped store ---
{
    var legacy = app.Services.GetRequiredService<RuntimeStateStore>().Load();
    if (!string.IsNullOrEmpty(legacy.LastMode))
    {
        var sessionStore = app.Services.GetRequiredService<ISessionRuntimeStore>();
        var defaultState = await sessionStore.LoadAsync(null);
        defaultState.Mode.ActiveModeName = legacy.LastMode;
        defaultState.Mode.ProjectName = legacy.LastProject;
        defaultState.Mode.CurrentFile = legacy.LastFile;
        defaultState.Mode.Character = legacy.LastCharacter;
        await sessionStore.SaveAsync(defaultState);
        app.Logger.LogInformation("Migrated legacy runtime state: mode={Mode}", legacy.LastMode);
    }
}

// --- Load persisted providers ---
{
    var providerStore = app.Services.GetRequiredService<ProviderConfigStore>();
    var registry = app.Services.GetRequiredService<ProviderRegistry>();
    var loadedConfigs = providerStore.Load();

    foreach (var dto in loadedConfigs)
    {
        if (!Enum.TryParse<ProviderType>(dto.Type, ignoreCase: true, out var providerType))
        {
            app.Logger.LogWarning("Unknown provider type '{Type}' for alias '{Alias}', skipping", dto.Type, dto.Alias);
            continue;
        }

        registry.Register(new ProviderConfig
        {
            Alias = dto.Alias,
            Type = providerType,
            ApiKey = dto.ApiKey ?? "",
            BaseUrl = dto.BaseUrl,
            ModelsUrl = dto.ModelsUrl,
            DefaultModel = dto.DefaultModel,
            ContextLimit = dto.ContextLimit,
            RequiresReasoning = dto.RequiresReasoning,
            Options = dto.Options is not null ? new ProviderOptions
            {
                Temperature = dto.Options.Temperature,
                TopP = dto.Options.TopP,
                TopK = dto.Options.TopK,
                FrequencyPenalty = dto.Options.FrequencyPenalty,
                PresencePenalty = dto.Options.PresencePenalty,
            } : null,
        });
    }

    app.Logger.LogInformation("Loaded {Count} persisted providers", loadedConfigs.Count);

    // Bootstrap: if no providers and ANTHROPIC_API_KEY env var is set, create a default
    if (loadedConfigs.Count == 0)
    {
        var anthropicKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (!string.IsNullOrEmpty(anthropicKey))
        {
            registry.Register(new ProviderConfig
            {
                Alias = "claude",
                Type = ProviderType.Anthropic,
                ApiKey = anthropicKey,
                DefaultModel = "claude-sonnet-4-20250514",
            });

            var dtos = registry.GetAllConfigs().Select(c => new ProviderConfigDto
            {
                Alias = c.Alias,
                Type = c.Type.ToString(),
                ApiKey = c.ApiKey,
                BaseUrl = c.BaseUrl,
                DefaultModel = c.DefaultModel,
            }).ToList();
            providerStore.SaveAsync(dtos).GetAwaiter().GetResult();

            app.Logger.LogInformation("Bootstrapped default 'claude' provider from ANTHROPIC_API_KEY env var");
        }
    }
}

// --- Static files for React frontend ---
app.UseDefaultFiles();
app.UseStaticFiles();

// --- API endpoint groups ---
app.MapStatusEndpoints();
app.MapSessionEndpoints();
app.MapChatEndpoints();
app.MapModeEndpoints();
app.MapProviderEndpoints();
app.MapProviderFetchModelsEndpoint();
app.MapForgeEndpoints();
app.MapForgeManagementEndpoints();
app.MapContentEndpoints(contentRoot);
app.MapProfileEndpoints(contentRoot);
app.MapCharacterCardEndpoints(contentRoot);
app.MapCouncilEndpoints();
app.MapArtifactEndpoints();
app.MapTtsEndpoints();
app.MapResearchEndpoints(contentRoot);
app.MapProbeEndpoints(contentRoot);

// --- Debug bridge (Development only) ---
if (app.Environment.IsDevelopment())
{
    app.MapDebugBridgeEndpoints();
}

// --- SPA fallback ---
app.MapFallbackToFile("index.html");

app.Run();
