using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class ParticipantChannelService
{
    public ParticipantCommunicationApplyResult JoinParticipant(
        ParticipantCommunicationState state,
        JoinParticipantChannelIntentCommand command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);

        var sequence = TakeNextSequence(state);
        var participant = state.Participants.FirstOrDefault(item => item.ParticipantId == command.ParticipantId);
        if (participant is null)
        {
            participant = new ParticipantPresenceState
            {
                ParticipantId = command.ParticipantId,
                DisplayName = command.DisplayName,
                IsJoined = true,
                JoinedSequence = sequence
            };
            state.Participants.Add(participant);
        }
        else
        {
            participant.DisplayName = command.DisplayName;
            participant.IsJoined = true;
            participant.JoinedSequence = sequence;
            participant.LeftSequence = null;
        }

        EnsureCursor(state, command.ParticipantId);
        return ParticipantCommunicationApplyResult.Accepted(new ParticipantJoinedChannelEvent(
            command.ParticipantId,
            command.DisplayName,
            sequence,
            command.OccurredAt));
    }

    public ParticipantCommunicationApplyResult LeaveParticipant(
        ParticipantCommunicationState state,
        LeaveParticipantChannelIntentCommand command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);

        var participant = FindJoinedParticipant(state, command.ParticipantId);
        if (participant is null)
        {
            return ParticipantCommunicationApplyResult.Rejected(new ParticipantCommunicationIssue(
                "participant_not_joined",
                $"Participant '{command.ParticipantId}' is not joined."));
        }

        var sequence = TakeNextSequence(state);
        participant.IsJoined = false;
        participant.LeftSequence = sequence;

        return ParticipantCommunicationApplyResult.Accepted(new ParticipantLeftChannelEvent(
            command.ParticipantId,
            sequence,
            command.OccurredAt));
    }

    public ParticipantCommunicationApplyResult PostPublicMessage(
        ParticipantCommunicationState state,
        PostParticipantChannelMessageIntentCommand command,
        ParticipantCommunicationPermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(permissions);

        if (!permissions.AllowsPublicMessages)
        {
            return RejectPublicMessage(command, "public_channel_forbidden");
        }

        if (FindJoinedParticipant(state, command.Author.ParticipantId) is null)
        {
            return RejectPublicMessage(command, "participant_not_joined");
        }

        if (string.IsNullOrWhiteSpace(command.Text))
        {
            return RejectPublicMessage(command, "empty_message");
        }

        if (ContainsMessageId(state, command.MessageId))
        {
            return RejectPublicMessage(command, "duplicate_message_id");
        }

        var sequence = TakeNextSequence(state);
        state.ChannelMessages.Add(new ParticipantChannelMessage(
            command.MessageId,
            sequence,
            command.Author,
            command.Text,
            command.CreatedAt));

        return ParticipantCommunicationApplyResult.Accepted(new ParticipantChannelMessagePostedEvent(
            command.MessageId,
            command.Author.ParticipantId,
            sequence,
            command.CreatedAt));
    }

    public ParticipantCommunicationApplyResult SendDirectMessage(
        ParticipantCommunicationState state,
        SendParticipantDirectMessageIntentCommand command,
        ParticipantCommunicationPermissions permissions)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(permissions);

        if (!permissions.AllowsDirectMessages)
        {
            return RejectDirectMessage(command, "dm_forbidden");
        }

        if (FindJoinedParticipant(state, command.Author.ParticipantId) is null)
        {
            return RejectDirectMessage(command, "participant_not_joined");
        }

        if (command.RecipientParticipantIds.Count == 0)
        {
            return RejectDirectMessage(command, "missing_recipient");
        }

        var distinctRecipients = command.RecipientParticipantIds
            .Distinct()
            .ToArray();

        if (distinctRecipients.Contains(command.Author.ParticipantId))
        {
            return RejectDirectMessage(command, "sender_cannot_receive_own_dm");
        }

        if (distinctRecipients.Any(recipient => FindJoinedParticipant(state, recipient) is null))
        {
            return RejectDirectMessage(command, "recipient_not_joined");
        }

        if (!IsDirectMessageRouteAllowed(command.Author.ParticipantId, distinctRecipients, permissions))
        {
            return RejectDirectMessage(command, "dm_recipient_forbidden");
        }

        if (string.IsNullOrWhiteSpace(command.Text))
        {
            return RejectDirectMessage(command, "empty_message");
        }

        if (ContainsMessageId(state, command.MessageId))
        {
            return RejectDirectMessage(command, "duplicate_message_id");
        }

        var sequence = TakeNextSequence(state);
        state.DirectMessages.Add(new ParticipantDirectMessage(
            command.MessageId,
            sequence,
            command.Author,
            distinctRecipients,
            command.Text,
            command.CreatedAt));

        return ParticipantCommunicationApplyResult.Accepted(new ParticipantDirectMessageSentEvent(
            command.MessageId,
            command.Author.ParticipantId,
            distinctRecipients,
            sequence,
            command.CreatedAt));
    }

    public ParticipantCommunicationApplyResult LinkGameEvent(
        ParticipantCommunicationState state,
        LinkParticipantGameEventIntentCommand command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);

        if (string.IsNullOrWhiteSpace(command.GameEventId))
        {
            return ParticipantCommunicationApplyResult.Rejected(new ParticipantCommunicationIssue(
                "missing_game_event_id",
                "Game event links require an event ID."));
        }

        if (state.GameEventLinks.Any(link => link.LinkId == command.LinkId))
        {
            return ParticipantCommunicationApplyResult.Rejected(new ParticipantCommunicationIssue(
                "duplicate_link_id",
                $"Communication link '{command.LinkId}' already exists."));
        }

        var visibleTo = command.VisibleToParticipantIds.Distinct().ToArray();
        if (command.Visibility == ParticipantGameEventLinkVisibility.PrivateToParticipantSet && visibleTo.Length == 0)
        {
            return ParticipantCommunicationApplyResult.Rejected(new ParticipantCommunicationIssue(
                "missing_visible_participants",
                "Private game event links require at least one visible participant."));
        }

        var sequence = TakeNextSequence(state);
        state.GameEventLinks.Add(new ParticipantGameEventLink(
            command.LinkId,
            sequence,
            command.GameEventId,
            command.GameEventSequence,
            command.Visibility,
            visibleTo,
            command.Summary,
            command.CreatedAt));

        return ParticipantCommunicationApplyResult.Accepted(new ParticipantGameEventLinkedEvent(
            command.LinkId,
            command.GameEventId,
            sequence,
            command.CreatedAt));
    }

    public ParticipantCommunicationApplyResult AdvanceCursor(
        ParticipantCommunicationState state,
        AdvanceParticipantCommunicationCursorIntentCommand command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);

        if (command.ThroughSequence >= state.NextSequence)
        {
            return RejectCursor(command, "cursor_sequence_not_committed");
        }

        var cursor = EnsureCursor(state, command.ParticipantId);
        var previous = command.CursorKind == ParticipantCommunicationCursorKind.Delivered
            ? cursor.DeliveredThroughSequence
            : cursor.ReadThroughSequence;

        if (command.ThroughSequence < previous)
        {
            return RejectCursor(command, "cursor_cannot_move_backward");
        }

        if (command.CursorKind == ParticipantCommunicationCursorKind.Delivered)
        {
            cursor.DeliveredThroughSequence = command.ThroughSequence;
        }
        else
        {
            cursor.ReadThroughSequence = command.ThroughSequence;
        }

        return ParticipantCommunicationApplyResult.Accepted(new ParticipantCommunicationCursorAdvancedEvent(
            command.ParticipantId,
            command.CursorKind,
            previous,
            command.ThroughSequence,
            command.OccurredAt));
    }

    public ParticipantFeedProjection ProjectPublicFeed(ParticipantCommunicationState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var entries = PublicFeedEntries(state)
            .OrderBy(entry => entry.Sequence)
            .ToArray();

        return new ParticipantFeedProjection(null, entries);
    }

    public ParticipantFeedProjection ProjectParticipantFeed(
        ParticipantCommunicationState state,
        GameParticipantId participantId)
    {
        ArgumentNullException.ThrowIfNull(state);

        var entries = PublicFeedEntries(state)
            .Concat(DirectMessagesVisibleTo(state, participantId))
            .Concat(GameEventLinksVisibleToParticipant(state, participantId))
            .OrderBy(entry => entry.Sequence)
            .ToArray();

        return new ParticipantFeedProjection(participantId, entries);
    }

    private static IEnumerable<ParticipantFeedEntry> PublicFeedEntries(ParticipantCommunicationState state)
    {
        foreach (var message in state.ChannelMessages)
        {
            yield return ChannelEntry(message);
        }

        foreach (var link in state.GameEventLinks.Where(link => link.Visibility == ParticipantGameEventLinkVisibility.Public))
        {
            yield return GameEventLinkEntry(link);
        }
    }

    private static IEnumerable<ParticipantFeedEntry> DirectMessagesVisibleTo(
        ParticipantCommunicationState state,
        GameParticipantId participantId)
    {
        foreach (var message in state.DirectMessages.Where(message =>
            message.Author.ParticipantId == participantId
            || message.RecipientParticipantIds.Contains(participantId)))
        {
            yield return new ParticipantFeedEntry(
                message.Sequence,
                ParticipantFeedEntryKind.DirectMessage,
                message.MessageId,
                null,
                message.Author,
                message.RecipientParticipantIds.ToArray(),
                message.Text,
                null,
                null,
                null,
                message.CreatedAt);
        }
    }

    private static IEnumerable<ParticipantFeedEntry> GameEventLinksVisibleToParticipant(
        ParticipantCommunicationState state,
        GameParticipantId participantId)
    {
        foreach (var link in state.GameEventLinks.Where(link =>
            link.Visibility == ParticipantGameEventLinkVisibility.PrivateToParticipantSet
            && link.VisibleToParticipantIds.Contains(participantId)))
        {
            yield return GameEventLinkEntry(link);
        }
    }

    private static ParticipantFeedEntry ChannelEntry(ParticipantChannelMessage message) =>
        new(
            message.Sequence,
            ParticipantFeedEntryKind.PublicChannelMessage,
            message.MessageId,
            null,
            message.Author,
            [],
            message.Text,
            null,
            null,
            null,
            message.CreatedAt);

    private static ParticipantFeedEntry GameEventLinkEntry(ParticipantGameEventLink link) =>
        new(
            link.Sequence,
            ParticipantFeedEntryKind.GameEventLink,
            null,
            link.LinkId,
            null,
            link.VisibleToParticipantIds.ToArray(),
            null,
            link.GameEventId,
            link.GameEventSequence,
            link.Summary,
            link.CreatedAt);

    private static bool IsDirectMessageRouteAllowed(
        GameParticipantId senderParticipantId,
        IReadOnlyList<GameParticipantId> recipients,
        ParticipantCommunicationPermissions permissions)
    {
        if (permissions.AllowedDirectMessageRoutes.Count == 0)
        {
            return true;
        }

        var route = permissions.AllowedDirectMessageRoutes.FirstOrDefault(item => item.SenderParticipantId == senderParticipantId);
        return route is not null && recipients.All(recipient => route.RecipientParticipantIds.Contains(recipient));
    }

    private static ParticipantPresenceState? FindJoinedParticipant(
        ParticipantCommunicationState state,
        GameParticipantId participantId) =>
        state.Participants.FirstOrDefault(item => item.ParticipantId == participantId && item.IsJoined);

    private static ParticipantCommunicationCursor EnsureCursor(
        ParticipantCommunicationState state,
        GameParticipantId participantId)
    {
        var cursor = state.Cursors.FirstOrDefault(item => item.ParticipantId == participantId);
        if (cursor is not null)
        {
            return cursor;
        }

        cursor = new ParticipantCommunicationCursor
        {
            ParticipantId = participantId
        };
        state.Cursors.Add(cursor);
        return cursor;
    }

    private static long TakeNextSequence(ParticipantCommunicationState state)
    {
        var sequence = state.NextSequence;
        state.NextSequence++;
        return sequence;
    }

    private static bool ContainsMessageId(ParticipantCommunicationState state, Guid messageId) =>
        state.ChannelMessages.Any(message => message.MessageId == messageId)
        || state.DirectMessages.Any(message => message.MessageId == messageId);

    private static ParticipantCommunicationApplyResult RejectPublicMessage(
        PostParticipantChannelMessageIntentCommand command,
        string reasonCode) =>
        new(
            [new ParticipantChannelMessageRejectedEvent(command.Author.ParticipantId, reasonCode, command.CreatedAt)],
            [new ParticipantCommunicationIssue(reasonCode, $"Public channel message rejected: {reasonCode}.")]);

    private static ParticipantCommunicationApplyResult RejectDirectMessage(
        SendParticipantDirectMessageIntentCommand command,
        string reasonCode) =>
        new(
            [new ParticipantDirectMessageRejectedEvent(command.Author.ParticipantId, command.RecipientParticipantIds.ToArray(), reasonCode, command.CreatedAt)],
            [new ParticipantCommunicationIssue(reasonCode, $"Direct message rejected: {reasonCode}.")]);

    private static ParticipantCommunicationApplyResult RejectCursor(
        AdvanceParticipantCommunicationCursorIntentCommand command,
        string reasonCode) =>
        new(
            [new ParticipantCommunicationCursorRejectedEvent(command.ParticipantId, command.CursorKind, reasonCode, command.OccurredAt)],
            [new ParticipantCommunicationIssue(reasonCode, $"Communication cursor advance rejected: {reasonCode}.")]);
}
