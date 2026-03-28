namespace QuillForge.Core.Models;

/// <summary>
/// Root application configuration. Loaded from config.yaml.
/// </summary>
public sealed record AppConfig
{
    public ModelsConfig Models { get; set; } = new();
    public PersonaConfig Persona { get; set; } = new();
    public LoreConfig Lore { get; set; } = new();
    public WritingStyleConfig WritingStyle { get; set; } = new();
    public LayoutConfig Layout { get; set; } = new();
    public RoleplayConfig Roleplay { get; set; } = new();
    public ForgeConfig Forge { get; set; } = new();
    public WebSearchConfig WebSearch { get; set; } = new();
    public EmailConfig Email { get; set; } = new();
    public DiagnosticsConfig Diagnostics { get; set; } = new();
}

public sealed record ModelsConfig
{
    public string Orchestrator { get; set; } = "default";
    public string ProseWriter { get; set; } = "default";
    public string Librarian { get; set; } = "default";
    public string ForgeWriter { get; set; } = "default";
    public string ForgePlanner { get; set; } = "default";
    public string ForgeReviewer { get; set; } = "default";
}

public sealed record PersonaConfig
{
    public string Active { get; set; } = "default";
    public int MaxTokens { get; set; } = 6000;
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
