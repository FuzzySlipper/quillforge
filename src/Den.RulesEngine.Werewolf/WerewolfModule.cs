using Den.RulesEngine;

namespace Den.RulesEngine.Werewolf;

public sealed class WerewolfModule : IGameModule
{
    public GameModuleDescriptor Descriptor { get; } = CreateDescriptor();

    public IReadOnlyList<WerewolfRoleDefinition> RoleDefinitions { get; } =
    [
        new(WerewolfRole.Werewolf, WerewolfConstants.WerewolfRoleSetId, WerewolfConstants.WerewolfTeamSetId, "Werewolf", "Hidden antagonist. Werewolves win if they reach parity with non-werewolves."),
        new(WerewolfRole.Villager, WerewolfConstants.VillagerRoleSetId, WerewolfConstants.VillageTeamSetId, "Villager", "Village-aligned participant. Villagers win by eliminating all werewolves."),
        new(WerewolfRole.Seer, WerewolfConstants.SeerRoleSetId, WerewolfConstants.VillageTeamSetId, "Seer", "Village-aligned optional information role reserved for follow-up night action expansion.")
    ];

    public ValidationResult ValidateSetup(GameSetupValidationContext context)
    {
        var issues = new List<ValidationIssue>();
        var werewolfCount = GetInt(context.Setup, WerewolfConstants.WerewolfCountSetupField, 1);
        var seerEnabled = GetBool(context.Setup, WerewolfConstants.SeerEnabledSetupField, false);

        if (werewolfCount < 1)
        {
            issues.Add(new ValidationIssue("invalid_werewolf_count", "Werewolf count must be at least one."));
        }

        if (werewolfCount >= context.Participants.Count)
        {
            issues.Add(new ValidationIssue("invalid_werewolf_count", "Werewolf count must leave at least one non-werewolf participant."));
        }

        if (seerEnabled && werewolfCount + 1 > context.Participants.Count)
        {
            issues.Add(new ValidationIssue("invalid_role_mix", "A seer role requires at least one non-werewolf slot."));
        }

        return ValidationResult.FromIssues(issues);
    }

    public RulesGameState CreateInitialState(GameSetupInitializationContext context)
    {
        var werewolfCount = GetInt(context.Setup, WerewolfConstants.WerewolfCountSetupField, 1);
        var seerEnabled = GetBool(context.Setup, WerewolfConstants.SeerEnabledSetupField, false);
        var random = DeterministicRandomState.Create(context.Seed);
        var seating = context.Participants.ToArray();
        Shuffle(seating, ref random);

        var roleByParticipant = AssignRoles(seating, werewolfCount, seerEnabled);
        var participants = context.Participants
            .Select(participant => new ParticipantState(
                participant.ParticipantId,
                participant.DisplayName,
                participant.Kind,
                ParticipantSetsFor(roleByParticipant[participant.ParticipantId]),
                IsActive: true))
            .ToArray();

        return RulesGameState.CreateNotStarted(
            context.GameInstanceId,
            context.Descriptor,
            context.Seed,
            participants) with
        {
            Random = random,
            Round = new GameRoundState(1),
            Stage = WerewolfConstants.NightStage
        };
    }

    public IReadOnlyList<LegalIntentDescriptor> GetLegalIntentDescriptors(RulesGameState state, ParticipantId participantId)
    {
        var participant = state.FindParticipant(participantId);
        if (participant is null || !participant.IsActive)
        {
            return [];
        }

        if (state.Stage.StageId == WerewolfConstants.NightStage.StageId)
        {
            return [new LegalIntentDescriptor(WerewolfConstants.SkipNightChoice, "Skip night", "Continue after private role information is revealed.", state.Stage.StageId, participantId)];
        }

        if (state.Stage.StageId == WerewolfConstants.VotingStage.StageId)
        {
            return state.Participants
                .Where(candidate => candidate.IsActive)
                .Select(candidate => new LegalIntentDescriptor(
                    candidate.ParticipantId.Value,
                    $"Vote {candidate.DisplayName}",
                    $"Vote to eliminate {candidate.DisplayName}.",
                    state.Stage.StageId,
                    participantId))
                .Append(new LegalIntentDescriptor(WerewolfConstants.AbstainChoice, "Abstain", "Do not vote to eliminate anyone.", state.Stage.StageId, participantId))
                .ToArray();
        }

        return [];
    }

