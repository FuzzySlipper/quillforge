using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public class ParticipantChannelServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 4, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PublicFeed_CombinesAuthoredMessagesAndPublicGameEventLinksWithoutMixingAuthority()
    {
        var state = NewJoinedState(out var service, Alice, Bob);
        var messageId = Guid.Parse("00000000-0000-0000-0000-000000000101");
        var linkId = Guid.Parse("00000000-0000-0000-0000-000000000201");

        var post = service.PostPublicMessage(
            state,
            new PostParticipantChannelMessageIntentCommand(
                messageId,
                Human(Alice),
                "I nominate Bob.",
                Now),
            ParticipantCommunicationPermissions.AllowAll("day"));
        var link = service.LinkGameEvent(
            state,
            new LinkParticipantGameEventIntentCommand(
                linkId,
                "event-7",
                7,
                ParticipantGameEventLinkVisibility.Public,
                [],
                "Vote recorded.",
                Now.AddSeconds(1)));

        Assert.True(post.IsAccepted);
        Assert.True(link.IsAccepted);

        var projection = service.ProjectPublicFeed(state);

        Assert.Collection(
            projection.Entries,
            entry =>
            {
                Assert.Equal(ParticipantFeedEntryKind.PublicChannelMessage, entry.Kind);
                Assert.Equal(messageId, entry.MessageId);
                Assert.Equal(Alice, entry.Author?.ParticipantId);
                Assert.Equal("I nominate Bob.", entry.Text);
                Assert.Null(entry.GameEventId);
            },
            entry =>
            {
                Assert.Equal(ParticipantFeedEntryKind.GameEventLink, entry.Kind);
                Assert.Equal(linkId, entry.LinkId);
                Assert.Equal("event-7", entry.GameEventId);
                Assert.Equal(7, entry.GameEventSequence);
                Assert.Equal("Vote recorded.", entry.Summary);
                Assert.Null(entry.Author);
                Assert.Null(entry.Text);
            });
    }

    [Fact]
    public void DirectMessages_AreVisibleOnlyToSenderAndRecipients()
    {
        var state = NewJoinedState(out var service, Alice, Bob, Carol);
        var messageId = Guid.Parse("00000000-0000-0000-0000-000000000102");

        var result = service.SendDirectMessage(
            state,
            new SendParticipantDirectMessageIntentCommand(
                messageId,
                Agent(Alice),
                [Bob],
                "Can you confirm your role?",
                Now),
            ParticipantCommunicationPermissions.AllowAll("night"));

        Assert.True(result.IsAccepted);

        var aliceFeed = service.ProjectParticipantFeed(state, Alice);
        var bobFeed = service.ProjectParticipantFeed(state, Bob);
        var carolFeed = service.ProjectParticipantFeed(state, Carol);
        var publicFeed = service.ProjectPublicFeed(state);

        Assert.Contains(aliceFeed.Entries, entry => entry.MessageId == messageId && entry.Kind == ParticipantFeedEntryKind.DirectMessage);
        Assert.Contains(bobFeed.Entries, entry => entry.MessageId == messageId && entry.Kind == ParticipantFeedEntryKind.DirectMessage);
        Assert.DoesNotContain(carolFeed.Entries, entry => entry.MessageId == messageId);
        Assert.DoesNotContain(publicFeed.Entries, entry => entry.MessageId == messageId);
    }

    [Fact]
    public void CursorAdvancement_IsMonotonicAndUsesCommittedSequences()
    {
        var state = NewJoinedState(out var service, Alice, Bob);
        var messageId = Guid.Parse("00000000-0000-0000-0000-000000000103");
        service.PostPublicMessage(
            state,
            new PostParticipantChannelMessageIntentCommand(messageId, Human(Alice), "Hello.", Now),
            ParticipantCommunicationPermissions.AllowAll("day"));

        var delivered = service.AdvanceCursor(
            state,
            new AdvanceParticipantCommunicationCursorIntentCommand(
                Bob,
                ParticipantCommunicationCursorKind.Delivered,
                3,
                Now.AddSeconds(1)));
        var read = service.AdvanceCursor(
            state,
            new AdvanceParticipantCommunicationCursorIntentCommand(
                Bob,
                ParticipantCommunicationCursorKind.Read,
                3,
                Now.AddSeconds(2)));
        var backwards = service.AdvanceCursor(
            state,
            new AdvanceParticipantCommunicationCursorIntentCommand(
                Bob,
                ParticipantCommunicationCursorKind.Delivered,
                2,
                Now.AddSeconds(3)));
        var beyondCommitted = service.AdvanceCursor(
            state,
            new AdvanceParticipantCommunicationCursorIntentCommand(
                Bob,
                ParticipantCommunicationCursorKind.Read,
                state.NextSequence,
                Now.AddSeconds(4)));

        Assert.True(delivered.IsAccepted);
        Assert.True(read.IsAccepted);
        Assert.False(backwards.IsAccepted);
        Assert.False(beyondCommitted.IsAccepted);
        Assert.Equal("cursor_cannot_move_backward", backwards.Issues.Single().Code);
        Assert.Equal("cursor_sequence_not_committed", beyondCommitted.Issues.Single().Code);

        var cursor = state.Cursors.Single(cursor => cursor.ParticipantId == Bob);
        Assert.Equal(3, cursor.DeliveredThroughSequence);
        Assert.Equal(3, cursor.ReadThroughSequence);
    }

    [Fact]
    public void DirectMessagePermission_ChecksHostModuleStageAndRecipientRoutes()
    {
        var state = NewJoinedState(out var service, Alice, Bob, Carol);
        var forbiddenByStage = new ParticipantCommunicationPermissions(
            "night",
            HostAllowsPublicMessages: true,
            ModuleAllowsPublicMessages: true,
            HostAllowsDirectMessages: true,
            ModuleAllowsDirectMessages: false,
            AllowedDirectMessageRoutes: []);
        var routeLimited = new ParticipantCommunicationPermissions(
            "night",
            HostAllowsPublicMessages: true,
            ModuleAllowsPublicMessages: true,
            HostAllowsDirectMessages: true,
            ModuleAllowsDirectMessages: true,
            AllowedDirectMessageRoutes: [new ParticipantDirectMessageRoute(Alice, [Bob])]);

        var stageResult = service.SendDirectMessage(
            state,
            new SendParticipantDirectMessageIntentCommand(
                Guid.Parse("00000000-0000-0000-0000-000000000104"),
                Human(Alice),
                [Bob],
                "Allowed only if module enables DMs.",
                Now),
            forbiddenByStage);
        var routeResult = service.SendDirectMessage(
            state,
            new SendParticipantDirectMessageIntentCommand(
                Guid.Parse("00000000-0000-0000-0000-000000000105"),
                Human(Alice),
                [Carol],
                "This route is forbidden.",
                Now.AddSeconds(1)),
            routeLimited);
        var accepted = service.SendDirectMessage(
            state,
            new SendParticipantDirectMessageIntentCommand(
                Guid.Parse("00000000-0000-0000-0000-000000000106"),
                Human(Alice),
                [Bob],
                "This route is allowed.",
                Now.AddSeconds(2)),
            routeLimited);

        Assert.False(stageResult.IsAccepted);
        Assert.Equal("dm_forbidden", stageResult.Issues.Single().Code);
        Assert.IsType<ParticipantDirectMessageRejectedEvent>(stageResult.Events.Single());

        Assert.False(routeResult.IsAccepted);
        Assert.Equal("dm_recipient_forbidden", routeResult.Issues.Single().Code);

        Assert.True(accepted.IsAccepted);
        Assert.Single(state.DirectMessages);
        Assert.Equal(Bob, state.DirectMessages.Single().RecipientParticipantIds.Single());
    }

    [Fact]
    public void JoinAndLeave_UpdatePresenceWithTypedEventsAndSequences()
    {
        var state = new ParticipantCommunicationState();
        var service = new ParticipantChannelService();

        var joined = service.JoinParticipant(
            state,
            new JoinParticipantChannelIntentCommand(Alice, "Alice", Now));
        var left = service.LeaveParticipant(
            state,
            new LeaveParticipantChannelIntentCommand(Alice, Now.AddSeconds(1)));

        Assert.True(joined.IsAccepted);
        Assert.True(left.IsAccepted);
        var joinedEvent = Assert.IsType<ParticipantJoinedChannelEvent>(joined.Events.Single());
        var leftEvent = Assert.IsType<ParticipantLeftChannelEvent>(left.Events.Single());
        Assert.Equal(1, joinedEvent.Sequence);
        Assert.Equal(2, leftEvent.Sequence);

        var participant = state.Participants.Single();
        Assert.False(participant.IsJoined);
        Assert.Equal(1, participant.JoinedSequence);
        Assert.Equal(2, participant.LeftSequence);
    }

    [Fact]
    public void MessageIdsAreStableGuidsAndSequencesAreMonotonicAcrossCommunicationFacts()
    {
        var state = NewJoinedState(out var service, Alice, Bob);
        var publicMessageId = Guid.Parse("00000000-0000-0000-0000-000000000107");
        var directMessageId = Guid.Parse("00000000-0000-0000-0000-000000000108");

        service.PostPublicMessage(
            state,
            new PostParticipantChannelMessageIntentCommand(publicMessageId, Human(Alice), "Public", Now),
            ParticipantCommunicationPermissions.AllowAll("day"));
        service.SendDirectMessage(
            state,
            new SendParticipantDirectMessageIntentCommand(directMessageId, Agent(Bob), [Alice], "Private", Now.AddSeconds(1)),
            ParticipantCommunicationPermissions.AllowAll("day"));

        Assert.Equal(publicMessageId, state.ChannelMessages.Single().MessageId);
        Assert.Equal(directMessageId, state.DirectMessages.Single().MessageId);
        Assert.Equal([1, 2, 3, 4], state.Participants.Select(participant => participant.JoinedSequence)
            .Concat(state.ChannelMessages.Select(message => message.Sequence))
            .Concat(state.DirectMessages.Select(message => message.Sequence))
            .Order()
            .ToArray());
        Assert.Equal(5, state.NextSequence);
    }

    private static ParticipantCommunicationState NewJoinedState(
        out ParticipantChannelService service,
        params GameParticipantId[] participantIds)
    {
        service = new ParticipantChannelService();
        var state = new ParticipantCommunicationState();
        foreach (var participantId in participantIds)
        {
            var result = service.JoinParticipant(
                state,
                new JoinParticipantChannelIntentCommand(participantId, participantId.Value, Now));
            Assert.True(result.IsAccepted);
        }

        return state;
    }

    private static ParticipantMessageAuthor Human(GameParticipantId participantId) =>
        new(participantId, ParticipantMessageAuthorKind.Human);

    private static ParticipantMessageAuthor Agent(GameParticipantId participantId) =>
        new(participantId, ParticipantMessageAuthorKind.Agent);

    private static readonly GameParticipantId Alice = new("alice");
    private static readonly GameParticipantId Bob = new("bob");
    private static readonly GameParticipantId Carol = new("carol");
}
