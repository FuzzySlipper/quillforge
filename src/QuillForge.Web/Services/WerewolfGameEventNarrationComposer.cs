using Den.RulesEngine;
using Den.RulesEngine.Werewolf;
using QuillForge.Core.Services;

namespace QuillForge.Web.Services;

public sealed class WerewolfGameEventNarrationComposer : IGameEventNarrationComposer
{
    private readonly DefaultGameEventNarrationComposer _fallback = new();

    public string ComposeSummary(IGameEvent gameEvent)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);

        return gameEvent switch
        {
            WerewolfRoleRevealedEvent revealed => $"Your role is {FormatRole(revealed.Role)}.",
            WerewolfTeamRevealedEvent team => $"Werewolf teammates: {FormatParticipants(team.WerewolfParticipantIds)}.",
            WerewolfStageStartedEvent stage => StageText(stage),
            WerewolfNightActionsResolvedEvent night => $"Night {night.RoundNumber} resolved. Dawn breaks over the table.",
            WerewolfVoteRecordedEvent vote => VoteRecordedText(vote),
            WerewolfVoteResolvedEvent vote => VoteResolvedText(vote),
            WerewolfPlayerEliminatedEvent eliminated => $"{eliminated.ParticipantId.Value} was eliminated and revealed as {FormatRole(eliminated.Role)}.",
            WerewolfWinConditionResolvedEvent win => $"{FormatWinner(win.Winner)} win: {FormatReason(win.ReasonCode)}.",
            _ => _fallback.ComposeSummary(gameEvent),
        };
    }

    private static string StageText(WerewolfStageStartedEvent stage)
    {
        if (stage.StageId == WerewolfConstants.NightStage.StageId)
        {
            return $"Night {stage.RoundNumber} begins. Private role information is active.";
        }

        if (stage.StageId == WerewolfConstants.DayDiscussionStage.StageId)
        {
            return $"Day discussion begins for round {stage.RoundNumber}. Public table talk is open.";
        }

        if (stage.StageId == WerewolfConstants.VotingStage.StageId)
        {
            return $"Voting begins for round {stage.RoundNumber}. Choose an active participant or abstain.";
        }

        return $"Stage {stage.StageId.Value} begins for round {stage.RoundNumber}.";
    }

    private static string VoteRecordedText(WerewolfVoteRecordedEvent vote)
    {
        var target = vote.TargetParticipantId is null ? "abstain" : vote.TargetParticipantId.Value.Value;
        return $"{vote.VoterParticipantId.Value} voted for {target}.";
    }

    private static string VoteResolvedText(WerewolfVoteResolvedEvent vote)
    {
        if (vote.EliminatedParticipantId is null)
        {
            return vote.IsTie
                ? "The vote tied; no one was eliminated."
                : "The village abstained; no one was eliminated.";
        }

        return $"The vote resolved: {vote.EliminatedParticipantId.Value} was selected for elimination.";
    }

    private static string FormatParticipants(IReadOnlyList<ParticipantId> participants) =>
        participants.Count == 0 ? "none" : string.Join(", ", participants.Select(participant => participant.Value).Order(StringComparer.Ordinal));

    private static string FormatRole(WerewolfRole role) => role switch
    {
        WerewolfRole.Werewolf => "Werewolf",
        WerewolfRole.Seer => "Seer",
        _ => "Villager",
    };

    private static string FormatWinner(WerewolfWinner winner) => winner switch
    {
        WerewolfWinner.Werewolves => "Werewolves",
        _ => "Villagers",
    };

    private static string FormatReason(string reasonCode) =>
        string.IsNullOrWhiteSpace(reasonCode) ? "resolved" : reasonCode.Replace('_', ' ');
}
