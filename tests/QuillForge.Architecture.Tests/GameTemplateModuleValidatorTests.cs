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
}
