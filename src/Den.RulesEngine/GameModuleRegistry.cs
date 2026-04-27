namespace Den.RulesEngine;

public sealed class GameModuleRegistry
{
    private readonly List<IGameModule> _modules = [];

    public IReadOnlyList<IGameModule> Modules => _modules;

    public ValidationResult Register(IGameModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        if (_modules.Any(existing => HasSameIdentity(existing, module)))
        {
            return ValidationResult.Invalid(new ValidationIssue(
                "duplicate_module_id",
                $"Module '{module.Descriptor.ModuleId}' version '{module.Descriptor.ModuleVersion}' is already registered."));
        }

        _modules.Add(module);
        return ValidationResult.Valid;
    }

    public IGameModule? Find(GameModuleId moduleId, GameModuleVersion moduleVersion) =>
        _modules.FirstOrDefault(module =>
            module.Descriptor.ModuleId == moduleId
            && module.Descriptor.ModuleVersion == moduleVersion);

    public ValidationResult ValidateRegistered(GameModuleId moduleId, GameModuleVersion moduleVersion)
    {
        if (!_modules.Any(module => module.Descriptor.ModuleId == moduleId))
        {
            return ValidationResult.Invalid(new ValidationIssue("unknown_module_id", $"Module '{moduleId}' is not registered."));
        }

        return Find(moduleId, moduleVersion) is null
            ? ValidationResult.Invalid(new ValidationIssue(
                "unknown_module_version",
                $"Module '{moduleId}' version '{moduleVersion}' is not registered."))
            : ValidationResult.Valid;
    }

    private static bool HasSameIdentity(IGameModule first, IGameModule second) =>
        first.Descriptor.ModuleId == second.Descriptor.ModuleId
        && first.Descriptor.ModuleVersion == second.Descriptor.ModuleVersion;
}

public sealed class GameModuleRegistryFactory
{
    public GameModuleRegistryBuildResult Create(IReadOnlyList<IGameModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var registry = new GameModuleRegistry();
        var issues = new List<ValidationIssue>();
        foreach (var module in modules)
        {
            var result = registry.Register(module);
            issues.AddRange(result.Issues);
        }

        return new GameModuleRegistryBuildResult(registry, ValidationResult.FromIssues(issues));
    }
}

public sealed record GameModuleRegistryBuildResult(
    GameModuleRegistry Registry,
    ValidationResult ValidationResult);

public sealed class GameSetupValidationService
{
    private readonly GameModuleRegistry _registry;

    public GameSetupValidationService(GameModuleRegistry registry)
    {
        _registry = registry;
    }

    public ValidationResult Validate(
        GameModuleId moduleId,
        GameModuleVersion moduleVersion,
        GameTemplateVersion templateVersion,
        GameSetup setup,
        IReadOnlyList<ParticipantSetup> participants)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(participants);

        var registration = _registry.ValidateRegistered(moduleId, moduleVersion);
        if (!registration.IsValid)
        {
            return registration;
        }

        var module = _registry.Find(moduleId, moduleVersion)
            ?? throw new InvalidOperationException("Registry validation succeeded but module lookup failed.");

