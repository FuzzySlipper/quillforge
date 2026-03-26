namespace QuillForge.Core.Models;

/// <summary>
/// Root application configuration. Loaded from config.yaml.
/// </summary>
public sealed record AppConfig
{
    public ModelsConfig Models { get; init; } = new();
    public PersonaConfig Persona { get; init; } = new();
    public LoreConfig Lore { get; init; } = new();
    public WritingStyleConfig WritingStyle { get; init; } = new();
    public LayoutConfig Layout { get; init; } = new();
    public RoleplayConfig Roleplay { get; init; } = new();
    public ForgeConfig Forge { get; init; } = new();
    public WebSearchConfig WebSearch { get; init; } = new();
    public EmailConfig Email { get; init; } = new();
}

public sealed record ModelsConfig
{
    public string Orchestrator { get; init; } = "default";
    public string ProseWriter { get; init; } = "default";
    public string Librarian { get; init; } = "default";
    public string ForgeWriter { get; init; } = "default";
    public string ForgePlanner { get; init; } = "default";
    public string ForgeReviewer { get; init; } = "default";
}

public sealed record PersonaConfig
{
    public string Active { get; init; } = "default";
    public int MaxTokens { get; init; } = 6000;
}

public sealed record LoreConfig
{
    public string Active { get; init; } = "default";
}

public sealed record WritingStyleConfig
{
    public string Active { get; init; } = "default";
}

public sealed record LayoutConfig
{
    public string Active { get; init; } = "default";
}

public sealed record RoleplayConfig
{
    public string? AiCharacter { get; init; }
    public string? UserCharacter { get; init; }
}

public sealed record ForgeConfig
{
    public double ReviewPassThreshold { get; init; } = 7.0;
    public int MaxRevisions { get; init; } = 3;
    public bool PauseAfterChapter1 { get; init; } = true;
    public int StageTimeoutMinutes { get; init; } = 120;
}

public sealed record WebSearchConfig
{
    public bool Enabled { get; init; }
    public string Provider { get; init; } = "searxng";
    public string? SearxngUrl { get; init; }
    public int MaxResults { get; init; } = 50;
}

public sealed record EmailConfig
{
    public string? ResendApiKey { get; init; }
    public string? DeveloperEmail { get; init; }
}
