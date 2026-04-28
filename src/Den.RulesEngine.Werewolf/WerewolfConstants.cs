using Den.RulesEngine;

namespace Den.RulesEngine.Werewolf;

public static class WerewolfConstants
{
    public static GameStageState NightStage { get; } = new(new GameStageId("night"), "Night", 1, false, true);

    public static GameStageState DayDiscussionStage { get; } = new(new GameStageId("day-discussion"), "Day discussion", 2, true, false);

    public static GameStageState VotingStage { get; } = new(new GameStageId("voting"), "Voting", 3, false, false);

    public const string WerewolfCountSetupField = "werewolf_count";

    public const string SeerEnabledSetupField = "seer_enabled";

    public const string OneNightCompatibleSetupField = "one_night_compatible";

    public const string NightActionIntentName = "night-action";

    public const string VoteIntentName = "vote";

    public const string SkipNightChoice = "skip-night";

    public const string AbstainChoice = "abstain";

    public static ParticipantSetId WerewolfTeamSetId { get; } = new("team:werewolves");

    public static ParticipantSetId VillageTeamSetId { get; } = new("team:village");

    public static ParticipantSetId WerewolfRoleSetId { get; } = new("role:werewolf");

    public static ParticipantSetId VillagerRoleSetId { get; } = new("role:villager");

    public static ParticipantSetId SeerRoleSetId { get; } = new("role:seer");
}

public enum WerewolfRole
{
    Villager,
    Werewolf,
    Seer
}

public enum WerewolfWinner
{
    Villagers,
    Werewolves
}

public sealed record WerewolfRoleDefinition(
    WerewolfRole Role,
    ParticipantSetId RoleSetId,
    ParticipantSetId TeamSetId,
    string DisplayName,
    string Description);
