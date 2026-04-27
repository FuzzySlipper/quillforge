namespace QuillForge.Core.Models;

/// <summary>
/// Session-owned participant communication state for active games. V1 persists this
/// object embedded under GameRuntimeState so fork/delete/resume semantics follow
/// the owning session runtime state. Services are the only writers; HTTP/UI
/// adapters should submit typed intent records instead of mutating lists directly.
/// </summary>
public sealed class ParticipantCommunicationState
{
    /// <summary>
    /// Next committed communication sequence. Sequences are global within this
    /// state, monotonic, and never derived from list indexes.
    /// </summary>
    public long NextSequence { get; set; } = 1;

    public List<ParticipantPresenceState> Participants { get; set; } = [];

    public List<ParticipantChannelMessage> ChannelMessages { get; set; } = [];

    public List<ParticipantDirectMessage> DirectMessages { get; set; } = [];

    public List<ParticipantGameEventLink> GameEventLinks { get; set; } = [];

    public List<ParticipantCommunicationCursor> Cursors { get; set; } = [];
}

public readonly record struct GameParticipantId(string Value)
{
    public override string ToString() => Value;
}

public enum ParticipantMessageAuthorKind
{
    Human,
    Agent,
    System
}

public sealed record ParticipantMessageAuthor(
    GameParticipantId ParticipantId,
    ParticipantMessageAuthorKind Kind);

public sealed class ParticipantPresenceState
{
    public GameParticipantId ParticipantId { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public bool IsJoined { get; set; }

    public long JoinedSequence { get; set; }

    public long? LeftSequence { get; set; }
}

public sealed record ParticipantChannelMessage(
    Guid MessageId,
    long Sequence,
    ParticipantMessageAuthor Author,
    string Text,
    DateTimeOffset CreatedAt);

public sealed record ParticipantDirectMessage(
    Guid MessageId,
    long Sequence,
    ParticipantMessageAuthor Author,
    IReadOnlyList<GameParticipantId> RecipientParticipantIds,
    string Text,
    DateTimeOffset CreatedAt);

public sealed record ParticipantGameEventLink(
    Guid LinkId,
    long Sequence,
    string GameEventId,
    long? GameEventSequence,
    ParticipantGameEventLinkVisibility Visibility,
    IReadOnlyList<GameParticipantId> VisibleToParticipantIds,
    string Summary,
    DateTimeOffset CreatedAt);

public enum ParticipantGameEventLinkVisibility
{
    Public,
    PrivateToParticipantSet,
    SystemOnly
}

/// <summary>
/// Participant cursor over the communication sequence. Invisible direct
/// messages may be skipped by projection; advancing through a global sequence
/// means the participant has been offered every visible item up to that point.
/// </summary>
public sealed class ParticipantCommunicationCursor
{
    public GameParticipantId ParticipantId { get; set; }

    public long DeliveredThroughSequence { get; set; }

    public long ReadThroughSequence { get; set; }
}

public sealed record JoinParticipantChannelIntentCommand(
    GameParticipantId ParticipantId,
    string DisplayName,
    DateTimeOffset OccurredAt);

public sealed record LeaveParticipantChannelIntentCommand(
    GameParticipantId ParticipantId,
    DateTimeOffset OccurredAt);

public sealed record PostParticipantChannelMessageIntentCommand(
    Guid MessageId,
    ParticipantMessageAuthor Author,
    string Text,
    DateTimeOffset CreatedAt);

public sealed record SendParticipantDirectMessageIntentCommand(
    Guid MessageId,
    ParticipantMessageAuthor Author,
    IReadOnlyList<GameParticipantId> RecipientParticipantIds,
    string Text,
    DateTimeOffset CreatedAt);

public sealed record LinkParticipantGameEventIntentCommand(
    Guid LinkId,
    string GameEventId,
    long? GameEventSequence,
    ParticipantGameEventLinkVisibility Visibility,
    IReadOnlyList<GameParticipantId> VisibleToParticipantIds,
    string Summary,
    DateTimeOffset CreatedAt);

public sealed record AdvanceParticipantCommunicationCursorIntentCommand(
    GameParticipantId ParticipantId,
    ParticipantCommunicationCursorKind CursorKind,
    long ThroughSequence,
    DateTimeOffset OccurredAt);

public enum ParticipantCommunicationCursorKind
{
    Delivered,
    Read
}

/// <summary>
/// Stage-specific permission snapshot supplied by the host/bridge after reading
/// module metadata and active stage rules. Empty direct-message routes mean any
/// joined non-sender participant may receive a DM while DMs are enabled.
/// </summary>
public sealed record ParticipantCommunicationPermissions(
    string StageId,
    bool HostAllowsPublicMessages,
    bool ModuleAllowsPublicMessages,
    bool HostAllowsDirectMessages,
    bool ModuleAllowsDirectMessages,
    IReadOnlyList<ParticipantDirectMessageRoute> AllowedDirectMessageRoutes)
{
    public bool AllowsPublicMessages => HostAllowsPublicMessages && ModuleAllowsPublicMessages;

    public bool AllowsDirectMessages => HostAllowsDirectMessages && ModuleAllowsDirectMessages;

    public static ParticipantCommunicationPermissions AllowAll(string stageId) =>
        new(stageId, true, true, true, true, []);
}

public sealed record ParticipantDirectMessageRoute(
    GameParticipantId SenderParticipantId,
    IReadOnlyList<GameParticipantId> RecipientParticipantIds);

public sealed record ParticipantCommunicationApplyResult(
    IReadOnlyList<IParticipantCommunicationEvent> Events,
    IReadOnlyList<ParticipantCommunicationIssue> Issues)
{
    public bool IsAccepted => Issues.Count == 0;

    public static ParticipantCommunicationApplyResult Accepted(params IParticipantCommunicationEvent[] events) =>
        new(events, []);

    public static ParticipantCommunicationApplyResult Rejected(params ParticipantCommunicationIssue[] issues) =>
        new([], issues);
}

public sealed record ParticipantCommunicationIssue(
    string Code,
    string Message);

public interface IParticipantCommunicationEvent
{
    long? Sequence { get; }

