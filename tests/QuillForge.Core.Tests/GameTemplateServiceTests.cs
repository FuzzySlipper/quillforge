using Microsoft.Extensions.Logging.Abstractions;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Tests;

public sealed class GameTemplateServiceTests
{
    [Fact]
    public async Task SaveAsync_PersistsValidTemplateWithoutCreatingProviders()
    {
        var store = new InMemoryGameTemplateStore();
        var providerCatalog = new FakeProviderCatalog(["local"]);
        var moduleValidator = new FakeModuleValidator();
        var service = CreateService(store, providerCatalog, moduleValidator);
        var template = CreateTemplate();

        var result = await service.SaveAsync("village", template);

        Assert.True(result.Validation.IsValid);
        Assert.True(await store.ExistsAsync("village"));
        Assert.Equal(["local"], providerCatalog.ProviderAliases);
    }

    [Fact]
    public async Task ValidateAsync_RejectsUnknownProviderAliases()
    {
        var service = CreateService(
            new InMemoryGameTemplateStore(),
            new FakeProviderCatalog(["configured"]),
            new FakeModuleValidator());
        var template = CreateTemplate() with
        {
            Roster = new GameTemplateRosterSettings
            {
                RosterSize = 4,
                UserSeatParticipantId = "user",
                AgentPlayers =
                [
                    new GameTemplateAgentPlayerConfig
                    {
                        ParticipantId = "agent-1",
                        ProviderAlias = "missing",
                    }
                ],
            },
        };

        var result = await service.ValidateAsync(template);

        var issue = Assert.Single(result.Issues, issue => issue.Code == "unknown_provider_alias");
        Assert.Equal(GameTemplateValidationSources.Provider, issue.Source);
    }

    [Fact]
    public async Task ValidateAsync_DelegatesBadPlayerCountsToModuleValidator()
    {
        var moduleValidator = new FakeModuleValidator
        {
            Issues =
            [
                new GameTemplateValidationIssue
                {
                    Code = "invalid_player_count",
                    Message = "Too few players.",
                    Source = GameTemplateValidationSources.Module,
                }
            ],
        };
        var service = CreateService(new InMemoryGameTemplateStore(), new FakeProviderCatalog(["local"]), moduleValidator);

        var result = await service.ValidateAsync(CreateTemplate() with
        {
            Roster = CreateTemplate().Roster with { RosterSize = 2 },
        });

        Assert.Contains(result.Issues, issue => issue.Code == "invalid_player_count" && issue.Source == GameTemplateValidationSources.Module);
        Assert.Equal(1, moduleValidator.ValidateCalls);
    }

    [Fact]
    public async Task ValidateAsync_DelegatesUnsupportedRuleOptionsToModuleValidator()
    {
        var moduleValidator = new FakeModuleValidator
        {
            Issues =
            [
                new GameTemplateValidationIssue
                {
                    Code = "unsupported_setup_option",
                    Message = "Unsupported option.",
                    Source = GameTemplateValidationSources.Module,
                }
            ],
        };
        var service = CreateService(new InMemoryGameTemplateStore(), new FakeProviderCatalog(["local"]), moduleValidator);

        var result = await service.ValidateAsync(CreateTemplate() with
        {
            RulesOptions = new GameTemplateRulesOptions
            {
                Values =
                [
                    new GameTemplateRuleOptionValue { Name = "werewolf_count", Kind = GameTemplateRuleOptionValueKind.Int, IntValue = 1 },
                    new GameTemplateRuleOptionValue { Name = "unsupported", Kind = GameTemplateRuleOptionValueKind.Bool, BoolValue = true },
                ],
            },
        });

        Assert.Contains(result.Issues, issue => issue.Code == "unsupported_setup_option" && issue.Source == GameTemplateValidationSources.Module);
    }

    [Fact]
    public async Task ValidateAsync_DelegatesModuleVersionMismatchesToModuleValidator()
    {
        var moduleValidator = new FakeModuleValidator
        {
            Issues =
            [
                new GameTemplateValidationIssue
                {
                    Code = "module_version_mismatch",
                    Message = "No compatible registered module version.",
                    Source = GameTemplateValidationSources.Module,
                }
            ],
        };
        var service = CreateService(new InMemoryGameTemplateStore(), new FakeProviderCatalog(["local"]), moduleValidator);

        var result = await service.ValidateAsync(CreateTemplate() with
        {
            Module = new GameTemplateModuleSelection
            {
                ModuleId = "werewolf",
                MinimumVersion = "9.0.0",
                MaximumVersion = "9.9.9",
            },
        });

        Assert.Contains(result.Issues, issue => issue.Code == "module_version_mismatch" && issue.Source == GameTemplateValidationSources.Module);
    }

    [Fact]
    public async Task ValidateAsync_RejectsIncompleteRostersBeforePersistence()
    {
        var service = CreateService(new InMemoryGameTemplateStore(), new FakeProviderCatalog(["local"]), new FakeModuleValidator());
        var result = await service.ValidateAsync(CreateTemplate() with
        {
            Roster = new GameTemplateRosterSettings
            {
                RosterSize = 4,
                UserSeatParticipantId = "user",
                AgentPlayers =
                [
                    new GameTemplateAgentPlayerConfig { ParticipantId = "agent-1", ProviderAlias = "local" },
                ],
            },
        });

        Assert.Contains(result.Issues, issue => issue.Code == "incomplete_roster" && issue.Source == GameTemplateValidationSources.Template);
    }

