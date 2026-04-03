using QuillForge.Core;
using QuillForge.Core.Agents.Tools;
using QuillForge.Core.Diagnostics;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Providers.ImageGen;
using QuillForge.Providers.Tts;
using QuillForge.Storage.Configuration;
using QuillForge.Storage.FileSystem;
using QuillForge.Storage.Utilities;

namespace QuillForge.Web;

/// <summary>
/// Extension methods that group service registrations by domain.
/// All registrations remain explicit — no reflection or scanning.
/// </summary>
public static class ServiceRegistration
{
    public static void AddStorageServices(this IServiceCollection services, string contentRoot)
    {
        services.AddSingleton<IContentFileService>(sp =>
            new FileSystemContentService(contentRoot,
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemContentService>>()));

        services.AddSingleton<ILoreStore>(sp =>
            new FileSystemLoreStore(Path.Combine(contentRoot, ContentPaths.Lore),
                sp.GetRequiredService<ILogger<FileSystemLoreStore>>()));

        services.AddSingleton<IStoryStore>(sp =>
            new FileSystemStoryStore(Path.Combine(contentRoot, ContentPaths.Story),
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemStoryStore>>()));

        services.AddSingleton<IWritingStyleStore>(sp =>
            new FileSystemWritingStyleStore(Path.Combine(contentRoot, ContentPaths.WritingStyles),
                sp.GetRequiredService<ILogger<FileSystemWritingStyleStore>>()));

        services.AddSingleton<INarrativeRulesStore>(sp =>
            new FileSystemNarrativeRulesStore(Path.Combine(contentRoot, ContentPaths.NarrativeRules),
                sp.GetRequiredService<ILogger<FileSystemNarrativeRulesStore>>()));

        services.AddSingleton<IPlotStore>(sp =>
            new FileSystemPlotStore(Path.Combine(contentRoot, ContentPaths.Plots),
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemPlotStore>>()));

        services.AddSingleton<IArtifactService>(sp =>
            new FileSystemArtifactService(Path.Combine(contentRoot, ContentPaths.Artifacts),
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemArtifactService>>()));

        services.AddSingleton<FileSystemConductorStore>(sp =>
            new FileSystemConductorStore(
                Path.Combine(contentRoot, ContentPaths.Conductor),
                Path.Combine(contentRoot, ContentPaths.Persona),
                sp.GetRequiredService<ILogger<FileSystemConductorStore>>()));
        services.AddSingleton<IConductorStore>(sp => sp.GetRequiredService<FileSystemConductorStore>());
        services.AddSingleton<IPersonaStore>(sp => sp.GetRequiredService<FileSystemConductorStore>());

        services.AddSingleton<ICharacterCardStore>(sp =>
            new FileSystemCharacterCardStore(
                Path.Combine(contentRoot, ContentPaths.CharacterCards),
                Path.Combine(contentRoot, ContentPaths.CharacterCards),
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemCharacterCardStore>>()));

        services.AddSingleton<ISessionStore>(sp =>
            new FileSystemSessionStore(Path.Combine(contentRoot, ContentPaths.DataSessions),
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemSessionStore>>(),
                sp.GetRequiredService<ILoggerFactory>()));

        services.AddSingleton(sp =>
            new RuntimeStateStore(contentRoot,
                sp.GetRequiredService<Den.Persistence.AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<RuntimeStateStore>>()));

        services.AddSingleton<IAppConfigStore>(sp =>
            new AppConfigStore(contentRoot,
                sp.GetRequiredService<Den.Persistence.AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<AppConfigStore>>()));

        services.AddSingleton<IProfileConfigStore>(sp =>
            new FileSystemProfileConfigStore(contentRoot,
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemProfileConfigStore>>()));

        services.AddSingleton<ISessionRuntimeStore>(sp =>
            new FileSystemSessionRuntimeStore(contentRoot,
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemSessionRuntimeStore>>()));

        services.AddSingleton<IStoryStateService>(sp =>
            new FileSystemStoryStateService(
                Path.Combine(contentRoot, ContentPaths.Story),
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemStoryStateService>>()));
    }