    public GameModuleTransitionResult HandleIntentCommand(GameModuleTransitionContext context)
    {
        if (context.Phase == RulesResolutionPhase.CanStart)
        {
            return ValidateCommand(context);
        }

        if (context.Phase != RulesResolutionPhase.OnRun)
        {
            return GameModuleTransitionResult.Accepted(context.State, []);
        }

        return context.Command switch
        {
            StartGameIntentCommand => StartGame(context.State),
            SubmitPlayerChoiceIntentCommand submit when context.State.Stage.StageId == WerewolfConstants.NightStage.StageId => ResolveNightIfReady(context.State),
            SubmitPlayerChoiceIntentCommand submit when context.State.Stage.StageId == WerewolfConstants.VotingStage.StageId => RecordVoteAndResolveIfReady(context.State, submit),
            AdvanceStageIntentCommand advance when advance.NextStage.StageId == WerewolfConstants.VotingStage.StageId => GameModuleTransitionResult.Accepted(context.State, [WerewolfStageStartedEvent.Create(context.State.GameInstanceId, WerewolfConstants.VotingStage.StageId, context.State.Round.RoundNumber)]),
            _ => GameModuleTransitionResult.Accepted(context.State, [])
        };
    }

    public IReadOnlyList<GameRuleHandlerDescriptor> GetRuleHandlerDescriptors() =>
    [
        new GameRuleHandlerDescriptor(nameof(StartGameIntentCommand), RulesResolutionPhase.OnRun, nameof(WerewolfModule), 0),
        new GameRuleHandlerDescriptor(nameof(SubmitPlayerChoiceIntentCommand), RulesResolutionPhase.CanStart, nameof(WerewolfModule), 0),
        new GameRuleHandlerDescriptor(nameof(SubmitPlayerChoiceIntentCommand), RulesResolutionPhase.OnRun, nameof(WerewolfModule), 0)
    ];

    public IReadOnlyList<GamePromptAsset> GetPromptAssets() =>
    [
        new GamePromptAsset("werewolf-rules", GamePromptAssetKind.RulesText, "Baseline Werewolf: hidden werewolves try to survive the village vote; villagers win by eliminating all werewolves."),
        new GamePromptAsset("werewolf-participant-instructions", GamePromptAssetKind.ParticipantInstructions, "Use only visible facts. Do not claim private role information unless your participant feed revealed it."),
        new GamePromptAsset("werewolf-one-night-follow-up", GamePromptAssetKind.RulesText, "One Night-compatible setup is represented by the one_night_compatible option. Center-card roles and One Night-specific night order remain follow-up module work; v1 uses baseline Werewolf resolution.")
    ];

    private static GameModuleDescriptor CreateDescriptor() =>
        new(
            WerewolfModuleAssemblyMarker.ModuleId,
            WerewolfModuleAssemblyMarker.ModuleVersion,
            new GameTemplateVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            "Werewolf",
            new PlayerCountRange(3, 12),
            [
                new GameSetupFieldDescriptor(WerewolfConstants.WerewolfCountSetupField, GameSetupValueKind.Int, true, "Werewolf count", "Number of werewolf roles in the deck."),
                new GameSetupFieldDescriptor(WerewolfConstants.SeerEnabledSetupField, GameSetupValueKind.Bool, false, "Enable seer", "Adds one seer role before filling remaining seats with villagers."),
                new GameSetupFieldDescriptor(WerewolfConstants.OneNightCompatibleSetupField, GameSetupValueKind.Bool, false, "One Night compatible", "Documents One Night-compatible setup intent; specialized center-card rules are follow-up work.")
            ])
        {
            CommunicationCapabilities = new GameCommunicationCapabilities(true, true),
            MemoryExpectations = new GameMemoryExpectations(true, 512, 8),
            RequiredPromptAssets =
            [
                new GamePromptAssetIdentifier("werewolf-rules", GamePromptAssetKind.RulesText),
                new GamePromptAssetIdentifier("werewolf-participant-instructions", GamePromptAssetKind.ParticipantInstructions)
            ],
            ParticipantRequirements = new GameParticipantRequirements(true, true, false, 1, 0)
        };

