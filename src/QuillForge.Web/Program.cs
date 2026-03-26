using QuillForge.Core.Agents;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Agents.Tools;
using QuillForge.Core.Models;
using QuillForge.Core.Pipeline;
using QuillForge.Core.Services;
using QuillForge.Providers.Adapters;
using QuillForge.Providers.Registry;
using QuillForge.Storage.Configuration;
using QuillForge.Storage.FileSystem;
using QuillForge.Storage.Utilities;
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
var configPath = Path.Combine(contentRoot, "config.yaml");
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

// --- Storage services (explicit registration, no scanning) ---
builder.Services.AddSingleton<IContentFileService>(sp =>
    new FileSystemContentService(contentRoot,
        sp.GetRequiredService<AtomicFileWriter>(),
        sp.GetRequiredService<ILogger<FileSystemContentService>>()));

builder.Services.AddSingleton<ILoreStore>(sp =>
    new FileSystemLoreStore(Path.Combine(contentRoot, "lore"),
        sp.GetRequiredService<ILogger<FileSystemLoreStore>>()));

builder.Services.AddSingleton<IStoryStore>(sp =>
    new FileSystemStoryStore(Path.Combine(contentRoot, "story"),
        sp.GetRequiredService<AtomicFileWriter>(),
        sp.GetRequiredService<ILogger<FileSystemStoryStore>>()));

builder.Services.AddSingleton<IWritingStyleStore>(sp =>
    new FileSystemWritingStyleStore(Path.Combine(contentRoot, "writing-styles"),
        sp.GetRequiredService<ILogger<FileSystemWritingStyleStore>>()));

builder.Services.AddSingleton<IPersonaStore>(sp =>
    new FileSystemPersonaStore(Path.Combine(contentRoot, "persona"),
        sp.GetRequiredService<ILogger<FileSystemPersonaStore>>()));

builder.Services.AddSingleton<ISessionStore>(sp =>
    new FileSystemSessionStore(Path.Combine(contentRoot, "data", "sessions"),
        sp.GetRequiredService<AtomicFileWriter>(),
        sp.GetRequiredService<ILogger<FileSystemSessionStore>>(),
        sp.GetRequiredService<ILoggerFactory>()));

builder.Services.AddSingleton(sp =>
    new RuntimeStateStore(contentRoot,
        sp.GetRequiredService<AtomicFileWriter>(),
        sp.GetRequiredService<ILogger<RuntimeStateStore>>()));

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
    var queryLore = new QueryLoreHandler(
        sp.GetRequiredService<LibrarianAgent>(),
        config.Lore.Active,
        sp.GetRequiredService<ILogger<QueryLoreHandler>>());
    return new ProseWriterAgent(toolLoop, queryLore,
        sp.GetRequiredService<IWritingStyleStore>(),
        sp.GetRequiredService<ILogger<ProseWriterAgent>>());
});

builder.Services.AddSingleton<ForgePlannerAgent>();
builder.Services.AddSingleton<ForgeWriterAgent>();
builder.Services.AddSingleton<ForgeReviewerAgent>(sp =>
    new ForgeReviewerAgent(
        sp.GetRequiredService<ICompletionService>(),
        sp.GetRequiredService<ILogger<ForgeReviewerAgent>>()));

// --- Modes (explicit, no scanning) ---
builder.Services.AddSingleton<IMode, GeneralMode>();
builder.Services.AddSingleton<IMode>(sp => new WriterMode(sp.GetRequiredService<ILogger<WriterMode>>()));
builder.Services.AddSingleton<IMode, RoleplayMode>();
builder.Services.AddSingleton<IMode, ForgeMode>();
builder.Services.AddSingleton<IMode, CouncilMode>();

// --- Orchestrator ---
builder.Services.AddSingleton<OrchestratorAgent>();

// --- Pipeline stages ---
builder.Services.AddSingleton<IPipelineStage, PlanningStage>();
builder.Services.AddSingleton<IPipelineStage, DesignStage>();
builder.Services.AddSingleton<IPipelineStage, WritingStage>();
builder.Services.AddSingleton<IPipelineStage, ReviewStage>();
builder.Services.AddSingleton<IPipelineStage, AssemblyStage>();
builder.Services.AddSingleton<ForgePipeline>();

// --- Auto-update checker ---
builder.Services.AddHttpClient();
builder.Services.AddSingleton<QuillForge.Web.Services.AutoUpdateService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<QuillForge.Web.Services.AutoUpdateService>());

// --- CORS for local development ---
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors();

// --- Restore last mode from runtime state ---
var runtimeState = app.Services.GetRequiredService<RuntimeStateStore>().Load();
if (!string.IsNullOrEmpty(runtimeState.LastMode))
{
    try
    {
        var orchestrator = app.Services.GetRequiredService<OrchestratorAgent>();
        orchestrator.SetMode(runtimeState.LastMode, runtimeState.LastProject, runtimeState.LastFile);
        app.Logger.LogInformation("Restored last mode: {Mode}", runtimeState.LastMode);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Failed to restore last mode '{Mode}'", runtimeState.LastMode);
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
app.MapForgeEndpoints();
app.MapContentEndpoints(contentRoot);
app.MapProfileEndpoints(contentRoot);

// --- SPA fallback ---
app.MapFallbackToFile("index.html");

app.Run();
