namespace QuillForge.Core;

/// <summary>
/// Single source of truth for all content directory names and file paths
/// relative to the content root (build/). Every store, endpoint, and setup
/// class should reference these constants instead of bare strings.
/// </summary>
public static class ContentPaths
{
    // --- Top-level content directories ---
    public const string Lore = "lore";
    public const string LoreDefault = "lore/default";
    public const string Persona = "persona";
    public const string WritingStyles = "writing-styles";
    public const string Story = "story";
    public const string Writing = "writing";
    public const string Chats = "chats";
    public const string Forge = "forge";
    public const string ForgePrompts = "forge-prompts";
    public const string Council = "council";
    public const string Layouts = "layouts";
    public const string CharacterCards = "character-cards";
    public const string Backgrounds = "backgrounds";
    public const string GeneratedImages = "generated-images";
    public const string GeneratedAudio = "generated-audio";
    public const string Artifacts = "artifacts";
    public const string Research = "research";

    // --- Data subdirectories ---
    public const string Data = "data";
    public const string DataSessions = "data/sessions";
    public const string DataSessionState = "data/session-state";
    public const string DataLlmDebug = "data/llm-debug";

    // --- Well-known files ---
    public const string ConfigFile = "config.yaml";
    public const string ProvidersFile = "data/providers.json";
    public const string RuntimeStateFile = "data/runtime-state.json";

    /// <summary>
    /// All directories that should exist in a content root.
    /// Used by FirstRunSetup to create the initial structure.
    /// </summary>
    public static readonly string[] AllDirectories =
    [
        LoreDefault,
        Persona,
        WritingStyles,
        Story,
        Writing,
        Chats,
        Forge,
        ForgePrompts,
        Council,
        Layouts,
        CharacterCards,
        Backgrounds,
        GeneratedImages,
        GeneratedAudio,
        Artifacts,
        Research,
        DataSessions,
        DataSessionState,
        DataLlmDebug,
    ];
}
