namespace Den.RulesEngine.Tests;

public sealed class GameModuleRegistryTests
{
    [Fact]
    public void Register_StoresExplicitModulesById()
    {
        var registry = new GameModuleRegistry();
        var module = new TestGameModule();

        var result = registry.Register(module);

        Assert.True(result.IsValid);
        Assert.Same(module, registry.Find(module.Descriptor.ModuleId));
        Assert.Single(registry.Modules);
    }

    [Fact]
    public void Register_RejectsDuplicateModuleIds()
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
    public void GameSetupValidation_RejectsUnknownModule()
    {
        var service = new GameSetupValidationService(new GameModuleRegistry());

        var result = service.Validate(
            new GameModuleId("missing"),
            new GameTemplateVersion("1.0.0"),
            GameSetup.Empty,
            CreateParticipants());

        Assert.False(result.IsValid);
        Assert.Equal("unknown_module_id", Assert.Single(result.Issues).Code);
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
            new GameTemplateVersion("1.0.0"),
            GameSetup.Empty,
            CreateParticipants());

        Assert.False(result.IsValid);
        Assert.Equal("required", Assert.Single(result.Issues).Code);
    }

    private static ParticipantSetup[] CreateParticipants() =>
    [
        new ParticipantSetup(new ParticipantId("alice"), "Alice", ParticipantKind.Human),
        new ParticipantSetup(new ParticipantId("bob"), "Bob", ParticipantKind.Agent)
    ];
}
