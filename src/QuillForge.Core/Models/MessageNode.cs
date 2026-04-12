namespace QuillForge.Core.Models;

/// <summary>
/// A single node in a conversation tree. Immutable once created;
/// child list mutations are managed by ConversationTree under lock.
/// </summary>
public sealed record MessageNode
{
    public required Guid Id { get; init; }
    public required Guid? ParentId { get; init; }
    public required string Role { get; init; }
    public required MessageContent Content { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public IReadOnlyList<Guid> ChildIds { get; init; } = [];
    public MessageMetadata? Metadata { get; init; }

    /// <summary>
    /// Rebuilds a completion message from the persisted conversation node.
    /// Provider replay stays attached so reasoning-capable adapters can
    /// reconstruct provider-shaped history after session reload.
    /// </summary>
    public CompletionMessage ToCompletionMessage()
    {
        return new CompletionMessage(Role, Content)
        {
            ProviderReplay = Metadata?.ProviderReplay,
        };
    }
}

/// <summary>
/// Optional metadata attached to a message (token usage, model info, etc.).
/// </summary>
public sealed record MessageMetadata
{
    public string? Model { get; init; }
    public int? InputTokens { get; init; }
    public int? OutputTokens { get; init; }
    public StopReason? StopReason { get; init; }
    public string? Reasoning { get; init; }
    public IReadOnlyList<ReasoningArtifact> ReasoningArtifacts { get; init; } = [];

    /// <summary>
    /// Adapter-owned replay data preserved with the message so resumed sessions
    /// can rebuild provider-specific history without flattening reasoning into
    /// the generic transcript.
    /// </summary>
    public ProviderReplayEnvelope? ProviderReplay { get; init; }
}
