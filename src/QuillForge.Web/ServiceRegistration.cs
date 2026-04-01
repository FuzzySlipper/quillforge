using QuillForge.Core.Agents.Tools;
using QuillForge.Core.Diagnostics;
using QuillForge.Core.Models;
using QuillForge.Core.Services;
using QuillForge.Providers.ImageGen;
using QuillForge.Providers.Tts;
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
            new FileSystemLoreStore(Path.Combine(contentRoot, "lore"),
                sp.GetRequiredService<ILogger<FileSystemLoreStore>>()));

        services.AddSingleton<IStoryStore>(sp =>
            new FileSystemStoryStore(Path.Combine(contentRoot, "story"),
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemStoryStore>>()));

        services.AddSingleton<IWritingStyleStore>(sp =>
            new FileSystemWritingStyleStore(Path.Combine(contentRoot, "writing-styles"),
                sp.GetRequiredService<ILogger<FileSystemWritingStyleStore>>()));

        services.AddSingleton<IArtifactService>(sp =>
            new FileSystemArtifactService(Path.Combine(contentRoot, "artifacts"),
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemArtifactService>>()));

        services.AddSingleton<IPersonaStore>(sp =>
            new FileSystemPersonaStore(Path.Combine(contentRoot, "persona"),
                sp.GetRequiredService<ILogger<FileSystemPersonaStore>>()));

        services.AddSingleton<ICharacterCardStore>(sp =>
            new FileSystemCharacterCardStore(
                Path.Combine(contentRoot, "character-cards"),
                Path.Combine(contentRoot, "character-cards"),
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemCharacterCardStore>>()));

        services.AddSingleton<ISessionStore>(sp =>
            new FileSystemSessionStore(Path.Combine(contentRoot, "data", "sessions"),
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemSessionStore>>(),
                sp.GetRequiredService<ILoggerFactory>()));

        services.AddSingleton(sp =>
            new RuntimeStateStore(contentRoot,
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<RuntimeStateStore>>()));

        services.AddSingleton<ISessionRuntimeStore>(sp =>
            new FileSystemSessionRuntimeStore(contentRoot,
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemSessionRuntimeStore>>()));

        services.AddSingleton<IStoryStateService>(sp =>
            new FileSystemStoryStateService(
                Path.Combine(contentRoot, "story"),
                sp.GetRequiredService<AtomicFileWriter>(),
                sp.GetRequiredService<ILogger<FileSystemStoryStateService>>()));
    }

    public static void AddToolHandlers(this IServiceCollection services, AppConfig appConfig)
    {
        services.AddSingleton<IToolHandler>(sp => new QueryLoreHandler(
            sp.GetRequiredService<QuillForge.Core.Agents.LibrarianAgent>(),
            sp.GetRequiredService<IContentFileService>(),
            sp.GetRequiredService<ILogger<QueryLoreHandler>>()));
        services.AddSingleton<IToolHandler, WriteProseHandler>();
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
        // Story state handlers resolve the active project from session context at call time
        // via ISessionRuntimeStore + AgentContext.SessionId passed to HandleAsync.
        services.AddSingleton<IToolHandler, GetStoryStateHandler>();
        services.AddSingleton<IToolHandler, UpdateStoryStateHandler>();
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
            var imageOutputDir = Path.Combine(contentRoot, "generated-images");

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
            var ttsOutputDir = Path.Combine(contentRoot, "generated-audio");

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