    public static void AddToolHandlers(this IServiceCollection services, AppConfig appConfig)
    {
        services.AddSingleton<QueryLoreHandler>(sp => new QueryLoreHandler(
            sp.GetRequiredService<QuillForge.Core.Agents.LibrarianAgent>(),
            sp.GetRequiredService<ILoreStore>(),
            sp.GetRequiredService<IContentFileService>(),
            sp.GetRequiredService<ILogger<QueryLoreHandler>>()));
        services.AddSingleton<IToolHandler>(sp => sp.GetRequiredService<QueryLoreHandler>());
        services.AddSingleton<WriteProseHandler>();
        services.AddSingleton<IToolHandler>(sp => sp.GetRequiredService<WriteProseHandler>());
        services.AddSingleton<IToolHandler, RollDiceHandler>();
        services.AddSingleton<IToolHandler, ReadFileHandler>();
        services.AddSingleton<IToolHandler, WriteFileHandler>();
        services.AddSingleton<IToolHandler, ListFilesHandler>();
        services.AddSingleton<IToolHandler, SearchFilesHandler>();
        services.AddSingleton<IToolHandler, DelegateTechnicalHandler>();
        services.AddSingleton<IToolHandler, RequestCodeChangeHandler>();
        services.AddSingleton<IToolHandler>(sp => sp.GetRequiredService<QuillForge.Core.Agents.Tools.RunCouncilHandler>());
        if (appConfig.WebSearch.Enabled)
        {
            services.AddSingleton<IToolHandler, WebSearchHandler>();
        }
        // Story state handlers resolve the active project/path from the prepared
        // interactive session context passed through AgentContext.
        services.AddSingleton<GetStoryStateHandler>();
        services.AddSingleton<IToolHandler>(sp => sp.GetRequiredService<GetStoryStateHandler>());
        services.AddSingleton<UpdateStoryStateHandler>();
        services.AddSingleton<IToolHandler>(sp => sp.GetRequiredService<UpdateStoryStateHandler>());
        services.AddSingleton<UpdateNarrativeStateHandler>();
        services.AddSingleton<IToolHandler>(sp => sp.GetRequiredService<UpdateNarrativeStateHandler>());
        services.AddSingleton<IToolHandler, DirectSceneHandler>();
        services.AddSingleton<IToolHandler>(sp =>
        {
            var imageGen = sp.GetService<IImageGenerator>();
            return new GenerateImageHandler(
                imageGen ?? new FallbackImageGenerator([],
                    sp.GetRequiredService<ILogger<FallbackImageGenerator>>()),
                sp.GetRequiredService<ILogger<GenerateImageHandler>>());
        });
        if (!string.IsNullOrEmpty(appConfig.Email.ResendApiKey) && !string.IsNullOrEmpty(appConfig.Email.DeveloperEmail))
        {
            services.AddSingleton<IEmailService>(sp =>
                new QuillForge.Providers.Email.ResendEmailService(
                    new HttpClient(),
                    appConfig.Email.ResendApiKey,
                    appConfig.Email.DeveloperEmail,
                    sp.GetRequiredService<ILogger<QuillForge.Providers.Email.ResendEmailService>>()));
            services.AddSingleton<IToolHandler, EmailDeveloperHandler>();
        }
    }

    public static void AddMediaProviders(this IServiceCollection services, string contentRoot, AppConfig appConfig)
    {
        // Image generation providers
        {
            var imageProviders = new List<IImageGenerator>();
            var imageOutputDir = Path.Combine(contentRoot, ContentPaths.GeneratedImages);

            var comfyUrl = Environment.GetEnvironmentVariable("COMFYUI_URL");
            if (!string.IsNullOrEmpty(comfyUrl))
            {
                imageProviders.Add(new ComfyUiImageGenerator(new HttpClient(), comfyUrl, imageOutputDir, appConfig.ImageGen.ComfyUi,
                    LoggerFactory.Create(b => b.AddConsole()).CreateLogger<ComfyUiImageGenerator>()));
            }

            var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrEmpty(openAiKey))
            {
                imageProviders.Add(new OpenAiImageGenerator(new HttpClient(), openAiKey, imageOutputDir, appConfig.ImageGen.OpenAi,
                    LoggerFactory.Create(b => b.AddConsole()).CreateLogger<OpenAiImageGenerator>()));
            }

            if (imageProviders.Count > 0)
            {
                services.AddSingleton<IImageGenerator>(sp =>
                    new FallbackImageGenerator(imageProviders,
                        sp.GetRequiredService<ILogger<FallbackImageGenerator>>()));
            }
        }

        // TTS providers
        {
            var ttsProviders = new List<ITtsGenerator>();
            var ttsOutputDir = Path.Combine(contentRoot, ContentPaths.GeneratedAudio);

            var openAiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrEmpty(openAiKey))
            {
                ttsProviders.Add(new OpenAiTtsGenerator(new HttpClient(), openAiKey, ttsOutputDir, appConfig.Tts.OpenAi,
                    LoggerFactory.Create(b => b.AddConsole()).CreateLogger<OpenAiTtsGenerator>()));
            }

            var elevenLabsKey = Environment.GetEnvironmentVariable("ELEVENLABS_API_KEY");
            if (!string.IsNullOrEmpty(elevenLabsKey))
            {
                ttsProviders.Add(new ElevenLabsTtsGenerator(new HttpClient(), elevenLabsKey, ttsOutputDir, appConfig.Tts.ElevenLabs,
                    LoggerFactory.Create(b => b.AddConsole()).CreateLogger<ElevenLabsTtsGenerator>()));
            }

            if (ttsProviders.Count > 0)
            {
                services.AddSingleton<ITtsGenerator>(sp =>
                    new FallbackTtsGenerator(ttsProviders,
                        sp.GetRequiredService<ILogger<FallbackTtsGenerator>>()));
            }
        }
    }
}
