namespace Den.RulesEngine;

public enum GameEventVisibilityKind
{
    Public,
    PrivateToParticipant,
    PrivateToSet,
    HiddenSystemOnly
}

public sealed record GameEventVisibility(
    GameEventVisibilityKind Kind,
    ParticipantId? ParticipantId = null,
    ParticipantSetId? ParticipantSetId = null)
{
    public static GameEventVisibility Public { get; } = new(GameEventVisibilityKind.Public);

    public static GameEventVisibility HiddenSystemOnly { get; } = new(GameEventVisibilityKind.HiddenSystemOnly);

    public static GameEventVisibility PrivateToParticipant(ParticipantId participantId) =>
        new(GameEventVisibilityKind.PrivateToParticipant, ParticipantId: participantId);

    public static GameEventVisibility PrivateToSet(ParticipantSetId participantSetId) =>
        new(GameEventVisibilityKind.PrivateToSet, ParticipantSetId: participantSetId);

    public bool IsVisibleTo(ParticipantState participant)
    {
        ArgumentNullException.ThrowIfNull(participant);

        return Kind switch
        {
            GameEventVisibilityKind.Public => true,
            GameEventVisibilityKind.PrivateToParticipant => ParticipantId == participant.ParticipantId,
            GameEventVisibilityKind.PrivateToSet => ParticipantSetId is not null
                && participant.ParticipantSetIds.Contains(ParticipantSetId.Value),
            GameEventVisibilityKind.HiddenSystemOnly => false,
            _ => false
        };
    }
}
