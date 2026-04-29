using Den.RulesEngine;
using Den.RulesEngine.Werewolf;
using QuillForge.Core.Models;
using QuillForge.Web.Services;

namespace QuillForge.Architecture.Tests;

public sealed class GameTemplateModuleValidatorTests
{
    [Fact]
    public async Task ValidateAsync_UsesRegistryVersionRangeForModuleMismatches()
    {
        var validator = CreateValidator();
        var template = CreateTemplate() with
        {
            Module = new GameTemplateModuleSelection
            {
                ModuleId = WerewolfModuleAssemblyMarker.ModuleId.Value,
                MinimumVersion = "9.0.0",
                MaximumVersion = "9.9.9",
            },
        };

        var result = await validator.ValidateAsync(template);

        Assert.Contains(result.Issues, issue => issue.Code == "module_version_mismatch" && issue.Source == GameTemplateValidationSources.Module);
    }

    [Fact]
    public async Task ValidateAsync_DelegatesBadPlayerCountsToWerewolfModuleValidation()
    {
        var validator = CreateValidator();
        var template = CreateTemplate() with
        {
            Roster = new GameTemplateRosterSettings
            {
                RosterSize = 2,
                UserSeatParticipantId = "user",
                AgentPlayers =
                [
                    new GameTemplateAgentPlayerConfig { ParticipantId = "agent-1", ProviderAlias = "local" },
                ],
            },
        };

        var result = await validator.ValidateAsync(template);

        Assert.Contains(result.Issues, issue => issue.Code == "invalid_player_count" && issue.Source == GameTemplateValidationSources.Module);
    }

    [Fact]
    public async Task ValidateAsync_RejectsUnsupportedModuleOptions()
    {
        var validator = CreateValidator();
        var template = CreateTemplate() with
        {
            RulesOptions = new GameTemplateRulesOptions
            {
                Values =
                [
                    new GameTemplateRuleOptionValue { Name = WerewolfConstants.WerewolfCountSetupField, Kind = GameTemplateRuleOptionValueKind.Int, IntValue = 1 },
                    new GameTemplateRuleOptionValue { Name = WerewolfConstants.SeerEnabledSetupField, Kind = GameTemplateRuleOptionValueKind.Bool, BoolValue = false },
                    new GameTemplateRuleOptionValue { Name = WerewolfConstants.OneNightCompatibleSetupField, Kind = GameTemplateRuleOptionValueKind.Bool, BoolValue = false },
                    new GameTemplateRuleOptionValue { Name = "unsupported_option", Kind = GameTemplateRuleOptionValueKind.Bool, BoolValue = true },
                ],
            },
        };

        var result = await validator.ValidateAsync(template);

        Assert.Contains(result.Issues, issue => issue.Code == "unsupported_setup_option" && issue.Source == GameTemplateValidationSources.Module);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsTypedIssueWhenRegistryLoadStateIsInconsistent()
    {
        var module = new FlappingDescriptorModule();
        var registry = new GameModuleRegistry();
        Assert.True(registry.Register(module).IsValid);
        var validator = new GameTemplateModuleValidator(registry, new GameSetupValidationService(registry));
        var template = CreateTemplate() with
        {
            Module = new GameTemplateModuleSelection
            {
                ModuleId = FlappingDescriptorModule.ModuleIdValue,
                MinimumVersion = "1.0.0",
                MaximumVersion = "1.0.0",
            },
            RulesOptions = new GameTemplateRulesOptions { Values = [] },
        };

        var result = await validator.ValidateAsync(template);

        var issue = Assert.Single(result.Issues);
        Assert.Equal("loadable_module_missing", issue.Code);
        Assert.Equal("module", issue.Field);
        Assert.Equal(GameTemplateValidationSources.Module, issue.Source);
    }

    private static GameTemplateModuleValidator CreateValidator()
    {
        var registryResult = new GameModuleRegistryFactory().Create([new WerewolfModule()]);
        Assert.True(registryResult.ValidationResult.IsValid);
        return new GameTemplateModuleValidator(registryResult.Registry, new GameSetupValidationService(registryResult.Registry));
    }

    private static GameTemplate CreateTemplate() =>
        new()
        {
            TemplateId = "village",
            DisplayName = "Village",
            Module = new GameTemplateModuleSelection
            {
                ModuleId = WerewolfModuleAssemblyMarker.ModuleId.Value,
                MinimumVersion = WerewolfModuleAssemblyMarker.ModuleVersion.Value,
                MaximumVersion = WerewolfModuleAssemblyMarker.ModuleVersion.Value,
            },
            RulesOptions = new GameTemplateRulesOptions
            {
                Values =
                [
                    new GameTemplateRuleOptionValue { Name = WerewolfConstants.WerewolfCountSetupField, Kind = GameTemplateRuleOptionValueKind.Int, IntValue = 1 },
                    new GameTemplateRuleOptionValue { Name = WerewolfConstants.SeerEnabledSetupField, Kind = GameTemplateRuleOptionValueKind.Bool, BoolValue = false },
                    new GameTemplateRuleOptionValue { Name = WerewolfConstants.OneNightCompatibleSetupField, Kind = GameTemplateRuleOptionValueKind.Bool, BoolValue = false },
                ],
            },
            Roster = new GameTemplateRosterSettings
            {
                RosterSize = 4,
                UserSeatParticipantId = "user",
                AgentPlayers =
                [
                    new GameTemplateAgentPlayerConfig { ParticipantId = "agent-1", ProviderAlias = "local" },
                    new GameTemplateAgentPlayerConfig { ParticipantId = "agent-2", ProviderAlias = "local" },
                    new GameTemplateAgentPlayerConfig { ParticipantId = "agent-3", ProviderAlias = "local" },
                ],
            },
        };

    private sealed class FlappingDescriptorModule : IGameModule
    {
        public const string ModuleIdValue = "flapping-module";

        private int _descriptorAccessCount;

        public GameModuleDescriptor Descriptor
        {
            get
            {
                _descriptorAccessCount++;
                var version = _descriptorAccessCount <= 2 ? "1.0.0" : "9.0.0";
                return new GameModuleDescriptor(
                    new GameModuleId(ModuleIdValue),
                    new GameModuleVersion(version),
                    new GameTemplateVersion("1.0.0"),
                    new GameTemplateVersion("1.0.0"),
                    "Flapping Module",
                    new PlayerCountRange(1, 8),
                    []);
            }
        }

        public ValidationResult ValidateSetup(GameSetupValidationContext context) => ValidationResult.Valid;

        public RulesGameState CreateInitialState(GameSetupInitializationContext context) =>
            RulesGameState.CreateNotStarted(context.GameInstanceId, context.Descriptor, 1, []);

        public IReadOnlyList<LegalIntentDescriptor> GetLegalIntentDescriptors(RulesGameState state, ParticipantId participantId) => [];

        public GameModuleTransitionResult HandleIntentCommand(GameModuleTransitionContext context) =>
            GameModuleTransitionResult.Accepted(context.State, []);

        public IReadOnlyList<GameRuleHandlerDescriptor> GetRuleHandlerDescriptors() => [];

        public IReadOnlyList<GamePromptAsset> GetPromptAssets() => [];
    }
}
