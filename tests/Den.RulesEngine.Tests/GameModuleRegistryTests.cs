namespace Den.RulesEngine.Tests;

public sealed class GameModuleRegistryTests
{
    [Fact]
    public void EmptyAuthoringHooks_DefaultToNoProjectionCapabilities()
    {
        Assert.False(GameModuleAuthoringHooks.Empty.ProjectionCapabilities.SupportsPublicEventProjection);
        Assert.False(GameModuleAuthoringHooks.Empty.ProjectionCapabilities.SupportsParticipantPrivateProjection);
        Assert.False(GameModuleAuthoringHooks.Empty.ProjectionCapabilities.SupportsHostInspectorProjection);
    }

    [Fact]
    public void Register_StoresExplicitModulesByIdAndVersion()
    {
        var registry = new GameModuleRegistry();
        var module = new TestGameModule();

        var result = registry.Register(module);

        Assert.True(result.IsValid);
        Assert.Same(module, registry.Find(module.Descriptor.ModuleId, module.Descriptor.ModuleVersion));
        Assert.Single(registry.Modules);
    }

    [Fact]
    public void Register_AllowsSameModuleIdWithDifferentVersions()
    {
        var registry = new GameModuleRegistry();
        var first = CreateModule(CreateDescriptor(new GameModuleVersion("1.0.0")));
        var second = CreateModule(CreateDescriptor(new GameModuleVersion("1.1.0")));

        var firstResult = registry.Register(first);
        var secondResult = registry.Register(second);

        Assert.True(firstResult.IsValid);
        Assert.True(secondResult.IsValid);
        Assert.Same(first, registry.Find(first.Descriptor.ModuleId, first.Descriptor.ModuleVersion));
        Assert.Same(second, registry.Find(second.Descriptor.ModuleId, second.Descriptor.ModuleVersion));
        Assert.Equal(2, registry.Modules.Count);
    }

