using Den.RulesEngine;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Web.Services;

public sealed class GameTemplateModuleValidator : IGameTemplateModuleValidator
{
    private readonly GameModuleRegistry _registry;
    private readonly GameSetupValidationService _setupValidationService;

    public GameTemplateModuleValidator(GameModuleRegistry registry, GameSetupValidationService setupValidationService)
    {
        _registry = registry;
        _setupValidationService = setupValidationService;
    }

    public Task<GameTemplateValidationResult> ValidateAsync(GameTemplate template, CancellationToken ct = default)
    {
        var issues = new List<GameTemplateValidationIssue>();
        var moduleId = new GameModuleId(template.Module.ModuleId);
        var versionRange = new GameModuleVersionRange(
            new GameModuleVersion(template.Module.MinimumVersion),
            new GameModuleVersion(template.Module.MaximumVersion));
        var templateVersion = new GameTemplateVersion(template.TemplateVersion);
        var loadRequest = new GameModuleLoadRequest(moduleId, versionRange, templateVersion);
        var loadResult = _registry.CanLoad(loadRequest);
        if (!loadResult.IsValid)
        {
            issues.AddRange(loadResult.Issues.Select(ToTemplateIssue));
            return Task.FromResult(GameTemplateValidationResult.FromIssues(issues));
        }

        var module = _registry.FindLoadable(loadRequest)
            ?? throw new InvalidOperationException("Registry CanLoad succeeded but no loadable module was found.");
        var setup = ToGameSetup(template.RulesOptions.Values);
        var participants = ToParticipants(template.Roster);
        var setupResult = _setupValidationService.Validate(
            module.Descriptor.ModuleId,
            module.Descriptor.ModuleVersion,
            templateVersion,
            setup,
            participants);
        issues.AddRange(setupResult.Issues.Select(ToTemplateIssue));

        return Task.FromResult(GameTemplateValidationResult.FromIssues(issues));
    }

    private static GameSetup ToGameSetup(IReadOnlyList<GameTemplateRuleOptionValue> values) =>
        new(values.Select(ToGameSetupValue).ToArray());

    private static GameSetupValue ToGameSetupValue(GameTemplateRuleOptionValue value) =>
        value.Kind switch
        {
            GameTemplateRuleOptionValueKind.String => new StringGameSetupValue(value.Name, value.StringValue ?? string.Empty),
            GameTemplateRuleOptionValueKind.Int => new IntGameSetupValue(value.Name, value.IntValue ?? 0),
            GameTemplateRuleOptionValueKind.Bool => new BoolGameSetupValue(value.Name, value.BoolValue ?? false),
            GameTemplateRuleOptionValueKind.ParticipantId => new ParticipantIdGameSetupValue(value.Name, new ParticipantId(value.ParticipantIdValue ?? string.Empty)),
            GameTemplateRuleOptionValueKind.ParticipantSet => new ParticipantSetGameSetupValue(value.Name, value.ParticipantSetValue.Select(item => new ParticipantId(item)).ToArray()),
            _ => throw new ArgumentException($"Unsupported template rule option kind '{value.Kind}'.", nameof(value)),
        };

    private static IReadOnlyList<ParticipantSetup> ToParticipants(GameTemplateRosterSettings roster)
    {
        var participants = new List<ParticipantSetup>();
        var userParticipantId = string.IsNullOrWhiteSpace(roster.UserSeatParticipantId)
            ? null
            : roster.UserSeatParticipantId.Trim();
        if (userParticipantId is not null)
        {
            participants.Add(new ParticipantSetup(new ParticipantId(userParticipantId), "User", ParticipantKind.Human));
        }

        foreach (var agent in roster.AgentPlayers)
        {
            participants.Add(new ParticipantSetup(
                new ParticipantId(agent.ParticipantId),
                string.IsNullOrWhiteSpace(agent.FixedName) ? agent.ParticipantId : agent.FixedName.Trim(),
                ParticipantKind.Agent));
        }

        var nextSeat = 1;
        while (participants.Count < roster.RosterSize)
        {
            var participantId = NextAvailableSeatId(participants, nextSeat);
            participants.Add(new ParticipantSetup(new ParticipantId(participantId), participantId, ParticipantKind.Agent));
            nextSeat++;
        }

        return participants;
    }

    private static string NextAvailableSeatId(IReadOnlyList<ParticipantSetup> participants, int nextSeat)
    {
        var candidate = $"seat-{nextSeat}";
        while (participants.Any(participant => string.Equals(participant.ParticipantId.Value, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            nextSeat++;
            candidate = $"seat-{nextSeat}";
        }

        return candidate;
    }

    private static GameTemplateValidationIssue ToTemplateIssue(ValidationIssue issue) =>
        new()
        {
            Code = issue.Code,
            Message = issue.Message,
            Source = GameTemplateValidationSources.Module,
        };
}
