namespace Den.RulesEngine;

public sealed class GameModuleRegistry
{
    private readonly List<IGameModule> _modules = [];

    public IReadOnlyList<IGameModule> Modules => _modules;

    public ValidationResult Register(IGameModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        if (_modules.Any(existing => existing.Descriptor.ModuleId == module.Descriptor.ModuleId))
        {
            return ValidationResult.Invalid(new ValidationIssue(
                "duplicate_module_id",
                $"Module '{module.Descriptor.ModuleId}' is already registered."));
        }

        _modules.Add(module);
        return ValidationResult.Valid;
    }

    public IGameModule? Find(GameModuleId moduleId) =>
        _modules.FirstOrDefault(module => module.Descriptor.ModuleId == moduleId);

    public ValidationResult ValidateRegistered(GameModuleId moduleId)
    {
        return Find(moduleId) is null
            ? ValidationResult.Invalid(new ValidationIssue("unknown_module_id", $"Module '{moduleId}' is not registered."))
            : ValidationResult.Valid;
    }
}

public sealed class GameSetupValidationService
{
    private readonly GameModuleRegistry _registry;

    public GameSetupValidationService(GameModuleRegistry registry)
    {
        _registry = registry;
    }

    public ValidationResult Validate(
        GameModuleId moduleId,
        GameTemplateVersion templateVersion,
        GameSetup setup,
        IReadOnlyList<ParticipantSetup> participants)
    {
        ArgumentNullException.ThrowIfNull(setup);
        ArgumentNullException.ThrowIfNull(participants);

        var module = _registry.Find(moduleId);
        if (module is null)
        {
            return ValidationResult.Invalid(new ValidationIssue("unknown_module_id", $"Module '{moduleId}' is not registered."));
        }

        var descriptor = module.Descriptor;
        if (!IsTemplateVersionCompatible(templateVersion, descriptor.MinimumTemplateVersion, descriptor.MaximumTemplateVersion))
        {
            return ValidationResult.Invalid(new ValidationIssue(
                "incompatible_template_version",
                $"Template version '{templateVersion}' is not compatible with module '{moduleId}'."));
        }

        if (!descriptor.PlayerCount.Contains(participants.Count))
        {
            return ValidationResult.Invalid(new ValidationIssue(
                "invalid_player_count",
                $"Module '{moduleId}' requires between {descriptor.PlayerCount.Minimum} and {descriptor.PlayerCount.Maximum} participants."));
        }

        var requiredMissing = descriptor.SetupFields
            .Where(field => field.IsRequired && setup.FindValue(field.Name) is null)
            .Select(field => ValidationIssue.Required(field.Name))
            .ToArray();

        if (requiredMissing.Length > 0)
        {
            return ValidationResult.FromIssues(requiredMissing);
        }

        return module.ValidateSetup(new GameSetupValidationContext(descriptor, setup, participants, templateVersion));
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