    [Fact]
    public void Register_RejectsDuplicateModuleIdsAndVersions()
    {
        var registry = new GameModuleRegistry();
        var first = new TestGameModule();
        var second = new TestGameModule();

        registry.Register(first);
        var result = registry.Register(second);

        Assert.False(result.IsValid);
        Assert.Equal("duplicate_module_id", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void RegistryFactory_RegistersExplicitModulesWithoutScanning()
    {
        var modules = new IGameModule[]
        {
            CreateModule(CreateDescriptor(new GameModuleVersion("1.0.0"))),
            CreateModule(CreateDescriptor(new GameModuleVersion("2.0.0")))
        };

        var result = new GameModuleRegistryFactory().Create(modules);

        Assert.True(result.ValidationResult.IsValid);
        Assert.Equal(2, result.Registry.Modules.Count);
        Assert.Same(modules[0], result.Registry.Find(modules[0].Descriptor.ModuleId, modules[0].Descriptor.ModuleVersion));
        Assert.Same(modules[1], result.Registry.Find(modules[1].Descriptor.ModuleId, modules[1].Descriptor.ModuleVersion));
    }

    [Fact]
    public void GameSetupValidation_RejectsUnknownModule()
    {
        var service = new GameSetupValidationService(new GameModuleRegistry());

        var result = service.Validate(
            new GameModuleId("missing"),
            new GameModuleVersion("1.0.0"),
            new GameTemplateVersion("1.0.0"),
            GameSetup.Empty,
            CreateParticipants());

        Assert.False(result.IsValid);
        Assert.Equal("unknown_module_id", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void GameSetupValidation_RejectsUnknownModuleVersion()
    {
        var registry = new GameModuleRegistry();
        var module = new TestGameModule();
        registry.Register(module);
        var service = new GameSetupValidationService(registry);

        var result = service.Validate(
            module.Descriptor.ModuleId,
            new GameModuleVersion("9.9.9"),
            new GameTemplateVersion("1.0.0"),
            new GameSetup([new StringGameSetupValue("scenario", "baseline")]),
            CreateParticipants());

        Assert.False(result.IsValid);
        Assert.Equal("unknown_module_version", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void GameSetupValidation_UsesRegisteredModuleContracts()
    {
        var registry = new GameModuleRegistry();
        var module = new TestGameModule();
        registry.Register(module);
        var service = new GameSetupValidationService(registry);

        var result = service.Validate(
            module.Descriptor.ModuleId,
            module.Descriptor.ModuleVersion,
            new GameTemplateVersion("1.0.0"),
            new GameSetup([new StringGameSetupValue("scenario", "baseline")]),
            CreateParticipants());

        Assert.True(result.IsValid);
    }

    [Fact]
    public void GameSetupValidation_RejectsIncompatibleTemplateVersion()
    {
        var registry = new GameModuleRegistry();
        var module = new TestGameModule();
        registry.Register(module);
        var service = new GameSetupValidationService(registry);

        var result = service.Validate(
            module.Descriptor.ModuleId,
            module.Descriptor.ModuleVersion,
            new GameTemplateVersion("2.0.0"),
            new GameSetup([new StringGameSetupValue("scenario", "baseline")]),
            CreateParticipants());

        Assert.False(result.IsValid);
        Assert.Equal("incompatible_template_version", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void GameSetupValidation_RejectsMissingRequiredSetupFieldsBeforeModuleInitialization()
    {
        var registry = new GameModuleRegistry();
        var module = new TestGameModule();
        registry.Register(module);
        var service = new GameSetupValidationService(registry);

        var result = service.Validate(
            module.Descriptor.ModuleId,
            module.Descriptor.ModuleVersion,
            new GameTemplateVersion("1.0.0"),
            GameSetup.Empty,
            CreateParticipants());

        Assert.False(result.IsValid);
        Assert.Equal("required", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void GameSetupValidation_RejectsIncompatibleSetupOptionTypes()
    {
        var registry = new GameModuleRegistry();
        var module = new TestGameModule();
        registry.Register(module);
        var service = new GameSetupValidationService(registry);

        var result = service.Validate(
            module.Descriptor.ModuleId,
            module.Descriptor.ModuleVersion,
            new GameTemplateVersion("1.0.0"),
            new GameSetup([new IntGameSetupValue("scenario", 5)]),
            CreateParticipants());

        Assert.False(result.IsValid);
        Assert.Equal("incompatible_setup_option", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void GameSetupValidation_RejectsUnsupportedRoleMixes()
    {
        var descriptor = TestGameModule.CreateDescriptor() with
        {
            ParticipantRequirements = new GameParticipantRequirements(
                AllowsHumanParticipants: true,
                AllowsAgentParticipants: true,
                AllowsSystemParticipants: false,
                MinimumHumanParticipants: 2,
                MinimumAgentParticipants: 1)
        };
        var module = CreateModule(descriptor);
        var registry = new GameModuleRegistry();
        registry.Register(module);
        var service = new GameSetupValidationService(registry);

        var result = service.Validate(
            module.Descriptor.ModuleId,
            module.Descriptor.ModuleVersion,
            new GameTemplateVersion("1.0.0"),
            new GameSetup([new StringGameSetupValue("scenario", "baseline")]),
            CreateParticipants());

        Assert.False(result.IsValid);
        Assert.Equal("unsupported_role_mix", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void GameSetupValidation_RejectsMissingRequiredModuleAssets()
    {
        var module = CreateModule(TestGameModule.CreateDescriptor(), promptAssets: []);
        var registry = new GameModuleRegistry();
        registry.Register(module);
        var service = new GameSetupValidationService(registry);

        var result = service.Validate(
            module.Descriptor.ModuleId,
            module.Descriptor.ModuleVersion,
            new GameTemplateVersion("1.0.0"),
            new GameSetup([new StringGameSetupValue("scenario", "baseline")]),
            CreateParticipants());

        Assert.False(result.IsValid);
        Assert.Equal("missing_required_module_asset", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void CanLoad_AcceptsRegisteredModuleWithinVersionRange()
    {
        var registry = new GameModuleRegistry();
        var older = CreateModule(CreateDescriptor(new GameModuleVersion("1.0.0")));
        var newer = CreateModule(CreateDescriptor(new GameModuleVersion("1.2.0")));
        registry.Register(older);
        registry.Register(newer);

        var request = new GameModuleLoadRequest(
            older.Descriptor.ModuleId,
            new GameModuleVersionRange(new GameModuleVersion("1.1.0"), new GameModuleVersion("1.3.0")),
            new GameTemplateVersion("1.0.0"));

        var result = registry.CanLoad(request);
        var module = registry.FindLoadable(request);

        Assert.True(result.IsValid);
        Assert.Same(newer, module);
    }

    [Fact]
    public void CanLoad_RejectsModuleVersionMismatches()
    {
        var registry = new GameModuleRegistry();
        var module = CreateModule(CreateDescriptor(new GameModuleVersion("1.0.0")));
        registry.Register(module);

        var result = registry.CanLoad(new GameModuleLoadRequest(
            module.Descriptor.ModuleId,
            new GameModuleVersionRange(new GameModuleVersion("2.0.0"), new GameModuleVersion("3.0.0")),
            new GameTemplateVersion("1.0.0")));

        Assert.False(result.IsValid);
        Assert.Equal("module_version_mismatch", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void GameSetupValidation_RejectsUnsupportedSetupOptions()
    {
        var registry = new GameModuleRegistry();
        var module = new TestGameModule();
        registry.Register(module);
        var service = new GameSetupValidationService(registry);

        var result = service.Validate(
            module.Descriptor.ModuleId,
            module.Descriptor.ModuleVersion,
            new GameTemplateVersion("1.0.0"),
            new GameSetup([
                new StringGameSetupValue("scenario", "baseline"),
                new BoolGameSetupValue("unsupported", true)
            ]),
            CreateParticipants());

        Assert.False(result.IsValid);
        Assert.Equal("unsupported_setup_option", Assert.Single(result.Issues).Code);
    }

    [Fact]
    public void Descriptor_ExposesVersioningCapabilitiesMemoryAndPromptAssets()
    {
        var descriptor = TestGameModule.CreateDescriptor();

        Assert.Equal(new GameModuleId("test-module"), descriptor.ModuleId);
        Assert.Equal(new GameModuleVersion("1.0.0"), descriptor.ModuleVersion);
        Assert.Equal(new GameTemplateVersion("1.0.0"), descriptor.MinimumTemplateVersion);
        Assert.Equal(new GameTemplateVersion("1.0.0"), descriptor.MaximumTemplateVersion);
        Assert.True(descriptor.CommunicationCapabilities.AllowsPublicChannelMessages);
        Assert.True(descriptor.CommunicationCapabilities.AllowsDirectMessages);
        Assert.True(descriptor.MemoryExpectations.UsesRoundSummaries);
        Assert.Equal(512, descriptor.MemoryExpectations.SuggestedSummaryTokenBudget);
        var requiredAsset = Assert.Single(descriptor.RequiredPromptAssets);
        Assert.Equal("rules", requiredAsset.AssetId);
        Assert.Equal(GamePromptAssetKind.RulesText, requiredAsset.Kind);
    }

    private static GameModuleDescriptor CreateDescriptor(GameModuleVersion moduleVersion) =>
        TestGameModule.CreateDescriptor() with { ModuleVersion = moduleVersion };

    private static ConfigurableGameModule CreateModule(
        GameModuleDescriptor descriptor,
        IReadOnlyList<GamePromptAsset>? promptAssets = null) =>
        new(descriptor, promptAssets ?? [new GamePromptAsset("rules", GamePromptAssetKind.RulesText, "Rules.")]);

    private static ParticipantSetup[] CreateParticipants() =>
    [
        new ParticipantSetup(new ParticipantId("alice"), "Alice", ParticipantKind.Human),
        new ParticipantSetup(new ParticipantId("bob"), "Bob", ParticipantKind.Agent)
    ];

    private sealed class ConfigurableGameModule : IGameModule
    {
        private readonly IReadOnlyList<GamePromptAsset> _promptAssets;

        public ConfigurableGameModule(GameModuleDescriptor descriptor, IReadOnlyList<GamePromptAsset> promptAssets)
        {
            Descriptor = descriptor;
            _promptAssets = promptAssets;
        }

        public GameModuleDescriptor Descriptor { get; }

        public ValidationResult ValidateSetup(GameSetupValidationContext context) => ValidationResult.Valid;

        public RulesGameState CreateInitialState(GameSetupInitializationContext context)
        {
            var participants = context.Participants
                .Select(participant => new ParticipantState(participant.ParticipantId, participant.DisplayName, participant.Kind, []))
                .ToArray();

            return RulesGameState.CreateNotStarted(context.GameInstanceId, context.Descriptor, context.Seed, participants);
        }

        public IReadOnlyList<LegalIntentDescriptor> GetLegalIntentDescriptors(RulesGameState state, ParticipantId participantId) => [];

        public GameModuleTransitionResult HandleIntentCommand(GameModuleTransitionContext context) =>
            GameModuleTransitionResult.Accepted(context.State, []);

        public IReadOnlyList<GameRuleHandlerDescriptor> GetRuleHandlerDescriptors() => [];

        public IReadOnlyList<GamePromptAsset> GetPromptAssets() => _promptAssets;
    }
}
