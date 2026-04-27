namespace Den.RulesEngine.Tests;

public sealed class GameIntentCommandValidationTests
{
    [Fact]
    public void Validate_SubmitPlayerChoice_RejectsUnknownPendingInput()
    {
        var state = CreateState(waitingInput: null);
        var command = new SubmitPlayerChoiceIntentCommand(
            GameIntentCommandId.NewId(),
            state.GameInstanceId,
            new PendingInputId("missing"),
            new ParticipantId("alice"),
            "choose");

        var result = GameIntentCommandValidationService.Validate(state, command);

        Assert.False(result.IsAccepted);
        Assert.Equal("unknown_pending_input", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void Validate_SubmitPlayerChoice_RejectsIllegalChoice()
    {
        var input = new PendingInputState(
            new PendingInputId("input-1"),
            new ParticipantId("alice"),
            GameStageState.NotStarted.StageId,
            "choose",
            PendingInputStatus.Waiting,
            [new LegalIntentOption("choose", "Choose", "Choose.")]);
        var state = CreateState(input);
        var command = new SubmitPlayerChoiceIntentCommand(
            GameIntentCommandId.NewId(),
            state.GameInstanceId,
            input.PendingInputId,
            new ParticipantId("alice"),
            "not-legal");

        var result = GameIntentCommandValidationService.Validate(state, command);

        Assert.False(result.IsAccepted);
        Assert.Equal("illegal_choice", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void Validate_RequestPendingInput_RejectsMissingLegalOptions()
    {
        var state = CreateState(waitingInput: null);
        var command = new RequestPendingInputIntentCommand(
            GameIntentCommandId.NewId(),
            state.GameInstanceId,
            GameStageState.NotStarted.StageId,
            "choose",
            [],
            PendingInputAudience.One(new ParticipantId("alice")));

        var result = GameIntentCommandValidationService.Validate(state, command);

        Assert.False(result.IsAccepted);
        Assert.Equal("missing_legal_options", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void Validate_RequestPendingInput_RejectsMissingTargets()
    {
        var state = CreateState(waitingInput: null);
        var command = new RequestPendingInputIntentCommand(
            GameIntentCommandId.NewId(),
            state.GameInstanceId,
            GameStageState.NotStarted.StageId,
            "choose",
            [new LegalIntentOption("choose", "Choose", "Choose.")],
            PendingInputAudience.Many([]));

        var result = GameIntentCommandValidationService.Validate(state, command);

        Assert.False(result.IsAccepted);
        Assert.Equal("missing_pending_input_targets", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void Validate_RequestPendingInput_RejectsUnknownParticipantTargets()
    {
        var state = CreateState(waitingInput: null);
        var command = new RequestPendingInputIntentCommand(
            GameIntentCommandId.NewId(),
            state.GameInstanceId,
            GameStageState.NotStarted.StageId,
            "choose",
            [new LegalIntentOption("choose", "Choose", "Choose.")],
            PendingInputAudience.One(new ParticipantId("missing")));

        var result = GameIntentCommandValidationService.Validate(state, command);

        Assert.False(result.IsAccepted);
        Assert.Equal("unknown_participant", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void ToRejectedEvent_CreatesTypedPastTenseFactForInvalidCommand()
    {
        var state = CreateState(waitingInput: null);
        var command = new SubmitPlayerChoiceIntentCommand(
            GameIntentCommandId.NewId(),
            state.GameInstanceId,
            new PendingInputId("missing"),
            new ParticipantId("alice"),
            "choose");
        var result = GameIntentCommandValidationService.Validate(state, command);

        var rejected = GameIntentCommandValidationService.ToRejectedEvent(command, result);
        var journal = state.EventJournal.Append(rejected);

        var committed = Assert.IsType<IntentCommandRejectedEvent>(Assert.Single(journal.Events));
        Assert.Equal(command.CommandId, committed.CommandId);
        Assert.Equal("unknown_pending_input", committed.ReasonCode);
        Assert.Equal(1, committed.Sequence);
    }

    private static RulesGameState CreateState(PendingInputState? waitingInput)
    {
        var descriptor = TestGameModule.CreateDescriptor();
        var state = RulesGameState.CreateNotStarted(
            new GameInstanceId("game-1"),
            descriptor,
            seed: 99,
            [ParticipantState.Human(new ParticipantId("alice"), "Alice")]);

        return waitingInput is null ? state : state.WithPendingInputs([waitingInput]);
    }
}