    private static GameModuleTransitionResult ValidateCommand(GameModuleTransitionContext context)
    {
        if (context.Command is not SubmitPlayerChoiceIntentCommand submit)
        {
            return GameModuleTransitionResult.Accepted(context.State, []);
        }

        if (context.State.Stage.StageId == WerewolfConstants.NightStage.StageId)
        {
            return submit.ChoiceName == WerewolfConstants.SkipNightChoice
                ? GameModuleTransitionResult.Accepted(context.State, [])
                : GameModuleTransitionResult.Rejected(context.State, new ValidationIssue("invalid_night_action", "Baseline Werewolf night action must be skip-night."));
        }

        if (context.State.Stage.StageId == WerewolfConstants.VotingStage.StageId)
        {
            if (submit.ChoiceName == WerewolfConstants.AbstainChoice)
            {
                return GameModuleTransitionResult.Accepted(context.State, []);
            }

            var target = context.State.FindParticipant(new ParticipantId(submit.ChoiceName));
            return target is not null && target.IsActive
                ? GameModuleTransitionResult.Accepted(context.State, [])
                : GameModuleTransitionResult.Rejected(context.State, new ValidationIssue("invalid_vote_target", "Vote target must be an active participant."));
        }

        return GameModuleTransitionResult.Rejected(context.State, new ValidationIssue("invalid_stage_action", "Werewolf module does not accept player choices in the current stage."));
    }

    private static GameModuleTransitionResult StartGame(RulesGameState state)
    {
        var events = new List<IGameEvent>();
        foreach (var participant in state.Participants)
        {
            var role = RoleFor(participant);
            events.Add(WerewolfRoleAssignedEvent.Create(state.GameInstanceId, participant.ParticipantId, role));
            events.Add(WerewolfRoleRevealedEvent.Create(state.GameInstanceId, participant.ParticipantId, role));
        }

        var werewolves = state.Participants
            .Where(IsWerewolf)
            .Select(participant => participant.ParticipantId)
            .ToArray();
        if (werewolves.Length > 0)
        {
            events.Add(WerewolfTeamRevealedEvent.Create(state.GameInstanceId, werewolves));
        }

        events.Add(WerewolfStageStartedEvent.Create(state.GameInstanceId, state.Stage.StageId, state.Round.RoundNumber));
        return GameModuleTransitionResult.Accepted(state, events);
    }

    private static GameModuleTransitionResult ResolveNightIfReady(RulesGameState state)
    {
        if (!AllPendingInputsForStageSubmitted(state, WerewolfConstants.NightStage.StageId))
        {
            return GameModuleTransitionResult.Accepted(state, []);
        }

        var next = state with
        {
            Status = RulesGameStatus.Running,
            Stage = WerewolfConstants.DayDiscussionStage,
            PendingInputs = RemovePendingInputsForStage(state, WerewolfConstants.NightStage.StageId)
        };

        return GameModuleTransitionResult.Accepted(next,
        [
            WerewolfNightActionsResolvedEvent.Create(state.GameInstanceId, state.Round.RoundNumber),
            WerewolfStageStartedEvent.Create(state.GameInstanceId, WerewolfConstants.DayDiscussionStage.StageId, state.Round.RoundNumber)
        ]);
    }

