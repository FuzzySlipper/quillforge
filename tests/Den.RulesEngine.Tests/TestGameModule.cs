namespace Den.RulesEngine.Tests;

internal sealed class TestGameModule : IGameModule
{
    public GameModuleDescriptor Descriptor { get; } = CreateDescriptor();

    public bool RejectSetup { get; init; }

    public static GameModuleDescriptor CreateDescriptor() =>
        new(
            new GameModuleId("test-module"),
            new GameModuleVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            "Test Module",
            new PlayerCountRange(2, 8),
            [new GameSetupFieldDescriptor("scenario", GameSetupValueKind.String, true, "Scenario", "Scenario name.")]);

    public ValidationResult ValidateSetup(GameSetupValidationContext context)
    {
        return RejectSetup
            ? ValidationResult.Invalid(new ValidationIssue("module_setup_rejected", "Setup rejected by module."))
            : ValidationResult.Valid;
    }

    public RulesGameState CreateInitialState(GameSetupInitializationContext context)
    {
        var participants = context.Participants
            .Select(participant => new ParticipantState(
                participant.ParticipantId,
                participant.DisplayName,
                participant.Kind,
                []))
            .ToArray();

        return RulesGameState.CreateNotStarted(
            context.GameInstanceId,
            context.Descriptor,
            context.Seed,
            participants);
    }

    public IReadOnlyList<LegalIntentDescriptor> GetLegalIntentDescriptors(RulesGameState state, ParticipantId participantId)
    {
        return [new LegalIntentDescriptor("choose", "Choose", "Choose an option.", state.Stage.StageId, participantId)];
    }

    public GameModuleTransitionResult HandleIntentCommand(GameModuleTransitionContext context)
    {
        return GameModuleTransitionResult.Accepted(context.State, []);
    }

    public IReadOnlyList<GameRuleHandlerDescriptor> GetRuleHandlerDescriptors()
    {
        return [new GameRuleHandlerDescriptor("test-payload", RulesResolutionPhase.OnRun, "TestHandler", 0)];
    }

    public IReadOnlyList<GamePromptAsset> GetPromptAssets()
    {
        return [new GamePromptAsset("rules", GamePromptAssetKind.RulesText, "Test rules.")];
    }
}
