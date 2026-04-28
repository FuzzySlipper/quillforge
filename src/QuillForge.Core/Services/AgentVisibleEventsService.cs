using Den.RulesEngine;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class AgentVisibleEventsService
{
    private readonly GameVisibilityProjector _visibilityProjector;
    private readonly ParticipantChannelService _channelService;

    public AgentVisibleEventsService(
        GameVisibilityProjector visibilityProjector,
        ParticipantChannelService channelService)
    {
        _visibilityProjector = visibilityProjector;
        _channelService = channelService;
    }

    public AgentVisibleEventsSnapshot BuildForPrompt(
        GameRuntimeState runtime,
        RulesGameState liveState,
        string participantId,
        GameRuntimeAgentPromptDeliveryCursor? cursor)
    {
        var prior = cursor is null
            ? AgentVisibleEventsCursor.Empty
            : new AgentVisibleEventsCursor(
                cursor.LastDeliveredPublicEngineEventSequence,
                cursor.DeliveredPrivateEventIds.ToArray(),
                cursor.CommunicationDeliveredThroughSequence,
                cursor.MemoryRevision);
        return Build(runtime, liveState, participantId, prior);
    }

    public AgentVisibleEventsSnapshot BuildForMemorySummary(
        GameRuntimeState runtime,
        RulesGameState liveState,
        GameRuntimeAgentMemoryState? memory,
        string participantId)
    {
        var prior = memory is null
            ? AgentVisibleEventsCursor.Empty
            : new AgentVisibleEventsCursor(
                memory.LastSummarizedPublicEngineEventSequence,
                memory.LastSummarizedPrivateEventIds.ToArray(),
                memory.LastSummarizedCommunicationSequence,
                memory.Revision);
        return Build(runtime, liveState, participantId, prior);
    }

    private AgentVisibleEventsSnapshot Build(
        GameRuntimeState runtime,
        RulesGameState liveState,
        string participantId,
        AgentVisibleEventsCursor prior)
    {
        var participant = new ParticipantId(participantId);
        var playerProjection = _visibilityProjector.ProjectPlayer(liveState, participant);
        var visibleEngineEvents = playerProjection.Events
            .Where(item => IsNewVisibleEngineEvent(liveState, item, prior))
            .OrderBy(item => item.Sequence)
            .ToArray();
        var feed = _channelService.ProjectParticipantFeed(runtime.Communication, new GameParticipantId(participantId));
        var visibleFeed = feed.Entries
            .Where(item => item.Sequence > prior.CommunicationSequence)
            .OrderBy(item => item.Sequence)
            .ToArray();
        var publicCursor = playerProjection.Events
            .Where(item => IsPublicEvent(liveState, item.EventId))
            .Select(item => item.Sequence)
            .DefaultIfEmpty(prior.PublicEngineEventSequence)
            .Max();
        var privateIds = playerProjection.Events
            .Where(item => !IsPublicEvent(liveState, item.EventId))
            .Select(item => item.EventId.ToString())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var communicationCursor = feed.Entries.Count == 0
            ? prior.CommunicationSequence
            : Math.Max(prior.CommunicationSequence, feed.Entries.Max(item => item.Sequence));
        var next = new AgentVisibleEventsCursor(
            publicCursor,
            privateIds,
            communicationCursor,
            prior.MemoryRevision);
        return new AgentVisibleEventsSnapshot(
            runtime.GameInstanceId ?? liveState.GameInstanceId.Value,
            participantId,
            prior,
            next,
            visibleEngineEvents,
            visibleFeed);
    }

    private static bool IsNewVisibleEngineEvent(
        RulesGameState state,
        VisibleGameEvent visibleEvent,
        AgentVisibleEventsCursor prior)
    {
        if (IsPublicEvent(state, visibleEvent.EventId))
        {
            return visibleEvent.Sequence > prior.PublicEngineEventSequence;
        }

        return !prior.PrivateEngineEventIds.Any(item => string.Equals(item, visibleEvent.EventId.ToString(), StringComparison.Ordinal));
    }

    private static bool IsPublicEvent(RulesGameState state, GameEventId eventId)
    {
        var gameEvent = state.EventJournal.Events.FirstOrDefault(item => item.EventId == eventId);
        return gameEvent?.Visibility.Kind == GameEventVisibilityKind.Public;
    }
}