    private static GameModuleTransitionResult RecordVoteAndResolveIfReady(RulesGameState state, SubmitPlayerChoiceIntentCommand submit)
    {
        var target = submit.ChoiceName == WerewolfConstants.AbstainChoice
            ? (ParticipantId?)null
            : new ParticipantId(submit.ChoiceName);
        var events = new List<IGameEvent>
        {
            WerewolfVoteRecordedEvent.Create(state.GameInstanceId, submit.ParticipantId, target)
        };

        if (!AllPendingInputsForStageSubmitted(state, WerewolfConstants.VotingStage.StageId))
        {
            return GameModuleTransitionResult.Accepted(state, events);
        }

        var votes = SubmittedChoicesForStage(state, WerewolfConstants.VotingStage.StageId)
            .Where(choice => choice.ChoiceName != WerewolfConstants.AbstainChoice)
            .GroupBy(choice => choice.ChoiceName)
            .Select(group => new VoteCount(new ParticipantId(group.Key), group.Count()))
            .OrderByDescending(vote => vote.Count)
            .ThenBy(vote => vote.ParticipantId.Value, StringComparer.Ordinal)
            .ToArray();

        if (votes.Length == 0 || (votes.Length > 1 && votes[0].Count == votes[1].Count))
        {
            var nextNight = AdvanceToNextNight(state);
            events.Add(WerewolfVoteResolvedEvent.Create(state.GameInstanceId, null, isTie: votes.Length > 1));
            events.Add(WerewolfStageStartedEvent.Create(state.GameInstanceId, WerewolfConstants.NightStage.StageId, nextNight.Round.RoundNumber));
            return GameModuleTransitionResult.Accepted(nextNight, events);
        }

        var eliminatedId = votes[0].ParticipantId;
        var eliminated = state.FindParticipant(eliminatedId)
            ?? throw new InvalidOperationException("Validated vote target disappeared before resolution.");
        var eliminatedRole = RoleFor(eliminated);
        var afterElimination = state with
        {
            PendingInputs = RemovePendingInputsForStage(state, WerewolfConstants.VotingStage.StageId),
            Participants = state.Participants
                .Select(participant => participant.ParticipantId == eliminatedId ? participant with { IsActive = false } : participant)
                .ToArray()
        };

        events.Add(WerewolfVoteResolvedEvent.Create(state.GameInstanceId, eliminatedId, isTie: false));
        events.Add(WerewolfPlayerEliminatedEvent.Create(state.GameInstanceId, eliminatedId, eliminatedRole));

        var winner = DetermineWinner(afterElimination);
        if (winner is not null)
        {
            var outcomeName = winner == WerewolfWinner.Villagers ? "villagers_win" : "werewolves_win";
            var reasonCode = winner == WerewolfWinner.Villagers ? "all_werewolves_eliminated" : "werewolves_reached_parity";
            var ended = afterElimination with { Status = RulesGameStatus.Ended };
            events.Add(WerewolfWinConditionResolvedEvent.Create(state.GameInstanceId, winner.Value, reasonCode));
            events.Add(GameEndedEvent.Create(state.GameInstanceId, outcomeName));
            return GameModuleTransitionResult.Accepted(ended, events);
        }

        var next = AdvanceToNextNight(afterElimination);
        events.Add(WerewolfStageStartedEvent.Create(state.GameInstanceId, WerewolfConstants.NightStage.StageId, next.Round.RoundNumber));
        return GameModuleTransitionResult.Accepted(next, events);
    }

    private static RulesGameState AdvanceToNextNight(RulesGameState state) =>
        state with
        {
            Status = RulesGameStatus.Running,
            Round = new GameRoundState(state.Round.RoundNumber + 1),
            Stage = WerewolfConstants.NightStage,
            PendingInputs = RemovePendingInputsForStage(state, WerewolfConstants.VotingStage.StageId)
        };

    private static bool AllPendingInputsForStageSubmitted(RulesGameState state, GameStageId stageId)
    {
        var stageInputs = state.PendingInputs.Where(input => input.StageId == stageId).ToArray();
        return stageInputs.Length > 0 && stageInputs.All(input => input.Status == PendingInputStatus.Submitted);
    }

