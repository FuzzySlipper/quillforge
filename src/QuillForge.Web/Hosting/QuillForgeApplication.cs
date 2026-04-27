using Den.RulesEngine;
using Den.RulesEngine.Werewolf;
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
using QuillForge.Web.Endpoints;
using QuillForge.Web.Services;

namespace QuillForge.Web.Hosting;

internal static class QuillForgeApplication
{
    public static async Task<WebApplication> BuildAsync(string[] args)
    {
        var parsedArgs = BackendLaunchArgumentParser.Parse(args);
        var builderOptions = parsedArgs.ConfigurationOverrides.TryGetValue("QuillForge:Startup:RuntimeRoot", out var runtimeRoot)
            && !string.IsNullOrWhiteSpace(runtimeRoot)
            ? new WebApplicationOptions
            {
                Args = parsedArgs.PassThroughArgs,
                ContentRootPath = runtimeRoot,
            }
            : new WebApplicationOptions
            {
                Args = parsedArgs.PassThroughArgs,
            };

        var builder = WebApplication.CreateBuilder(builderOptions);
        if (parsedArgs.ConfigurationOverrides.Count > 0)
        {
            builder.Configuration.AddInMemoryCollection(parsedArgs.ConfigurationOverrides);
        }

        var launchOptions = BackendLaunchOptions.FromConfiguration(builder.Configuration);
        BackendHostingConfiguration.ApplyOverrides(builder.Configuration, launchOptions);
        var runtimeBaseDirectory = !string.IsNullOrWhiteSpace(launchOptions.RuntimeRoot)
            ? launchOptions.RuntimeRoot
            : AppContext.BaseDirectory;
        var runtimeCurrentDirectory = !string.IsNullOrWhiteSpace(launchOptions.RuntimeRoot)
            ? launchOptions.RuntimeRoot
            : Directory.GetCurrentDirectory();

        using var startupLoggerFactory = LoggerFactory.Create(logging => logging.AddConsole());
        var startupPaths = StartupPathResolver.Resolve(
            builder.Configuration,
            runtimeBaseDirectory,
            runtimeCurrentDirectory);
        var startupMigration = DesktopWorkspaceMigrator.ImportIfNeeded(
            startupPaths.MigrationPlan,
            startupLoggerFactory.CreateLogger("QuillForge.Web.Hosting.DesktopWorkspaceMigrator"));

        var contentRoot = startupPaths.ContentRoot;
        var docsRoot = startupPaths.DocsRoot;
        var appConfig = await LoadAppConfigAsync(contentRoot, startupPaths.DefaultsPath, startupLoggerFactory);
        var endpointBinding = BackendHostingConfiguration.Resolve(builder.Configuration, launchOptions);
        var runtimeInfo = new BackendRuntimeInfo(
            launchOptions.DesktopMode,
            contentRoot,
            endpointBinding.BindMode,
            endpointBinding.Port,
            launchOptions.DesktopInstanceId,
            launchOptions.OpenBrowser,
            endpointBinding.Url);

        ConfigureServices(builder, startupPaths, appConfig, runtimeInfo, launchOptions, contentRoot, docsRoot);

        var app = builder.Build();

        app.UseCors();

        await BootstrapProvidersAsync(app);

        app.UseDefaultFiles();
        app.UseStaticFiles();

        app.MapHealthEndpoints();
        app.MapStatusEndpoints();
        app.MapSessionEndpoints();
        app.MapChatEndpoints();
        app.MapModeEndpoints();
        app.MapWriterEndpoints();
        app.MapLoreCanonizationEndpoints();
        app.MapProviderEndpoints();
        app.MapProviderFetchModelsEndpoint();
        app.MapAppSettingsEndpoints();
        app.MapForgeEndpoints();
        app.MapForgeManagementEndpoints();
        app.MapContentEndpoints(contentRoot);
        app.MapProfileEndpoints(contentRoot);
        app.MapGameTemplateEndpoints();
        app.MapPlotEndpoints();
        app.MapCharacterCardEndpoints(contentRoot);
        app.MapCouncilEndpoints();
        app.MapArtifactEndpoints();
        app.MapTtsEndpoints();
        app.MapResearchEndpoints(contentRoot);
        app.MapProbeEndpoints(contentRoot);
        app.MapDocsEndpoints();

        if (app.Environment.IsDevelopment())
        {
            app.MapDebugBridgeEndpoints();
        }

        app.MapFallbackToFile("index.html");

        app.Services.GetRequiredService<StartupReadinessState>().MarkReady();
        LogStartupConfiguration(app, runtimeInfo, startupPaths, startupMigration);

        return app;
    }

