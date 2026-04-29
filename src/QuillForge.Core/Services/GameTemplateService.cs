using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class GameTemplateService : IGameTemplateService
{
    private readonly IGameTemplateStore _store;
    private readonly IGameTemplateProviderCatalog _providerCatalog;
    private readonly IGameTemplateModuleValidator _moduleValidator;
    private readonly ILogger<GameTemplateService> _logger;

    public GameTemplateService(
        IGameTemplateStore store,
        IGameTemplateProviderCatalog providerCatalog,
        IGameTemplateModuleValidator moduleValidator,
        ILogger<GameTemplateService> logger)
    {
        _store = store;
        _providerCatalog = providerCatalog;
        _moduleValidator = moduleValidator;
        _logger = logger;
    }

    public async Task<IReadOnlyList<GameTemplateSummary>> ListAsync(CancellationToken ct = default)
    {
        var templateIds = await _store.ListAsync(ct);
        var summaries = new List<GameTemplateSummary>();
        foreach (var templateId in templateIds)
        {
            var template = NormalizeTemplate(await _store.LoadAsync(templateId, ct), templateId);
            summaries.Add(new GameTemplateSummary
            {
                TemplateId = template.TemplateId,
                DisplayName = template.DisplayName,
                ModuleId = template.Module.ModuleId,
                MinimumModuleVersion = template.Module.MinimumVersion,
                MaximumModuleVersion = template.Module.MaximumVersion,
            });
        }

        return summaries
            .OrderBy(summary => summary.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(summary => summary.TemplateId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<GameTemplateValidationEnvelope> LoadAsync(string templateId, CancellationToken ct = default)
    {
        var resolvedTemplateId = NormalizeId(templateId, nameof(templateId));
        var template = NormalizeTemplate(await _store.LoadAsync(resolvedTemplateId, ct), resolvedTemplateId);
        var validation = await ValidateAsync(template, ct);
        return new GameTemplateValidationEnvelope
        {
            Template = template,
            Validation = validation,
        };
    }

    public async Task<GameTemplateValidationEnvelope> SaveAsync(string templateId, GameTemplate template, CancellationToken ct = default)
    {
        var resolvedTemplateId = NormalizeId(templateId, nameof(templateId));
        var normalized = NormalizeTemplate(template, resolvedTemplateId) with
        {
            TemplateId = resolvedTemplateId,
        };
        var validation = await ValidateAsync(normalized, ct);
        if (!validation.IsValid)
        {
            return new GameTemplateValidationEnvelope
            {
                Template = normalized,
                Validation = validation,
            };
        }

        await _store.SaveAsync(resolvedTemplateId, normalized, ct);
        _logger.LogInformation(
            "Saved game template {TemplateId} for module {ModuleId} versions {MinimumVersion}-{MaximumVersion}",
            resolvedTemplateId,
            normalized.Module.ModuleId,
            normalized.Module.MinimumVersion,
            normalized.Module.MaximumVersion);

        return new GameTemplateValidationEnvelope
        {
            Template = normalized,
            Validation = validation,
        };
    }

    public async Task<GameTemplateValidationEnvelope> CloneAsync(
        string sourceTemplateId,
        string targetTemplateId,
        string? displayName,
        CancellationToken ct = default)
    {
        var sourceId = NormalizeId(sourceTemplateId, nameof(sourceTemplateId));
        var targetId = NormalizeId(targetTemplateId, nameof(targetTemplateId));
        if (string.Equals(sourceId, targetId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Clone target template id must differ from the source template id.");
        }

        if (await _store.ExistsAsync(targetId, ct))
        {
            throw new InvalidOperationException($"Game template {targetId} already exists.");
        }

        var source = NormalizeTemplate(await _store.LoadAsync(sourceId, ct), sourceId);
        var clone = source with
        {
            TemplateId = targetId,
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? $"{source.DisplayName} Copy" : displayName.Trim(),
        };

        return await SaveAsync(targetId, clone, ct);
    }

    public async Task DeleteAsync(string templateId, CancellationToken ct = default)
    {
        var resolvedTemplateId = NormalizeId(templateId, nameof(templateId));
        if (!await _store.ExistsAsync(resolvedTemplateId, ct))
        {
            throw new FileNotFoundException($"Game template {resolvedTemplateId} not found.");
        }

        await _store.DeleteAsync(resolvedTemplateId, ct);
        _logger.LogInformation("Deleted game template {TemplateId}", resolvedTemplateId);
    }

    public async Task<GameTemplateValidationResult> ValidateAsync(GameTemplate template, CancellationToken ct = default)
    {
        var normalized = NormalizeTemplate(template, template.TemplateId);
        var issues = new List<GameTemplateValidationIssue>();
        issues.AddRange(ValidateTemplateShape(normalized));
        issues.AddRange(await ValidateProvidersAsync(normalized, ct));

        var moduleValidation = await _moduleValidator.ValidateAsync(normalized, ct);
        issues.AddRange(moduleValidation.Issues);

        return GameTemplateValidationResult.FromIssues(issues);
    }

    private static IReadOnlyList<GameTemplateValidationIssue> ValidateTemplateShape(GameTemplate template)
    {
        var issues = new List<GameTemplateValidationIssue>();
        AddRequiredIssueIfBlank(issues, template.TemplateId, "templateId");
        AddRequiredIssueIfBlank(issues, template.DisplayName, "displayName");
        AddRequiredIssueIfBlank(issues, template.Module.ModuleId, "module.moduleId");
        AddRequiredIssueIfBlank(issues, template.Module.MinimumVersion, "module.minimumVersion");
        AddRequiredIssueIfBlank(issues, template.Module.MaximumVersion, "module.maximumVersion");
        AddRequiredIssueIfBlank(issues, template.TemplateVersion, "templateVersion");

        if (!IsVersionRangeValid(template.Module.MinimumVersion, template.Module.MaximumVersion))
        {
            issues.Add(new GameTemplateValidationIssue
            {
                Code = "invalid_module_version_range",
                Field = "module",
                Message = "Minimum module version must be less than or equal to maximum module version.",
            });
        }

        if (template.Roster.RosterSize <= 0)
        {
            issues.Add(new GameTemplateValidationIssue
            {
                Code = "invalid_roster_size",
                Field = "roster.rosterSize",
                Message = "Roster size must be greater than zero.",
            });
        }

        if (template.Roster.AgentPlayers.Count > template.Roster.RosterSize)
        {
            issues.Add(new GameTemplateValidationIssue
            {
                Code = "invalid_roster_size",
                Field = "roster.agentPlayers",
                Message = "Agent player count cannot exceed roster size.",
            });
        }

        var configuredSeatCount = template.Roster.AgentPlayers.Count
            + (string.IsNullOrWhiteSpace(template.Roster.UserSeatParticipantId) ? 0 : 1);
        if (template.Roster.RosterSize > 0 && configuredSeatCount != template.Roster.RosterSize)
        {
            issues.Add(new GameTemplateValidationIssue
            {
                Code = "incomplete_roster",
                Field = "roster",
                Message = "Roster size must equal the user seat plus configured agent player seats.",
            });
        }

        if (template.Memory.TokenBudget < 0)
        {
            issues.Add(new GameTemplateValidationIssue
            {
                Code = "invalid_memory_token_budget",
                Field = "memory.tokenBudget",
                Message = "Memory token budget cannot be negative.",
            });
        }

        var participantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(template.Roster.UserSeatParticipantId))
        {
            participantIds.Add(template.Roster.UserSeatParticipantId.Trim());
        }

        for (var i = 0; i < template.Roster.AgentPlayers.Count; i++)
        {
            var player = template.Roster.AgentPlayers[i];
            AddRequiredIssueIfBlank(issues, player.ParticipantId, $"roster.agentPlayers[{i}].participantId");
            AddRequiredIssueIfBlank(issues, player.ProviderAlias, $"roster.agentPlayers[{i}].providerAlias");
            if (!string.IsNullOrWhiteSpace(player.ParticipantId) && !participantIds.Add(player.ParticipantId.Trim()))
            {
                issues.Add(new GameTemplateValidationIssue
                {
                    Code = "duplicate_participant_id",
                    Field = $"roster.agentPlayers[{i}].participantId",
                    Message = $"Participant id '{player.ParticipantId}' is used more than once.",
                });
            }
        }

        for (var i = 0; i < template.RulesOptions.Values.Count; i++)
        {
            var option = template.RulesOptions.Values[i];
            AddRequiredIssueIfBlank(issues, option.Name, $"rulesOptions.values[{i}].name");
            if (!HasValueForKind(option))
            {
                issues.Add(new GameTemplateValidationIssue
                {
                    Code = "missing_rule_option_value",
                    Field = $"rulesOptions.values[{i}]",
                    Message = $"Rule option '{option.Name}' is missing a value for kind '{option.Kind}'.",
                });
            }
        }

        return issues;
    }

    private async Task<IReadOnlyList<GameTemplateValidationIssue>> ValidateProvidersAsync(GameTemplate template, CancellationToken ct)
    {
        var aliases = await _providerCatalog.ListProviderAliasesAsync(ct);
        var issues = new List<GameTemplateValidationIssue>();
        for (var i = 0; i < template.Roster.AgentPlayers.Count; i++)
        {
            var player = template.Roster.AgentPlayers[i];
            if (string.IsNullOrWhiteSpace(player.ProviderAlias))
            {
                continue;
            }

            if (!aliases.Contains(player.ProviderAlias.Trim()))
            {
                issues.Add(new GameTemplateValidationIssue
                {
                    Code = "unknown_provider_alias",
                    Field = $"roster.agentPlayers[{i}].providerAlias",
                    Source = GameTemplateValidationSources.Provider,
                    Message = $"Provider alias '{player.ProviderAlias}' is not configured.",
                });
            }
        }

        return issues;
    }

    private static bool HasValueForKind(GameTemplateRuleOptionValue option) =>
        option.Kind switch
        {
            GameTemplateRuleOptionValueKind.String => option.StringValue is not null,
            GameTemplateRuleOptionValueKind.Int => option.IntValue.HasValue,
            GameTemplateRuleOptionValueKind.Bool => option.BoolValue.HasValue,
            GameTemplateRuleOptionValueKind.ParticipantId => option.ParticipantIdValue is not null,
            GameTemplateRuleOptionValueKind.ParticipantSet => option.ParticipantSetValue.Count > 0,
            _ => false,
        };

    private static void AddRequiredIssueIfBlank(List<GameTemplateValidationIssue> issues, string? value, string field)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        issues.Add(new GameTemplateValidationIssue
        {
            Code = "required",
            Field = field,
            Message = $"{field} is required.",
        });
    }

    private static GameTemplate NormalizeTemplate(GameTemplate template, string? fallbackId)
    {
        var templateId = NormalizeOptional(template.TemplateId) ?? NormalizeOptional(fallbackId) ?? string.Empty;
        return template with
        {
            TemplateId = templateId,
            DisplayName = NormalizeOptional(template.DisplayName) ?? templateId,
            Description = NormalizeOptional(template.Description),
            TemplateVersion = NormalizeOptional(template.TemplateVersion) ?? "1.0.0",
            Module = new GameTemplateModuleSelection
            {
                ModuleId = NormalizeOptional(template.Module.ModuleId) ?? string.Empty,
                MinimumVersion = NormalizeOptional(template.Module.MinimumVersion) ?? string.Empty,
                MaximumVersion = NormalizeOptional(template.Module.MaximumVersion) ?? string.Empty,
            },
            RulesOptions = new GameTemplateRulesOptions
            {
                Values = template.RulesOptions.Values.Select(NormalizeRuleOption).ToArray(),
            },
            Roster = template.Roster with
            {
                UserSeatParticipantId = NormalizeOptional(template.Roster.UserSeatParticipantId),
                AgentPlayers = template.Roster.AgentPlayers.Select(NormalizeAgentPlayer).ToArray(),
            },
            Naming = template.Naming with
            {
                RandomNameSet = NormalizeOptional(template.Naming.RandomNameSet),
            },
        };
    }

    private static GameTemplateRuleOptionValue NormalizeRuleOption(GameTemplateRuleOptionValue value) =>
        value with
        {
            Name = NormalizeOptional(value.Name) ?? string.Empty,
            StringValue = NormalizeOptional(value.StringValue),
            ParticipantIdValue = NormalizeOptional(value.ParticipantIdValue),
            ParticipantSetValue = value.ParticipantSetValue
                .Select(item => NormalizeOptional(item))
                .Where(item => item is not null)
                .Select(item => item!)
                .ToArray(),
        };

    private static GameTemplateAgentPlayerConfig NormalizeAgentPlayer(GameTemplateAgentPlayerConfig player) =>
        player with
        {
            ParticipantId = NormalizeOptional(player.ParticipantId) ?? string.Empty,
            ProviderAlias = NormalizeOptional(player.ProviderAlias) ?? string.Empty,
            ModelOverride = NormalizeOptional(player.ModelOverride),
            CharacterPrompt = NormalizeOptional(player.CharacterPrompt),
            Personality = NormalizeOptional(player.Personality),
            FixedName = NormalizeOptional(player.FixedName),
            SystemPromptTemplate = NormalizePromptTemplateSelection(player.SystemPromptTemplate),
        };

    private static GamePromptTemplateSelection NormalizePromptTemplateSelection(GamePromptTemplateSelection? selection)
    {
        if (selection is null || selection.Source != GamePromptTemplateSource.User)
        {
            return GamePromptTemplateSelection.Default;
        }

        var promptName = NormalizeOptional(selection.UserPromptName);
        return promptName is null
            ? GamePromptTemplateSelection.Default
            : GamePromptTemplateSelection.ForUserPrompt(promptName);
    }

    private static string NormalizeId(string templateId, string parameterName)
    {
        var normalized = NormalizeOptional(templateId);
        if (normalized is null)
        {
            throw new ArgumentException($"{parameterName} is required.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static bool IsVersionRangeValid(string minimumVersion, string maximumVersion)
    {
        if (string.IsNullOrWhiteSpace(minimumVersion) || string.IsNullOrWhiteSpace(maximumVersion))
        {
            return true;
        }

        if (Version.TryParse(minimumVersion, out var minimum) && Version.TryParse(maximumVersion, out var maximum))
        {
            return minimum.CompareTo(maximum) <= 0;
        }

        return string.CompareOrdinal(minimumVersion, maximumVersion) <= 0;
    }
}