    DateTimeOffset OccurredAt { get; }
}

public sealed record ParticipantJoinedChannelEvent(
    GameParticipantId ParticipantId,
    string DisplayName,
    long Sequence,
    DateTimeOffset OccurredAt) : IParticipantCommunicationEvent
{
    long? IParticipantCommunicationEvent.Sequence => Sequence;
}

public sealed record ParticipantLeftChannelEvent(
    GameParticipantId ParticipantId,
    long Sequence,
    DateTimeOffset OccurredAt) : IParticipantCommunicationEvent
{
    long? IParticipantCommunicationEvent.Sequence => Sequence;
}

public sealed record ParticipantChannelMessagePostedEvent(
    Guid MessageId,
    GameParticipantId SenderParticipantId,
    long Sequence,
    DateTimeOffset OccurredAt) : IParticipantCommunicationEvent
{
    long? IParticipantCommunicationEvent.Sequence => Sequence;
}

public sealed record ParticipantDirectMessageSentEvent(
    Guid MessageId,
    GameParticipantId SenderParticipantId,
    IReadOnlyList<GameParticipantId> RecipientParticipantIds,
    long Sequence,
    DateTimeOffset OccurredAt) : IParticipantCommunicationEvent
{
    long? IParticipantCommunicationEvent.Sequence => Sequence;
}

public sealed record ParticipantGameEventLinkedEvent(
    Guid LinkId,
    string GameEventId,
    long Sequence,
    DateTimeOffset OccurredAt) : IParticipantCommunicationEvent
{
    long? IParticipantCommunicationEvent.Sequence => Sequence;
}

public sealed record ParticipantCommunicationCursorAdvancedEvent(
    GameParticipantId ParticipantId,
    ParticipantCommunicationCursorKind CursorKind,
    long PreviousSequence,
    long CurrentSequence,
    DateTimeOffset OccurredAt) : IParticipantCommunicationEvent
{
    long? IParticipantCommunicationEvent.Sequence => null;
}

public sealed record ParticipantChannelMessageRejectedEvent(
    GameParticipantId SenderParticipantId,
    string ReasonCode,
    DateTimeOffset OccurredAt) : IParticipantCommunicationEvent
{
    public long? Sequence => null;
}

public sealed record ParticipantDirectMessageRejectedEvent(
    GameParticipantId SenderParticipantId,
    IReadOnlyList<GameParticipantId> RecipientParticipantIds,
    string ReasonCode,
    DateTimeOffset OccurredAt) : IParticipantCommunicationEvent
{
    public long? Sequence => null;
}

public sealed record ParticipantCommunicationCursorRejectedEvent(
    GameParticipantId ParticipantId,
    ParticipantCommunicationCursorKind CursorKind,
    string ReasonCode,
    DateTimeOffset OccurredAt) : IParticipantCommunicationEvent
{
    public long? Sequence => null;
}

public sealed record ParticipantFeedProjection(
    GameParticipantId? ParticipantId,
    IReadOnlyList<ParticipantFeedEntry> Entries);

public sealed record ParticipantFeedEntry(
    long Sequence,
    ParticipantFeedEntryKind Kind,
    Guid? MessageId,
    Guid? LinkId,
    ParticipantMessageAuthor? Author,
    IReadOnlyList<GameParticipantId> RecipientParticipantIds,
    string? Text,
    string? GameEventId,
    long? GameEventSequence,
    string? Summary,
    DateTimeOffset CreatedAt);

public enum ParticipantFeedEntryKind
{
    PublicChannelMessage,
    DirectMessage,
    GameEventLink
}