    private static async Task<AppConfig> LoadAppConfigAsync(
        string contentRoot,
        string defaultsPath,
        ILoggerFactory startupLoggerFactory)
    {
        var firstRunSetup = new FirstRunSetup(startupLoggerFactory.CreateLogger<FirstRunSetup>());
        firstRunSetup.EnsureContentDirectory(
            contentRoot,
            Directory.Exists(defaultsPath) ? defaultsPath : null);

        var configPath = Path.Combine(contentRoot, ContentPaths.ConfigFile);
        if (!File.Exists(configPath))
        {
            var configLoader = new ConfigurationLoader(startupLoggerFactory.CreateLogger<ConfigurationLoader>());
            configLoader.WriteDefaults(configPath);
        }

        var startupWriter = new Den.Persistence.AtomicFileWriter(
            startupLoggerFactory.CreateLogger<Den.Persistence.AtomicFileWriter>());
        var appConfigStore = new AppConfigStore(
            contentRoot,
            startupWriter,
            startupLoggerFactory.CreateLogger<AppConfigStore>());
        return await appConfigStore.LoadAsync();
    }

    private static void ConfigureServices(
        WebApplicationBuilder builder,
        StartupPaths startupPaths,
        AppConfig appConfig,
        BackendRuntimeInfo runtimeInfo,
        BackendLaunchOptions launchOptions,
        string contentRoot,
        string docsRoot)
    {
        builder.Services.AddSingleton(appConfig);
        builder.Services.AddSingleton(startupPaths);
        builder.Services.AddSingleton(runtimeInfo);
        builder.Services.AddSingleton(launchOptions);
        builder.Services.AddSingleton<StartupReadinessState>();

        builder.Services.AddSingleton<AtomicFileWriter>();
        builder.Services.AddSingleton<Den.Persistence.AtomicFileWriter>();
        builder.Services.AddSingleton<ILlmDebugLogger>(new LlmDebugLogger(Path.Combine(contentRoot, ContentPaths.Data)));

        builder.Services.AddStorageServices(contentRoot);
        builder.Services.AddDocsService(docsRoot);

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

        builder.Services.AddSingleton<ProviderFactory>();
        builder.Services.AddSingleton<ProviderRegistry>();
        builder.Services.AddSingleton<ITokenUsageTracker, InMemoryTokenUsageTracker>();

        builder.Services.AddSingleton(sp =>
        {
            var result = new GameModuleRegistryFactory().Create([new WerewolfModule()]);
            if (!result.ValidationResult.IsValid)
            {
                throw new InvalidOperationException(
                    $"Game module registry failed to build: {string.Join(", ", result.ValidationResult.Issues.Select(issue => issue.Message))}");
            }

            return result.Registry;
        });
        builder.Services.AddSingleton<GameSetupValidationService>();
        builder.Services.AddSingleton(sp => new RulesEngineService(sp.GetRequiredService<GameModuleRegistry>()));
        builder.Services.AddSingleton<IGameRuntimeService, GameRuntimeService>();
        builder.Services.AddSingleton<IGameTemplateProviderCatalog, GameTemplateProviderCatalog>();
        builder.Services.AddSingleton<IGameTemplateModuleValidator, GameTemplateModuleValidator>();
        builder.Services.AddSingleton<IGameTemplateService, GameTemplateService>();

        builder.Services.AddSingleton<DefaultCompletionService>();
        builder.Services.AddSingleton<ICompletionService>(sp =>
            new UsageTrackingCompletionService(
                sp.GetRequiredService<DefaultCompletionService>(),
                sp.GetRequiredService<ITokenUsageTracker>(),
                sp.GetRequiredService<ILogger<UsageTrackingCompletionService>>()));

        builder.Services.AddSingleton<ContinuationStrategy>();
        builder.Services.AddSingleton<ToolLoop>();
        builder.Services.AddSingleton<CanonPrerequisiteGuard>();
        builder.Services.AddSingleton<LibrarianAgent>();
        builder.Services.AddSingleton<ProseWriterAgent>(sp =>
        {
            var toolLoop = sp.GetRequiredService<ToolLoop>();
            var config = sp.GetRequiredService<AppConfig>();
            var queryLore = new QueryLoreHandler(
                sp.GetRequiredService<LibrarianAgent>(),
                sp.GetRequiredService<ILoreStore>(),
                sp.GetRequiredService<IContentFileService>(),
                sp.GetRequiredService<CanonPrerequisiteGuard>(),
                sp.GetRequiredService<ILogger<QueryLoreHandler>>());
            return new ProseWriterAgent(
                toolLoop,
                queryLore,
                sp.GetRequiredService<CanonPrerequisiteGuard>(),
                config,
                sp.GetRequiredService<ILogger<ProseWriterAgent>>(),
                sp.GetRequiredService<QueryContextHandler>());
        });
        builder.Services.AddSingleton<NarrativeDirectorAgent>(sp =>
            new NarrativeDirectorAgent(
                sp.GetRequiredService<ToolLoop>(),
                sp.GetRequiredService<QueryLoreHandler>(),
                sp.GetRequiredService<UpdateStoryStateHandler>(),
                sp.GetRequiredService<UpdateNarrativeStateHandler>(),
                sp.GetRequiredService<WriteProseHandler>(),
                sp.GetRequiredService<CanonPrerequisiteGuard>(),
                sp.GetRequiredService<INarrativeRulesStore>(),
                sp.GetRequiredService<AppConfig>(),
                sp.GetRequiredService<ILogger<NarrativeDirectorAgent>>(),
                sp.GetRequiredService<QueryContextHandler>()));

        builder.Services.AddSingleton<DelegatePool>(sp =>
        {
            var registry = sp.GetRequiredService<ProviderRegistry>();
            var logger = sp.GetRequiredService<ILogger<DelegatePool>>();
            return new DelegatePool(
                alias => registry.GetCompletionService(alias),
                registry.ResolveProviderAlias,
                logger);
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

        builder.Services.AddToolHandlers(appConfig);

        builder.Services.AddSingleton<ResearchAgent>(sp =>
        {
            var tools = new List<IToolHandler>
            {
                new WriteFileHandler(
                    sp.GetRequiredService<IContentFileService>(),
                    sp.GetRequiredService<ILogger<WriteFileHandler>>()),
                new ReadFileHandler(
                    sp.GetRequiredService<IContentFileService>(),
                    sp.GetRequiredService<ILogger<ReadFileHandler>>()),
                new ListFilesHandler(
                    sp.GetRequiredService<IContentFileService>(),
                    sp.GetRequiredService<ILogger<ListFilesHandler>>()),
            };
            var webSearch = new WebSearchHandler(
                sp.GetRequiredService<IWebSearchService>(),
                sp.GetRequiredService<ILogger<WebSearchHandler>>());
            tools.Add(new ThrottledToolHandler(webSearch, TimeSpan.FromSeconds(1.5)));

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

        builder.Services.AddSingleton<IMode, GuideMode>();
        builder.Services.AddSingleton<IMode, WriterMode>();
        builder.Services.AddSingleton<IMode, RoleplayMode>();
        builder.Services.AddSingleton<IMode, LoreBuilderMode>();
        builder.Services.AddSingleton<IMode, ForgeMode>();
        builder.Services.AddSingleton<IMode, CouncilMode>();
        builder.Services.AddSingleton<IMode, ResearchMode>();

        builder.Services.AddSingleton<ISessionMutationGate, InMemorySessionMutationGate>();
        builder.Services.AddSingleton<SessionRuntimeService>();
        builder.Services.AddSingleton<ISessionStateService>(sp =>
            sp.GetRequiredService<SessionRuntimeService>());
        builder.Services.AddSingleton<ISessionBootstrapService, SessionBootstrapService>();
        builder.Services.AddSingleton<ISessionLifecycleService, SessionLifecycleService>();
        builder.Services.AddSingleton<ISessionTranscriptService, SessionTranscriptService>();
        builder.Services.AddSingleton<ISessionLoreCanonizationService, SessionLoreCanonizationService>();
        builder.Services.AddSingleton<IInteractiveSessionContextService, InteractiveSessionContextService>();
        builder.Services.AddSingleton<IProfileConfigService, ProfileConfigService>();
        builder.Services.AddSingleton<ISessionProfileReadService, SessionProfileReadService>();
        builder.Services.AddSingleton<ICharacterCardCommandService, CharacterCardCommandService>();

        builder.Services.AddSingleton<OrchestratorAgent>();

        builder.Services.AddSingleton<IPipelineStage, PlanningStage>();
        builder.Services.AddSingleton<IPipelineStage, DesignStage>();
        builder.Services.AddSingleton<IPipelineStage, WritingStage>();
        builder.Services.AddSingleton<IPipelineStage, ReviewStage>();
        builder.Services.AddSingleton<IPipelineStage, AssemblyStage>();
        builder.Services.AddSingleton<ForgePipeline>();

        builder.Services.AddSingleton<IWebSearchService, ConfigBackedWebSearchService>();

        builder.Services.AddHttpClient();
        builder.Services.AddSingleton<AutoUpdateService>();
        builder.Services.AddHostedService(sp => sp.GetRequiredService<AutoUpdateService>());

        builder.Services.AddMediaProviders(contentRoot, appConfig);

        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(policy =>
            {
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
            });
        });

        builder.Services.ConfigureHttpJsonOptions(options =>
        {
            options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        });
    }

    private static async Task BootstrapProvidersAsync(WebApplication app)
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
                ApiKey = dto.ApiKey ?? string.Empty,
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
                    RepetitionPenalty = dto.Options.RepetitionPenalty,
                    MinP = dto.Options.MinP,
                    Seed = dto.Options.Seed,
                    Additional = dto.Options.Additional,
                } : null,
            });
        }

        app.Logger.LogInformation("Loaded {Count} persisted providers", loadedConfigs.Count);

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

                var dtos = registry.GetAllConfigs().Select(config => new ProviderConfigDto
                {
                    Alias = config.Alias,
                    Type = config.Type.ToString(),
                    ApiKey = config.ApiKey,
                    BaseUrl = config.BaseUrl,
                    DefaultModel = config.DefaultModel,
                }).ToList();
                await providerStore.SaveAsync(dtos);

                app.Logger.LogInformation("Bootstrapped default 'claude' provider from ANTHROPIC_API_KEY env var");
            }
        }
    }

    private static void LogStartupConfiguration(
        WebApplication app,
        BackendRuntimeInfo runtimeInfo,
        StartupPaths startupPaths,
        WorkspaceMigrationResult? startupMigration)
    {
        app.Logger.LogInformation(
            "Backend startup ready: desktopMode={DesktopMode}, bindMode={BindMode}, httpUrl={HttpUrl}, contentRoot={ContentRoot}, contentRootKind={ContentRootKind}, desktopInstanceId={DesktopInstanceId}, openBrowser={OpenBrowser}",
            runtimeInfo.DesktopMode,
            runtimeInfo.BindMode == BackendBindMode.Loopback ? "loopback" : "lan",
            runtimeInfo.HttpUrl,
            runtimeInfo.ContentRoot,
            startupPaths.ContentRootKind,
            runtimeInfo.DesktopInstanceId,
            runtimeInfo.OpenBrowser);

        if (startupMigration is not null)
        {
            app.Logger.LogInformation(
                "Desktop workspace import completed from {SourceContentRoot} to {TargetContentRoot} with {CopiedFileCount} copied files",
                startupMigration.SourceContentRoot,
                startupMigration.TargetContentRoot,
                startupMigration.CopiedFileCount);
        }
    }
}
