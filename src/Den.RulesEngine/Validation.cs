namespace Den.RulesEngine;

public sealed record ValidationIssue(string Code, string Message)
{
    public static ValidationIssue Required(string fieldName) =>
        new("required", $"{fieldName} is required.");
}

public sealed record ValidationResult(IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsValid => Issues.Count == 0;

    public static ValidationResult Valid { get; } = new([]);

    public static ValidationResult Invalid(params ValidationIssue[] issues) => new(issues);

    public static ValidationResult FromIssues(IEnumerable<ValidationIssue> issues) => new(issues.ToArray());
}

public sealed record IntentCommandValidationResult(IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsAccepted => Issues.Count == 0;

    public static IntentCommandValidationResult Accepted { get; } = new([]);

    public static IntentCommandValidationResult Rejected(params ValidationIssue[] issues) => new(issues);
}

public static class GameIntentCommandValidationService
{
    public static IntentCommandValidationResult Validate(RulesGameState state, IGameIntentCommand command)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(command);

        if (state.GameInstanceId != command.GameInstanceId)
        {
            return IntentCommandValidationResult.Rejected(new ValidationIssue(
                "wrong_game_instance",
                "Intent command targets a different game instance."));
        }

        return command switch
        {
            SubmitPlayerChoiceIntentCommand submit => ValidateSubmitPlayerChoice(state, submit),
            StartGameIntentCommand => state.Status == RulesGameStatus.NotStarted
                ? IntentCommandValidationResult.Accepted
                : IntentCommandValidationResult.Rejected(new ValidationIssue("game_already_started", "The game has already started.")),
            AdvanceDeterministicEffectsIntentCommand => IntentCommandValidationResult.Accepted,
            RequestPendingInputIntentCommand request => ValidateRequestPendingInput(state, request),
            AdvanceStageIntentCommand => IntentCommandValidationResult.Accepted,
            EndRoundIntentCommand => IntentCommandValidationResult.Accepted,
            EndGameIntentCommand => IntentCommandValidationResult.Accepted,
            AbortGameIntentCommand => IntentCommandValidationResult.Accepted,
            _ => IntentCommandValidationResult.Rejected(new ValidationIssue("unknown_intent_command", "Intent command type is not recognized."))
        };
    }

    public static IntentCommandRejectedEvent ToRejectedEvent(IGameIntentCommand command, IntentCommandValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsAccepted)
        {
            throw new ArgumentException("Accepted commands cannot be converted to rejection events.", nameof(result));
        }

        var issue = result.Issues[0];
        return IntentCommandRejectedEvent.Create(command, issue.Code, issue.Message);
    }

    private static IntentCommandValidationResult ValidateSubmitPlayerChoice(
        RulesGameState state,
        SubmitPlayerChoiceIntentCommand command)
    {
        var participant = state.FindParticipant(command.ParticipantId);
        if (participant is null)
        {
            return IntentCommandValidationResult.Rejected(new ValidationIssue(
                "unknown_participant",
                "Participant is not registered in this game."));
        }

        var pendingInput = state.FindPendingInput(command.PendingInputId);
        if (pendingInput is null)
        {
            return IntentCommandValidationResult.Rejected(new ValidationIssue(
                "unknown_pending_input",
                "Pending input is not registered in this game."));
        }

        if (!pendingInput.IsWaitingFor(command.ParticipantId))
        {
            return IntentCommandValidationResult.Rejected(new ValidationIssue(
                "pending_input_not_available",
                "Pending input is not waiting for this participant."));
        }

        if (pendingInput.StageId != state.Stage.StageId)
        {
            return IntentCommandValidationResult.Rejected(new ValidationIssue(
                "out_of_stage",
                "Pending input belongs to a previous or future stage."));
        }

        if (!pendingInput.LegalOptions.Any(option => string.Equals(option.IntentName, command.ChoiceName, StringComparison.Ordinal)))
        {
            return IntentCommandValidationResult.Rejected(new ValidationIssue(
                "illegal_choice",
                "Choice is not legal for the pending input."));
        }

        return IntentCommandValidationResult.Accepted;
    }

    private static IntentCommandValidationResult ValidateRequestPendingInput(
        RulesGameState state,
        RequestPendingInputIntentCommand command)
    {
        if (command.LegalOptions.Count == 0)
        {
            return IntentCommandValidationResult.Rejected(new ValidationIssue(
                "missing_legal_options",
                "Pending input requests must provide at least one legal option."));
        }

        var targets = PendingInputAudienceResolver.Resolve(state, command.Audience);

        if (targets.Count == 0)
        {
            return IntentCommandValidationResult.Rejected(new ValidationIssue(
                "missing_pending_input_targets",
                "Pending input requests must target at least one participant."));
        }

        foreach (var participantId in targets)
        {
            if (state.FindParticipant(participantId) is null)
            {
                return IntentCommandValidationResult.Rejected(new ValidationIssue(
                    "unknown_participant",
                    "Pending input request targets a participant that is not registered in this game."));
            }
        }

        return IntentCommandValidationResult.Accepted;
    }
}
