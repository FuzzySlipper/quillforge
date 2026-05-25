using System.Text.Json.Serialization;

namespace QuillForge.Core.Models;

// ──────────────────────────────────────────────
// Enums for structured roleplay knowledge protocol
// ──────────────────────────────────────────────

/// <summary>
/// Scope classifying the knowledge domain of a roleplay lore fact.
/// Mirrors the Den protocol: character-level, world-level, or meta.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RoleplayKnowledgeScope
{
    /// <summary>Character-specific knowledge (inline lore, backstory).</summary>
    Character,
    /// <summary>Shared world/body-tech/faction knowledge (context).</summary>
    World,
    /// <summary>Meta/system knowledge about the roleplay itself.</summary>
    Meta,
}

/// <summary>
/// How a piece of lore applies to the currently active subject/character.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ActiveSubjectApplicability
{
    /// <summary>Directly about / owned by the active subject.</summary>
    ActiveCharacter,
    /// <summary>About a character or entity different from the active subject.</summary>
    OffCharacter,
    /// <summary>Shared world-level background knowledge.</summary>
    SharedWorld,
    /// <summary>Cannot be determined from available information.</summary>
    Unknown,
}

/// <summary>
/// How a piece of knowledge may be used in narrative generation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AllowedUse
{
    /// <summary>May be incorporated as inline facts about the active subject.</summary>
    Inline,
    /// <summary>Available as narrative context/background, not inline specifics.</summary>
    Context,
    /// <summary>Excluded — must not be used for the current query/subject.</summary>
    Excluded,
    /// <summary>Not yet classified.</summary>
    Unknown,
}

/// <summary>
/// Canon authority level for a knowledge source.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CanonAuthority
{
    /// <summary>Primary/definitive canon source (e.g. character lore file).</summary>
    Primary,
    /// <summary>Secondary source (e.g. shared world doc referencing the character).</summary>
    Secondary,
    /// <summary>Background/shared context not specific to any character.</summary>
    Background,
    /// <summary>User override or session-local canon.</summary>
    Override,
    /// <summary>Temporary/correction note.</summary>
    Provisional,
}

/// <summary>
/// Kind/type of a knowledge source.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SubjectSourceKind
{
    /// <summary>Character lore file.</summary>
    CharacterFile,
    /// <summary>World/body-tech lore file.</summary>
    WorldFile,
    /// <summary>Faction/organization lore file.</summary>
    FactionFile,
    /// <summary>Event/plot lore file.</summary>
    EventFile,
    /// <summary>Item/technology lore file.</summary>
    ItemFile,
    /// <summary>Location/setting lore file.</summary>
    LocationFile,
    /// <summary>User-authored correction/override note.</summary>
    Correction,
    /// <summary>Session-captured canon note.</summary>
    SessionCanon,
    /// <summary>Unknown/unclassified source.</summary>
    Unknown,
}

// ──────────────────────────────────────────────
// Structured protocol payloads
// ──────────────────────────────────────────────

/// <summary>
/// Request for roleplay knowledge retrieval. Carries active subject context
/// so the provider can filter/classify before returning evidence.
/// </summary>
public sealed record RoleplayKnowledgeRequest
{
    /// <summary>The natural-language query.</summary>
    [JsonPropertyName("query")]
    public required string Query { get; init; }

    /// <summary>The active subject/character this query is about.</summary>
    [JsonPropertyName("active_subject")]
    public string? ActiveSubject { get; init; }

    /// <summary>Character name to treat as off-character/excluded.</summary>
    [JsonPropertyName("excluded_subjects")]
    public IReadOnlyList<string>? ExcludedSubjects { get; init; }

    /// <summary>Lore set name to search.</summary>
    [JsonPropertyName("lore_set")]
    public string? LoreSet { get; init; }

    /// <summary>Optional original request ID for trace correlation.</summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }

    /// <summary>Whether to include shared/world-level context in results.</summary>
    [JsonPropertyName("include_shared_context")]
    public bool IncludeSharedContext { get; init; } = true;
}

/// <summary>
/// A single reference to a lore source file.
/// </summary>
public sealed record RoleplaySourceRef
{
    /// <summary>Path or identifier of the source file.</summary>
    [JsonPropertyName("source_path")]
    public required string SourcePath { get; init; }

    /// <summary>Kind/type of source.</summary>
    [JsonPropertyName("source_kind")]
    public SubjectSourceKind SourceKind { get; init; } = SubjectSourceKind.Unknown;

    /// <summary>Canon authority level of this source for the referenced fact.</summary>
    [JsonPropertyName("authority")]
    public CanonAuthority Authority { get; init; } = CanonAuthority.Background;

    /// <summary>Title or label for display.</summary>
    [JsonPropertyName("title")]
    public string? Title { get; init; }
}

/// <summary>
/// Single evidence item — one passage with provenance, applicability, and allowed-use.
/// </summary>
public sealed record RoleplayEvidenceItem
{
    /// <summary>The passage text (may be truncated for very long content).</summary>
    [JsonPropertyName("passage")]
    public required string Passage { get; init; }

    /// <summary>How this passage applies to the active subject.</summary>
    [JsonPropertyName("applicability")]
    public ActiveSubjectApplicability Applicability { get; init; } = ActiveSubjectApplicability.Unknown;

    /// <summary>How this passage may be used.</summary>
    [JsonPropertyName("allowed_use")]
    public AllowedUse AllowedUse { get; init; } = AllowedUse.Unknown;

