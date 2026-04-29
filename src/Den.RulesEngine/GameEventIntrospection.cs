namespace Den.RulesEngine;

public static class GameEventIntrospection
{
    public static GameEventIntrospectionFacts Inspect(IGameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        return new GameEventIntrospectionFacts(
            ParticipantIdFor(gameEvent),
            PendingInputIdFor(gameEvent),
            ChoiceNameFor(gameEvent),
            ReasonCodeFor(gameEvent),
            OutcomeNameFor(gameEvent));
    }

    public static string? ParticipantIdFor(IGameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        return gameEvent switch
        {
            PlayerChoiceSubmittedEvent choice => choice.ParticipantId.Value,
            AgentResponseRejectedEvent rejected => rejected.ParticipantId.Value,
            NoActionTakenEvent noAction => noAction.ParticipantId.Value,
            PendingInputRequestedEvent requested => requested.ParticipantId.Value,
            _ => null,
        };
    }

    public static string? PendingInputIdFor(IGameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        return gameEvent switch
        {
            PlayerChoiceSubmittedEvent choice => choice.PendingInputId.Value,
            AgentResponseRejectedEvent rejected => rejected.PendingInputId.Value,
            NoActionTakenEvent noAction => noAction.PendingInputId.Value,
            PendingInputRequestedEvent requested => requested.PendingInputId.Value,
            _ => null,
        };
    }

    public static string? ChoiceNameFor(IGameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        return gameEvent switch
        {
            PlayerChoiceSubmittedEvent choice => choice.ChoiceName,
            _ => null,
        };
    }

    public static string? ReasonCodeFor(IGameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        return gameEvent switch
        {
            AgentResponseRejectedEvent rejected => rejected.ReasonCode,
            NoActionTakenEvent noAction => noAction.ReasonCode,
            IntentCommandRejectedEvent rejected => rejected.ReasonCode,
            GameAbortedEvent aborted => aborted.ReasonCode,
            RoundEndedEvent roundEnded => roundEnded.ReasonCode,
            _ => null,
        };
    }

    public static string? OutcomeNameFor(IGameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        return gameEvent switch
        {
            GameEndedEvent ended => ended.OutcomeName,
            _ => null,
        };
    }
}

public sealed record GameEventIntrospectionFacts(
    string? ParticipantId,
    string? PendingInputId,
    string? ChoiceName,
    string? ReasonCode,
    string? OutcomeName);
