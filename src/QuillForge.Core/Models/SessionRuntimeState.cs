namespace QuillForge.Core.Models;

/// <summary>
/// Top-level per-session runtime aggregate. Owns all mutable state for a single
/// user session. Loaded at the start of a request, mutated during processing,
/// and persisted at the end.
///
/// Field ownership rules:
///   - Each field belongs to exactly one sub-state type.
///   - Services receive the narrowest sub-state they need, not the whole aggregate.
///   - New fields go into the sub-state that owns the concern, never top-level.
///   - If a new concern doesn't fit an existing sub-state, create a new one.
/// </summary>
public sealed class SessionRuntimeState
{
    /// <summary>
    /// The session this state belongs to. Null for the global/default state
    /// (legacy compatibility until all callers pass explicit session IDs).
    /// </summary>
    public Guid? SessionId { get; set; }

    /// <summary>Mode selection and project/file/character context.</summary>
    public ModeSelectionState Mode { get; set; } = new();

    /// <summary>Active profile selections (persona, lore, writing style).</summary>
    public ProfileState Profile { get; set; } = new();

    /// <summary>Writer-mode-specific runtime state (pending content, review workflow).</summary>
    public WriterRuntimeState Writer { get; set; } = new();

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
    public string ActiveModeName { get; set; } = "general";
    public string? ProjectName { get; set; }
    public string? CurrentFile { get; set; }
    public string? Character { get; set; }
}

/// <summary>
/// Active profile selections. Determines which persona, lore set, and writing style
/// are used when building prompts and invoking tools like query_lore and write_prose.
///
/// Currently mirrors AppConfig profile values. When session-scoped profiles land,
/// these override the global config for this session.
/// </summary>
public sealed class ProfileState
{
    /// <summary>Active persona file name. Null means "use global config default".</summary>
    public string? ActivePersona { get; set; }

    /// <summary>Active lore set name. Null means "use global config default".</summary>
    public string? ActiveLoreSet { get; set; }

    /// <summary>Active writing style name. Null means "use global config default".</summary>
    public string? ActiveWritingStyle { get; set; }
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

public enum WriterState
{
    Idle,
    PendingReview,
}