    /// <summary>Source file provenance.</summary>
    [JsonPropertyName("source_refs")]
    public IReadOnlyList<RoleplaySourceRef>? SourceRefs { get; init; }

    /// <summary>Subject this passage is about (if different from active_subject).</summary>
    [JsonPropertyName("subject_ref")]
    public RoleplaySubjectRef? SubjectRef { get; init; }

    /// <summary>Ambiguity notes, if any.</summary>
    [JsonPropertyName("ambiguity")]
    public RoleplayAmbiguity? Ambiguity { get; init; }
}

/// <summary>
/// Reference to a character/subject that a piece of knowledge is about.
/// </summary>
public sealed record RoleplaySubjectRef
{
    /// <summary>Subject name.</summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>Confidence that this subject is the one described.</summary>
    [JsonPropertyName("confidence")]
    public string Confidence { get; init; } = "high";

    /// <summary>Aliases or alternative names.</summary>
    [JsonPropertyName("aliases")]
    public IReadOnlyList<string>? Aliases { get; init; }
}

/// <summary>
/// Ambiguity signal — when the provider cannot clearly attribute a fact.
/// </summary>
public sealed record RoleplayAmbiguity
{
    /// <summary>Whether the provider asked for clarification.</summary>
    [JsonPropertyName("asked_for_clarification")]
    public bool AskedForClarification { get; init; }

    /// <summary>Description of the ambiguity.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>Possible subjects this could refer to.</summary>
    [JsonPropertyName("possible_subjects")]
    public IReadOnlyList<string>? PossibleSubjects { get; init; }
}

/// <summary>
/// Structured directive for how a piece of knowledge should be handled.
/// </summary>
public sealed record RoleplayDirective
{
    /// <summary>The active subject this directive applies to.</summary>
    [JsonPropertyName("for_subject")]
    public string? ForSubject { get; init; }

    /// <summary>The knowledge this directive applies to.</summary>
    [JsonPropertyName("knowledge_scope")]
    public RoleplayKnowledgeScope KnowledgeScope { get; init; }

    /// <summary>How this knowledge may be used.</summary>
    [JsonPropertyName("allowed_use")]
    public AllowedUse AllowedUse { get; init; } = AllowedUse.Unknown;

    /// <summary>Optional reason for the directive.</summary>
    [JsonPropertyName("reason")]
    public string? Reason { get; init; }
}

/// <summary>
/// Full structured knowledge packet returned by query_lore/query_context.
/// This is the primary structured handoff payload.
/// </summary>
public sealed record RoleplayKnowledgePacket
{
    /// <summary>The original query.</summary>
    [JsonPropertyName("query")]
    public required string Query { get; init; }

    /// <summary>The active subject at time of request.</summary>
    [JsonPropertyName("active_subject")]
    public string? ActiveSubject { get; init; }

    /// <summary>Scope of this knowledge packet.</summary>
    [JsonPropertyName("scope")]
    public RoleplayKnowledgeScope Scope { get; init; }

    /// <summary>Evidence items (passages with provenance and classification).</summary>
    [JsonPropertyName("evidence")]
    public IReadOnlyList<RoleplayEvidenceItem> Evidence { get; init; } = [];

    /// <summary>Directives for how this packet's knowledge should be used.</summary>
    [JsonPropertyName("directives")]
    public IReadOnlyList<RoleplayDirective>? Directives { get; init; }

    /// <summary>Source file list (summary provenance).</summary>
    [JsonPropertyName("source_files")]
    public IReadOnlyList<string>? SourceFiles { get; init; }

    /// <summary>Confidence level: high, medium, low.</summary>
    [JsonPropertyName("confidence")]
    public string Confidence { get; init; } = "high";

    /// <summary>Correlation ID for trace linking.</summary>
    [JsonPropertyName("request_id")]
    public string? RequestId { get; init; }

    /// <summary>The component that produced this packet, e.g. \"query_lore\", \"query_context\".</summary>
    [JsonPropertyName("source_component")]
    public string? SourceComponent { get; init; }
}

/// <summary>
/// Structured scene brief passed from Narrative Director to ProseWriter.
/// Carries active-subject context, directives, and knowledge references so
/// the prose writer can obey allowed-use boundaries.
/// </summary>
public sealed record StructuredSceneBrief
{
    /// <summary>The scene description / narrative direction text.</summary>
    [JsonPropertyName("scene_description")]
    public required string SceneDescription { get; init; }

    /// <summary>The active character/subject for this scene.</summary>
    [JsonPropertyName("active_subject")]
    public string? ActiveSubject { get; init; }

    /// <summary>Characters that are off-limits for inline attribution.</summary>
    [JsonPropertyName("excluded_subjects")]
    public IReadOnlyList<string>? ExcludedSubjects { get; init; }

    /// <summary>Knowledge packets that informed this scene brief.</summary>
    [JsonPropertyName("knowledge_packets")]
    public IReadOnlyList<RoleplayKnowledgePacket>? KnowledgePackets { get; init; }

    /// <summary>Active directives for knowledge use in this scene.</summary>
    [JsonPropertyName("directives")]
    public IReadOnlyList<RoleplayDirective>? Directives { get; init; }

    /// <summary>Tone/mood notes for the scene.</summary>
    [JsonPropertyName("tone_notes")]
    public string? ToneNotes { get; init; }

    /// <summary>Optional story state summary for context.</summary>
    [JsonPropertyName("story_context")]
    public string? StoryContext { get; init; }

    /// <summary>The component that produced this brief.</summary>
    [JsonPropertyName("source_component")]
    public string? SourceComponent { get; init; }
}