    [Fact]
    public async Task SaveAsync_DoesNotPersistInvalidTemplates()
    {
        var store = new InMemoryGameTemplateStore();
        var service = CreateService(store, new FakeProviderCatalog(["local"]), new FakeModuleValidator());
        var invalid = CreateTemplate() with
        {
            Roster = CreateTemplate().Roster with { RosterSize = 0 },
        };

        var result = await service.SaveAsync("invalid", invalid);

        Assert.False(result.Validation.IsValid);
        Assert.False(await store.ExistsAsync("invalid"));
    }

    [Fact]
    public async Task CloneAsync_PersistsCopyWithNewIdentity()
    {
        var store = new InMemoryGameTemplateStore();
        var service = CreateService(store, new FakeProviderCatalog(["local"]), new FakeModuleValidator());
        await service.SaveAsync("source", CreateTemplate() with { TemplateId = "source" });

        var clone = await service.CloneAsync("source", "copy", "Copy Template");

        Assert.True(clone.Validation.IsValid);
        Assert.True(await store.ExistsAsync("copy"));
        Assert.Equal("copy", clone.Template.TemplateId);
        Assert.Equal("Copy Template", clone.Template.DisplayName);
    }

    [Fact]
    public async Task CloneAsync_RejectsMissingTargetTemplateIdWithSpecificFieldMessage()
    {
        var service = CreateService(
            new InMemoryGameTemplateStore(),
            new FakeProviderCatalog(["local"]),
            new FakeModuleValidator());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CloneAsync("source", " ", displayName: null));

        Assert.Equal("targetTemplateId is required.", exception.Message);
    }

    private static GameTemplateService CreateService(
        IGameTemplateStore store,
        IGameTemplateProviderCatalog providerCatalog,
        IGameTemplateModuleValidator moduleValidator) =>
        new(store, providerCatalog, moduleValidator, NullLogger<GameTemplateService>.Instance);

    private static GameTemplate CreateTemplate() =>
        new()
        {
            TemplateId = "village",
            DisplayName = "Village",
            Module = new GameTemplateModuleSelection
            {
                ModuleId = "werewolf",
                MinimumVersion = "1.0.0",
                MaximumVersion = "1.0.0",
            },
            RulesOptions = new GameTemplateRulesOptions
            {
                Values =
                [
                    new GameTemplateRuleOptionValue { Name = "werewolf_count", Kind = GameTemplateRuleOptionValueKind.Int, IntValue = 1 },
                    new GameTemplateRuleOptionValue { Name = "seer_enabled", Kind = GameTemplateRuleOptionValueKind.Bool, BoolValue = false },
                    new GameTemplateRuleOptionValue { Name = "one_night_compatible", Kind = GameTemplateRuleOptionValueKind.Bool, BoolValue = false },
                ],
            },
            Roster = new GameTemplateRosterSettings
            {
                RosterSize = 4,
                UserSeatParticipantId = "user",
                AgentPlayers =
                [
                    new GameTemplateAgentPlayerConfig
                    {
                        ParticipantId = "agent-1",
                        ProviderAlias = "local",
                        ModelOverride = null,
                        CharacterPrompt = "You are cautious.",
                        Personality = "wary",
                        FixedName = "Bob",
                        RandomNameBehavior = GameTemplateRandomNameBehavior.UseFixedNameWhenProvided,
                    },
                    new GameTemplateAgentPlayerConfig
                    {
                        ParticipantId = "agent-2",
                        ProviderAlias = "local",
                        FixedName = "Carol",
                    },
                    new GameTemplateAgentPlayerConfig
                    {
                        ParticipantId = "agent-3",
                        ProviderAlias = "local",
                        FixedName = "Drew",
                    }
                ],
            },
            Memory = new GameTemplateMemorySettings { TokenBudget = 512 },
            Communication = new GameTemplateCommunicationSettings
            {
                PublicChannelEnabled = true,
                DirectMessagesEnabled = true,
                HostMessagesEnabled = true,
            },
            Naming = new GameTemplateNamingSettings
            {
                RandomizeAgentNames = true,
                RandomNameSet = "village",
                RandomSeed = 17,
            },
        };

    private sealed class FakeProviderCatalog : IGameTemplateProviderCatalog
    {
        public FakeProviderCatalog(IReadOnlyList<string> providerAliases)
        {
            ProviderAliases = providerAliases;
        }

        public IReadOnlyList<string> ProviderAliases { get; }

        public Task<IReadOnlySet<string>> ListProviderAliasesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlySet<string>>(ProviderAliases.ToHashSet(StringComparer.OrdinalIgnoreCase));
    }

    private sealed class FakeModuleValidator : IGameTemplateModuleValidator
    {
        public IReadOnlyList<GameTemplateValidationIssue> Issues { get; init; } = [];

        public int ValidateCalls { get; private set; }

        public Task<GameTemplateValidationResult> ValidateAsync(GameTemplate template, CancellationToken ct = default)
        {
            ValidateCalls++;
            return Task.FromResult(GameTemplateValidationResult.FromIssues(Issues));
        }
    }

    private sealed class InMemoryGameTemplateStore : IGameTemplateStore
    {
        private readonly Dictionary<string, GameTemplate> _templates = new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<string>>(_templates.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase).ToArray());

        public Task<bool> ExistsAsync(string templateId, CancellationToken ct = default) =>
            Task.FromResult(_templates.ContainsKey(templateId));

        public Task<GameTemplate> LoadAsync(string templateId, CancellationToken ct = default)
        {
            if (!_templates.TryGetValue(templateId, out var template))
            {
                throw new FileNotFoundException($"Template {templateId} not found.");
            }

            return Task.FromResult(template);
        }

        public Task SaveAsync(string templateId, GameTemplate template, CancellationToken ct = default)
        {
            _templates[templateId] = template;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string templateId, CancellationToken ct = default)
        {
            _templates.Remove(templateId);
            return Task.CompletedTask;
        }
    }
}
