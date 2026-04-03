namespace QuillForge.Core.Models;

/// <summary>
/// Root application configuration. Loaded from config.yaml.
/// </summary>
public sealed record AppConfig
{
    public ModelsConfig Models { get; set; } = new();
    public PersonaConfig Persona { get; set; } = new();
    public NarrativeRulesConfig NarrativeRules { get; set; } = new();
    public LoreConfig Lore { get; set; } = new();
    public WritingStyleConfig WritingStyle { get; set; } = new();
    public LayoutConfig Layout { get; set; } = new();
    public RoleplayConfig Roleplay { get; set; } = new();
    public ForgeConfig Forge { get; set; } = new();
    public WebSearchConfig WebSearch { get; set; } = new();
    public EmailConfig Email { get; set; } = new();
    public DiagnosticsConfig Diagnostics { get; set; } = new();
    public AgentsConfig Agents { get; set; } = new();
    public TimeoutsConfig Timeouts { get; set; } = new();
    public ImageGenConfig ImageGen { get; set; } = new();
    public TtsConfig Tts { get; set; } = new();
}

public sealed record ModelsConfig
{
    public string Orchestrator { get; set; } = "default";
    public string NarrativeDirector { get; set; } = "default";
    public string ProseWriter { get; set; } = "default";
    public string Librarian { get; set; } = "default";
    public string ForgeWriter { get; set; } = "default";
    public string ForgePlanner { get; set; } = "default";
    public string ForgeReviewer { get; set; } = "default";
    public string DelegateTechnical { get; set; } = "default";
    public string Artifact { get; set; } = "default";
    public string Research { get; set; } = "default";
}

public sealed record PersonaConfig
{
    public string Active { get; set; } = "default";
    public int MaxTokens { get; set; } = 6000;
}

public sealed record NarrativeRulesConfig
{
    public string Active { get; set; } = "default";
}

public sealed record LoreConfig
{
    public string Active { get; set; } = "default";
}

public sealed record WritingStyleConfig
{
    public string Active { get; set; } = "default";
}

public sealed record LayoutConfig
{
    public string Active { get; set; } = "default";
}

public sealed record RoleplayConfig
{
    public string? AiCharacter { get; set; }
    public string? UserCharacter { get; set; }
}

public sealed record ForgeConfig
{
    public double ReviewPassThreshold { get; set; } = 7.0;
    public int MaxRevisions { get; set; } = 3;
    public bool PauseAfterChapter1 { get; set; } = true;
    public int StageTimeoutMinutes { get; set; } = 120;
}

public sealed record WebSearchConfig
{
    public bool Enabled { get; set; }
    public string Provider { get; set; } = "searxng";
    public string? SearxngUrl { get; set; }
    public string? TavilyApiKey { get; set; }
    public string? BraveApiKey { get; set; }
    public string? GoogleApiKey { get; set; }
    public string? GoogleCxId { get; set; }
    public int MaxResults { get; set; } = 50;
}

public sealed record EmailConfig
{
    public string? ResendApiKey { get; set; }
    public string? DeveloperEmail { get; set; }
}

public sealed record DiagnosticsConfig
{
    public bool LivePanel { get; set; } = false;
}

public sealed record AgentsConfig
{
    public OrchestratorBudget Orchestrator { get; set; } = new();
    public NarrativeDirectorBudget NarrativeDirector { get; set; } = new();
    public LibrarianBudget Librarian { get; set; } = new();
    public ProseWriterBudget ProseWriter { get; set; } = new();
    public ForgePlannerBudget ForgePlanner { get; set; } = new();
    public ForgeWriterBudget ForgeWriter { get; set; } = new();
    public ForgeReviewerBudget ForgeReviewer { get; set; } = new();
    public DelegateTechnicalBudget DelegateTechnical { get; set; } = new();
    public CouncilBudget Council { get; set; } = new();
    public ArtifactBudget Artifact { get; set; } = new();
    public ResearchBudget Research { get; set; } = new();
}

public sealed record OrchestratorBudget
{
    public int MaxToolRounds { get; set; } = 15;
}

public sealed record NarrativeDirectorBudget
{
    public int MaxTokens { get; set; } = 4096;
    public int MaxToolRounds { get; set; } = 8;
}

public sealed record LibrarianBudget
{
    public int MaxTokens { get; set; } = 4096;
    public int MaxToolRounds { get; set; } = 1;
    public bool CacheSystemPrompt { get; set; } = true;
}

public sealed record ProseWriterBudget
{
    public int MaxTokens { get; set; } = 8192;
    public int MaxToolRounds { get; set; } = 10;
}

public sealed record ForgePlannerBudget
{
    public int MaxTokens { get; set; } = 8192;
    public int MaxToolRounds { get; set; } = 30;
}

public sealed record ForgeWriterBudget
{
    public int MaxTokens { get; set; } = 8192;
    public int MaxToolRounds { get; set; } = 15;
}

public sealed record ForgeReviewerBudget
{
    public int MaxTokens { get; set; } = 4096;
}

public sealed record DelegateTechnicalBudget
{
    public int MaxTokens { get; set; } = 2048;
}

public sealed record CouncilBudget
{
    public int MaxTokens { get; set; } = 1024;
    public double Temperature { get; set; } = 0.7;
}

public sealed record ArtifactBudget
{
    public int MaxTokens { get; set; } = 4096;
}

public sealed record ResearchBudget
{
    public int MaxTokens { get; set; } = 4096;
    public int MaxToolRounds { get; set; } = 15;
    public int MaxConcurrency { get; set; } = 4;
    public double Temperature { get; set; } = 0.4;
}

public sealed record TimeoutsConfig
{
    public int ToolExecutionSeconds { get; set; } = 120;
    public int ProviderHttpSeconds { get; set; } = 10;
    public int UpdateCheckHours { get; set; } = 6;
    public int UpdateStartupDelaySeconds { get; set; } = 60;
}

public sealed record ImageGenConfig
{
    public ComfyUiConfig ComfyUi { get; set; } = new();
    public OpenAiImageConfig OpenAi { get; set; } = new();
}

public sealed record ComfyUiConfig
{
    public int Width { get; set; } = 512;
    public int Height { get; set; } = 512;
    public int Steps { get; set; } = 20;
    public double CfgScale { get; set; } = 7.0;
    public string Sampler { get; set; } = "euler";
    public string Scheduler { get; set; } = "normal";
    public string Checkpoint { get; set; } = "sd_xl_base_1.0.safetensors";
    public int PollIntervalMs { get; set; } = 1000;
    public int MaxPollIterations { get; set; } = 120;
}

public sealed record OpenAiImageConfig
{
    public int Width { get; set; } = 1024;
    public int Height { get; set; } = 1024;
    public string Model { get; set; } = "dall-e-3";
    public string Quality { get; set; } = "standard";
}

public sealed record TtsConfig
{
    public ElevenLabsConfig ElevenLabs { get; set; } = new();
    public OpenAiTtsConfig OpenAi { get; set; } = new();
}

public sealed record ElevenLabsConfig
{
    public string VoiceId { get; set; } = "21m00Tcm4TlvDq8ikWAM";
    public string ModelId { get; set; } = "eleven_monolingual_v1";
    public double Stability { get; set; } = 0.5;
    public double SimilarityBoost { get; set; } = 0.75;
}

public sealed record OpenAiTtsConfig
{
    public string Model { get; set; } = "tts-1";
    public string Voice { get; set; } = "alloy";
    public double Speed { get; set; } = 1.0;
}
