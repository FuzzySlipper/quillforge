namespace QuillForge.Core.Models;

/// <summary>
/// Results from a lore query, including relevant passages and provenance.
/// Now optionally carries structured RoleplayKnowledgePacket evidence for
/// roleplay/lore-drift-aware consumers.
/// </summary>
public sealed record LoreBundle
{
    public required IReadOnlyList<string> RelevantPassages { get; init; }
    public required IReadOnlyList<string> SourceFiles { get; init; }
    public LoreConfidence Confidence { get; init; } = LoreConfidence.High;

    /// <summary>
    /// Optional structured knowledge packet for roleplay protocol consumers.
    /// Populated when the Librarian or query_lore handler is configured
    /// to emit structured evidence alongside the legacy bundle format.
    /// </summary>
    public RoleplayKnowledgePacket? StructuredPacket { get; init; }
}

/// <summary>
/// Internal result from LibrarianAgent.QueryAsync that carries both the serializable
/// lore payload and the token usage metadata. QueryLoreHandler uses the bundle for the
/// tool result and the usage for forge stats — keeping accounting data out of the
/// serialized tool payload sent back to the model.
/// </summary>
public sealed record LibrarianResult
{
    public required LoreBundle Bundle { get; init; }
    public required TokenUsage Usage { get; init; }
}

public enum LoreConfidence
{
    High,
    Medium,
    Low
}