    private static PendingInputState[] RemovePendingInputsForStage(RulesGameState state, GameStageId stageId) =>
        state.PendingInputs.Where(input => input.StageId != stageId).ToArray();

    private static PlayerChoiceSubmittedEvent[] SubmittedChoicesForStage(RulesGameState state, GameStageId stageId)
    {
        var pendingIds = state.PendingInputs
            .Where(input => input.StageId == stageId)
            .Select(input => input.PendingInputId)
            .ToArray();

        return state.EventJournal.Events
            .OfType<PlayerChoiceSubmittedEvent>()
            .Where(choice => pendingIds.Contains(choice.PendingInputId))
            .ToArray();
    }

    private static WerewolfWinner? DetermineWinner(RulesGameState state)
    {
        var activeWerewolves = state.Participants.Count(participant => participant.IsActive && IsWerewolf(participant));
        var activeNonWerewolves = state.Participants.Count(participant => participant.IsActive && !IsWerewolf(participant));

        if (activeWerewolves == 0)
        {
            return WerewolfWinner.Villagers;
        }

        if (activeWerewolves >= activeNonWerewolves)
        {
            return WerewolfWinner.Werewolves;
        }

        return null;
    }

    private static Dictionary<ParticipantId, WerewolfRole> AssignRoles(
        IReadOnlyList<ParticipantSetup> seating,
        int werewolfCount,
        bool seerEnabled)
    {
        var roles = new List<WerewolfRole>();
        for (var index = 0; index < werewolfCount; index++)
        {
            roles.Add(WerewolfRole.Werewolf);
        }

        if (seerEnabled)
        {
            roles.Add(WerewolfRole.Seer);
        }

        while (roles.Count < seating.Count)
        {
            roles.Add(WerewolfRole.Villager);
        }

        var result = new Dictionary<ParticipantId, WerewolfRole>();
        for (var index = 0; index < seating.Count; index++)
        {
            result[seating[index].ParticipantId] = roles[index];
        }

        return result;
    }

    private static void Shuffle(ParticipantSetup[] seating, ref DeterministicRandomState random)
    {
        for (var index = seating.Length - 1; index > 0; index--)
        {
            var draw = random.NextInt(index + 1);
            random = draw.State;
            (seating[index], seating[draw.Value]) = (seating[draw.Value], seating[index]);
        }
    }

    private static IReadOnlyList<ParticipantSetId> ParticipantSetsFor(WerewolfRole role) =>
        role switch
        {
            WerewolfRole.Werewolf => [WerewolfConstants.WerewolfRoleSetId, WerewolfConstants.WerewolfTeamSetId],
            WerewolfRole.Seer => [WerewolfConstants.SeerRoleSetId, WerewolfConstants.VillageTeamSetId],
            _ => [WerewolfConstants.VillagerRoleSetId, WerewolfConstants.VillageTeamSetId]
        };

    private static WerewolfRole RoleFor(ParticipantState participant)
    {
        if (participant.ParticipantSetIds.Contains(WerewolfConstants.WerewolfRoleSetId))
        {
            return WerewolfRole.Werewolf;
        }

        if (participant.ParticipantSetIds.Contains(WerewolfConstants.SeerRoleSetId))
        {
            return WerewolfRole.Seer;
        }

        return WerewolfRole.Villager;
    }

    private static bool IsWerewolf(ParticipantState participant) =>
        participant.ParticipantSetIds.Contains(WerewolfConstants.WerewolfRoleSetId);

    private static int GetInt(GameSetup setup, string name, int defaultValue) =>
        setup.FindValue(name) is IntGameSetupValue value ? value.Value : defaultValue;

    private static bool GetBool(GameSetup setup, string name, bool defaultValue) =>
        setup.FindValue(name) is BoolGameSetupValue value ? value.Value : defaultValue;

    private sealed record VoteCount(ParticipantId ParticipantId, int Count);
}
