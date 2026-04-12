using System.Text.Json.Serialization;

namespace QuillForge.Core.Models;

public static class ModeExtensions
{
    /// <summary>Lowercase wire string for serialization and logging.</summary>
    public static string ToWireString(this Mode mode) => mode switch
    {
        Mode.Guide => "guide",
        Mode.Writer => "writer",
        Mode.Roleplay => "roleplay",
        Mode.Forge => "forge",
        Mode.Council => "council",
        Mode.Research => "research",
        _ => "guide",
    };

    /// <summary>Parse a string to Mode enum. Case-insensitive. Returns null on failure.</summary>
    public static Mode? TryParseMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        if (string.Equals(value, "general", StringComparison.OrdinalIgnoreCase))
        {
            return Mode.Guide;
        }

        if (string.Equals(value, "guide", StringComparison.OrdinalIgnoreCase))
        {
            return Mode.Guide;
        }

        return Enum.TryParse<Mode>(value, ignoreCase: true, out var mode) ? mode : null;
    }
}

/// <summary>
/// Top-level per-session state aggregate. Owns all mutable state for a single
/// user session. Loaded at the start of a request, mutated during processing,
/// and persisted at the end.
///
/// Field ownership rules:
///   - Each field belongs to exactly one sub-state type.
///   - Services receive the narrowest sub-state they need, not the whole aggregate.
///   - New fields go into the sub-state that owns the concern, never top-level.
///   - If a new concern doesn't fit an existing sub-state, create a new one.
/// </summary>
public class SessionState
{
    /// <summary>
    /// The session this state belongs to. Null for the global/default state
    /// (legacy compatibility until all callers pass explicit session IDs).
    /// </summary>
    public Guid? SessionId { get; set; }

    /// <summary>Mode selection and project/file/character context.</summary>
    public ModeSelectionState Mode { get; set; } = new();

    /// <summary>Active profile selections (narrative rules, lore, writing style, librarian prompt).</summary>
    public ProfileState Profile { get; set; } = new();

    /// <summary>Roleplay character selections for this live session.</summary>
    public RoleplayRuntimeState Roleplay { get; set; } = new();

    /// <summary>Writer-mode-specific runtime state (pending content, review workflow).</summary>
    public WriterRuntimeState Writer { get; set; } = new();

    /// <summary>Narrative-director session state (notes, active plot selection).</summary>
    public NarrativeRuntimeState Narrative { get; set; } = new();

    /// <summary>
    /// Timestamp of last mutation. Used to detect stale state on concurrent access.
    /// </summary>
    public DateTimeOffset LastModified { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Which mode is active and what project/file/character context it uses.
/// Mutated by mode-switch endpoints. Read by the orchestrator to resolve the IMode
/// and build ModeContext.
/// </summary>
public sealed class ModeSelectionState
{
    /// <summary>
    /// The active mode for this session. Persisted as a lowercase wire string
    /// under the JSON key "activeModeName". Legacy persisted "general" values
    /// still deserialize as the guide mode for backwards compatibility.
    /// </summary>
    [JsonPropertyName("activeModeName")]
    [JsonConverter(typeof(ModeJsonConverter))]
    public Mode ActiveMode { get; set; } = Mode.Guide;

    public string? ProjectName { get; set; }
    public string? CurrentFile { get; set; }
    public string? Character { get; set; }
}

/// <summary>
/// Active profile selections. Determines which narrative rules, lore set,
/// writing style, and librarian prompt are used when building prompts and invoking tools like
/// query_lore, direct_scene, and write_prose.
///
/// ProfileId identifies the durable base profile for the session. The Active*
/// values are sparse session overrides layered on top of that profile.
/// </summary>
public sealed class ProfileState
{
    /// <summary>Durable profile backing this session. Null means "use the default profile".</summary>
    public string? ProfileId { get; set; }

    /// <summary>
    /// Legacy persisted JSON alias from conductor-era session files. Old
    /// session state may still contain `activeConductor`, but live runtime
    /// state no longer materializes or persists a conductor override.
    /// </summary>
    [JsonPropertyName("activeConductor")]
    public string? IgnoredLegacyActiveConductor
    {
        get => null;
        set { }
    }

    /// <summary>
    /// Older session files may also contain `activePersona`; it is intentionally
    /// ignored during deserialization so new runtime state does not keep
    /// conductor-era routing concepts alive.
    /// </summary>
    [JsonPropertyName("activePersona")]
    public string? IgnoredLegacyActivePersona
    {
        get => null;
        set { }
    }

    /// <summary>Active lore set name override. Null means "use the session profile default".</summary>
    public string? ActiveLoreSet { get; set; }

    /// <summary>Active narrative rules name override. Null means "use the session profile default".</summary>
    public string? ActiveNarrativeRules { get; set; }

    /// <summary>Active writing style name override. Null means "use the session profile default".</summary>
    public string? ActiveWritingStyle { get; set; }

    /// <summary>Active librarian prompt name override. Null means "use the session profile default".</summary>
    public string? ActiveLibrarianPrompt { get; set; }
}

/// <summary>
/// Session-owned roleplay selections. When the explicit flag is false, the active
/// value is inherited from the session profile default. When true, the stored
/// value is the active session selection and may itself be null to mean
/// "explicitly no character".
/// </summary>
public sealed class RoleplayRuntimeState
{
    public bool HasExplicitAiCharacterSelection { get; set; }
    public string? ActiveAiCharacter { get; set; }
    public bool HasExplicitUserCharacterSelection { get; set; }
    public string? ActiveUserCharacter { get; set; }
}

/// <summary>
/// Writer-mode-specific mutable state. Tracks the accept/reject workflow
/// for generated prose. Persisted so pending content survives app restart.
/// </summary>
public sealed class WriterRuntimeState
{
    public string? PendingContent { get; set; }
    public WriterState State { get; set; } = WriterState.Idle;
}

/// <summary>
/// Narrative-director mutable state persisted per session. Stores running
/// scene-direction notes and the currently loaded plot file, if any.
/// </summary>
public sealed class NarrativeRuntimeState
{
    public string? DirectorNotes { get; set; }
    public string? ActivePlotFile { get; set; }
    public PlotProgressState PlotProgress { get; set; } = new();
}

public sealed class PlotProgressState
{
    public string? CurrentBeat { get; set; }
    public List<string> CompletedBeats { get; set; } = [];
    public List<string> Deviations { get; set; } = [];
}

public enum WriterState
{
    Idle,
    PendingReview,
}