        return ValidateModule(module, templateVersion, setup, participants);
    }

    private static ValidationResult ValidateModule(
        IGameModule module,
        GameTemplateVersion templateVersion,
        GameSetup setup,
        IReadOnlyList<ParticipantSetup> participants)
    {
        var descriptor = module.Descriptor;
        var issues = new List<ValidationIssue>();

        if (!IsTemplateVersionCompatible(templateVersion, descriptor.MinimumTemplateVersion, descriptor.MaximumTemplateVersion))
        {
            issues.Add(new ValidationIssue(
                "incompatible_template_version",
                $"Template version '{templateVersion}' is not compatible with module '{descriptor.ModuleId}' version '{descriptor.ModuleVersion}'."));
        }

        if (!descriptor.PlayerCount.Contains(participants.Count))
        {
            issues.Add(new ValidationIssue(
                "invalid_player_count",
                $"Module '{descriptor.ModuleId}' requires between {descriptor.PlayerCount.Minimum} and {descriptor.PlayerCount.Maximum} participants."));
        }

        issues.AddRange(ValidateParticipantRequirements(descriptor, participants));
        issues.AddRange(ValidateSetupFields(descriptor, setup));
        issues.AddRange(ValidateRequiredPromptAssets(descriptor, module.GetPromptAssets()));

        if (issues.Count > 0)
        {
            return ValidationResult.FromIssues(issues);
        }

        return module.ValidateSetup(new GameSetupValidationContext(descriptor, setup, participants, templateVersion));
    }

    private static IReadOnlyList<ValidationIssue> ValidateParticipantRequirements(
        GameModuleDescriptor descriptor,
        IReadOnlyList<ParticipantSetup> participants)
    {
        var requirements = descriptor.ParticipantRequirements;
        var issues = new List<ValidationIssue>();

        if (!requirements.AllowsHumanParticipants && participants.Any(participant => participant.Kind == ParticipantKind.Human))
        {
            issues.Add(new ValidationIssue("unsupported_participant_kind", "Module does not allow human participants."));
        }

        if (!requirements.AllowsAgentParticipants && participants.Any(participant => participant.Kind == ParticipantKind.Agent))
        {
            issues.Add(new ValidationIssue("unsupported_participant_kind", "Module does not allow agent participants."));
        }

        if (!requirements.AllowsSystemParticipants && participants.Any(participant => participant.Kind == ParticipantKind.System))
        {
            issues.Add(new ValidationIssue("unsupported_participant_kind", "Module does not allow system participants."));
        }

        var humanCount = participants.Count(participant => participant.Kind == ParticipantKind.Human);
        var agentCount = participants.Count(participant => participant.Kind == ParticipantKind.Agent);
        if (humanCount < requirements.MinimumHumanParticipants || agentCount < requirements.MinimumAgentParticipants)
        {
            issues.Add(new ValidationIssue(
                "unsupported_role_mix",
                "Participant mix does not satisfy the module requirements."));
        }

        return issues;
    }

    private static IReadOnlyList<ValidationIssue> ValidateSetupFields(GameModuleDescriptor descriptor, GameSetup setup)
    {
        var issues = new List<ValidationIssue>();
        foreach (var field in descriptor.SetupFields)
        {
            var value = setup.FindValue(field.Name);
            if (value is null)
            {
                if (field.IsRequired)
                {
                    issues.Add(ValidationIssue.Required(field.Name));
                }

                continue;
            }

            var actualKind = GetSetupValueKind(value);
            if (actualKind != field.ValueKind)
            {
                issues.Add(new ValidationIssue(
                    "incompatible_setup_option",
                    $"Setup option '{field.Name}' must be '{field.ValueKind}' but was '{actualKind}'."));
            }
        }

        return issues;
    }

    private static IReadOnlyList<ValidationIssue> ValidateRequiredPromptAssets(
        GameModuleDescriptor descriptor,
        IReadOnlyList<GamePromptAsset> promptAssets)
    {
        var issues = new List<ValidationIssue>();
        foreach (var required in descriptor.RequiredPromptAssets)
        {
            var exists = promptAssets.Any(asset =>
                string.Equals(asset.AssetId, required.AssetId, StringComparison.Ordinal)
                && asset.Kind == required.Kind);
            if (!exists)
            {
                issues.Add(new ValidationIssue(
                    "missing_required_module_asset",
                    $"Module '{descriptor.ModuleId}' is missing required prompt asset '{required.AssetId}'."));
            }
        }

        return issues;
    }

    private static GameSetupValueKind GetSetupValueKind(GameSetupValue value)
    {
        return value switch
        {
            StringGameSetupValue => GameSetupValueKind.String,
            IntGameSetupValue => GameSetupValueKind.Int,
            BoolGameSetupValue => GameSetupValueKind.Bool,
            ParticipantIdGameSetupValue => GameSetupValueKind.ParticipantId,
            ParticipantSetGameSetupValue => GameSetupValueKind.ParticipantSet,
            _ => throw new ArgumentException("Unknown setup value type.", nameof(value))
        };
    }

    private static bool IsTemplateVersionCompatible(
        GameTemplateVersion templateVersion,
        GameTemplateVersion minimumTemplateVersion,
        GameTemplateVersion maximumTemplateVersion)
    {
        if (!Version.TryParse(templateVersion.Value, out var current)
            || !Version.TryParse(minimumTemplateVersion.Value, out var minimum)
            || !Version.TryParse(maximumTemplateVersion.Value, out var maximum))
        {
            return string.CompareOrdinal(templateVersion.Value, minimumTemplateVersion.Value) >= 0
                && string.CompareOrdinal(templateVersion.Value, maximumTemplateVersion.Value) <= 0;
        }

        return current.CompareTo(minimum) >= 0 && current.CompareTo(maximum) <= 0;
    }
}
