namespace Den.RulesEngine;

public interface IGameModule
{
    GameModuleDescriptor Descriptor { get; }

    ValidationResult ValidateSetup(GameSetupValidationContext context);

    RulesGameState CreateInitialState(GameSetupInitializationContext context);

    IReadOnlyList<LegalIntentDescriptor> GetLegalIntentDescriptors(RulesGameState state, ParticipantId participantId);

    GameModuleTransitionResult HandleIntentCommand(GameModuleTransitionContext context);

    IReadOnlyList<GameRuleHandlerDescriptor> GetRuleHandlerDescriptors();

    IReadOnlyList<GamePromptAsset> GetPromptAssets();
}

public sealed record GameModuleDescriptor(
    GameModuleId ModuleId,
    GameModuleVersion ModuleVersion,
    GameTemplateVersion MinimumTemplateVersion,
    GameTemplateVersion MaximumTemplateVersion,
    string DisplayName,
    PlayerCountRange PlayerCount,
    IReadOnlyList<GameSetupFieldDescriptor> SetupFields)
{
    public GameCommunicationCapabilities CommunicationCapabilities { get; init; } = new(false, false);

    public GameMemoryExpectations MemoryExpectations { get; init; } = new(false, 0, 0);

    public IReadOnlyList<GamePromptAssetIdentifier> RequiredPromptAssets { get; init; } = [];

    public GameParticipantRequirements ParticipantRequirements { get; init; } = new(true, true, false, 0, 0);

    public GameModuleAuthoringHooks AuthoringHooks { get; init; } = GameModuleAuthoringHooks.Empty;
}

public sealed record GameModuleAuthoringHooks(
    IReadOnlyList<GameStageDescriptor> Stages,
    IReadOnlyList<GameActionFormDescriptor> ActionForms,
    GameProjectionCapabilities ProjectionCapabilities)
{
    public static GameModuleAuthoringHooks Empty { get; } = new([], [], new GameProjectionCapabilities(true, true, true));
}

public sealed record GameStageDescriptor(
    GameStageId StageId,
    string DisplayName,
    string Description,
    int Sequence,
    bool AllowsPublicMessages,
    bool AllowsDirectMessages);

public sealed record GameActionFormDescriptor(
    string IntentName,
    GameStageId StageId,
    string DisplayName,
    string Description,
    GameActionFormLayout Layout,
    IReadOnlyList<GameActionFieldDescriptor> Fields);

public enum GameActionFormLayout
{
    ButtonList,
    SelectOne
}

public sealed record GameActionFieldDescriptor(
    string Name,
    GameActionFieldKind ValueKind,
    bool IsRequired,
    string DisplayName,
    string Description);

public enum GameActionFieldKind
{
    ChoiceName,
    FreeText
}

public sealed record GameProjectionCapabilities(
    bool SupportsPublicEventProjection,
    bool SupportsParticipantPrivateProjection,
    bool SupportsHostInspectorProjection);

public sealed record GameCommunicationCapabilities(
    bool AllowsPublicChannelMessages,
    bool AllowsDirectMessages);

public sealed record GameMemoryExpectations(
    bool UsesRoundSummaries,
    int SuggestedSummaryTokenBudget,
    int MaximumRetainedRoundSummaries);

public sealed record GamePromptAssetIdentifier(
    string AssetId,
    GamePromptAssetKind Kind);

public sealed record GameParticipantRequirements(
    bool AllowsHumanParticipants,
    bool AllowsAgentParticipants,
    bool AllowsSystemParticipants,
    int MinimumHumanParticipants,
    int MinimumAgentParticipants);

public sealed record PlayerCountRange(int Minimum, int Maximum)
{
    public bool Contains(int playerCount) => playerCount >= Minimum && playerCount <= Maximum;
}

public sealed record GameSetupFieldDescriptor(
    string Name,
    GameSetupValueKind ValueKind,
    bool IsRequired,
    string DisplayName,
    string Description);

public enum GameSetupValueKind
{
    String,
    Int,
    Bool,
    ParticipantId,
    ParticipantSet
}

public sealed record GameSetupValidationContext(
    GameModuleDescriptor Descriptor,
    GameSetup Setup,
    IReadOnlyList<ParticipantSetup> Participants,
    GameTemplateVersion TemplateVersion);

public sealed record GameSetupInitializationContext(
    GameInstanceId GameInstanceId,
    GameModuleDescriptor Descriptor,
    GameSetup Setup,
    IReadOnlyList<ParticipantSetup> Participants,
    long Seed);

public sealed record LegalIntentDescriptor(
    string IntentName,
    string DisplayName,
    string Description,
    GameStageId StageId,
    ParticipantId ParticipantId);

public sealed record GameModuleTransitionContext(
    RulesGameState State,
    IGameIntentCommand Command,
    RulesResolutionPhase Phase);

public sealed record GameModuleTransitionResult(
    RulesGameState State,
    IReadOnlyList<IGameEvent> Events,
    IReadOnlyList<ValidationIssue> Issues)
{
    public bool IsAccepted => Issues.Count == 0;

    public static GameModuleTransitionResult Accepted(RulesGameState state, IReadOnlyList<IGameEvent> events) =>
        new(state, events, []);

    public static GameModuleTransitionResult Rejected(RulesGameState state, params ValidationIssue[] issues) =>
        new(state, [], issues);
}

public enum RulesResolutionPhase
{
    CanStart,
    OnRun,
    OnEnd
}

public interface IRulePayload
{
    string PayloadName { get; }
}

public interface IRuleHandler<TPayload>
    where TPayload : IRulePayload
{
    string HandlerName { get; }

    int Priority { get; }

    GameModuleTransitionResult Handle(GameRuleHandlerContext<TPayload> context);
}

public sealed record GameRuleHandlerContext<TPayload>(
    RulesGameState State,
    TPayload Payload,
    RulesResolutionPhase Phase)
    where TPayload : IRulePayload;

public sealed record GameRuleHandlerDescriptor(
    string PayloadName,
    RulesResolutionPhase Phase,
    string HandlerName,
    int Priority);

public sealed record GamePromptAsset(
    string AssetId,
    GamePromptAssetKind Kind,
    string Content);

public enum GamePromptAssetKind
{
    RulesText,
    ParticipantInstructions,
    NarrationTemplate
}
