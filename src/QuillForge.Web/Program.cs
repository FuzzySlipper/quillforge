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
var contentRoot = builder.Configuration.GetValue<string>("ContentRoot")
    ?? Path.Combine(Directory.GetCurrentDirectory(), "build");

var defaultsPath = Path.Combine(AppContext.BaseDirectory, "..", "dev", "defaults");
if (!Directory.Exists(defaultsPath))
{
    // Try relative to current directory (for development)
    defaultsPath = Path.Combine(Directory.GetCurrentDirectory(), "dev", "defaults");
}

var firstRunSetup = new FirstRunSetup(
    LoggerFactory.Create(b => b.AddConsole()).CreateLogger<FirstRunSetup>());
firstRunSetup.EnsureContentDirectory(contentRoot,
    Directory.Exists(defaultsPath) ? defaultsPath : null);

// --- Load configuration ---
var configLoader = new ConfigurationLoader(
    LoggerFactory.Create(b => b.AddConsole()).CreateLogger<ConfigurationLoader>());
var appConfig = configLoader.Load(Path.Combine(contentRoot, "config.yaml"));
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

// --- Provider registry ---
builder.Services.AddSingleton<ProviderFactory>();
builder.Services.AddSingleton<ProviderRegistry>();

// --- Core agents ---
builder.Services.AddSingleton<ContinuationStrategy>();
builder.Services.AddSingleton<ToolLoop>();
builder.Services.AddSingleton<LibrarianAgent>();
builder.Services.AddSingleton<ProseWriterAgent>(sp =>
{
    var toolLoop = sp.GetRequiredService<ToolLoop>();
    // QueryLoreHandler needs to be created with runtime config; placeholder for now
    var queryLore = new QueryLoreHandler(
        sp.GetRequiredService<LibrarianAgent>(),
        "default",
        sp.GetRequiredService<ILogger<QueryLoreHandler>>());
    return new ProseWriterAgent(toolLoop, queryLore,
        sp.GetRequiredService<IWritingStyleStore>(),
        sp.GetRequiredService<ILogger<ProseWriterAgent>>());
});

builder.Services.AddSingleton<ForgePlannerAgent>();
builder.Services.AddSingleton<ForgeWriterAgent>();
builder.Services.AddSingleton<ForgeReviewerAgent>(sp =>
    new ForgeReviewerAgent(
        sp.GetRequiredService<ProviderRegistry>().GetCompletionService("default"),
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
builder.Services.AddHostedService<QuillForge.Web.Services.AutoUpdateService>();

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

// --- SPA fallback ---
app.MapFallbackToFile("index.html");

app.Run();
