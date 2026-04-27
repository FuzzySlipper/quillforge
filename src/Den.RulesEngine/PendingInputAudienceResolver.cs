namespace Den.RulesEngine;

internal static class PendingInputAudienceResolver
{
    public static IReadOnlyList<ParticipantId> Resolve(RulesGameState state, PendingInputAudience audience)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(audience);

        return audience.Kind switch
        {
            PendingInputAudienceKind.OneParticipant => audience.ParticipantId is null
                ? []
                : [audience.ParticipantId.Value],
            PendingInputAudienceKind.ManyParticipants => audience.ParticipantIds.ToArray(),
            PendingInputAudienceKind.AllActiveParticipants => state.Participants
                .Where(participant => participant.IsActive && participant.Kind != ParticipantKind.System)
                .Select(participant => participant.ParticipantId)
                .ToArray(),
            _ => []
        };
    }
}
