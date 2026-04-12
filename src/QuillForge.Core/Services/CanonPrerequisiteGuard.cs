using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

/// <summary>
/// Validates canon/configuration prerequisites for grounded scene and prose flows.
/// Fails loudly when required lore, rules, styles, or character context are missing.
/// </summary>
public sealed class CanonPrerequisiteGuard
{
    private readonly ILoreStore _loreStore;
    private readonly IContentFileService _contentFileService;
    private readonly INarrativeRulesStore _narrativeRulesStore;
    private readonly IWritingStyleStore _writingStyleStore;
    private readonly ILogger<CanonPrerequisiteGuard> _logger;

    public CanonPrerequisiteGuard(
        ILoreStore loreStore,
        IContentFileService contentFileService,
        INarrativeRulesStore narrativeRulesStore,
        IWritingStyleStore writingStyleStore,
        ILogger<CanonPrerequisiteGuard> logger)
    {
        _loreStore = loreStore;
        _contentFileService = contentFileService;
        _narrativeRulesStore = narrativeRulesStore;
        _writingStyleStore = writingStyleStore;
        _logger = logger;
    }

    public async Task EnsureQueryableLoreAvailableAsync(
        AgentContext context,
        string operation,
        CancellationToken ct = default)
    {
        if (await HasQueryableLoreAsync(context, ct))
        {
            return;
        }

        throw CreateFailure(
            context,
            $"Cannot {operation} because the active lore set \"{context.ActiveLoreSet}\" is missing or empty. " +
            $"Add markdown files under `{ContentPaths.Lore}/{context.ActiveLoreSet}/`, switch to a populated lore set, " +
            "or fix the session/profile configuration before retrying.");
    }

    public async Task<bool> HasQueryableLoreAsync(
        AgentContext context,
        CancellationToken ct = default)
    {
        if (await HasRunLoreAsync(context, ct))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(context.ActiveLoreSet))
        {
            return false;
        }

        var loreContent = await _loreStore.LoadLoreSetAsync(context.ActiveLoreSet, ct);
        return loreContent.Count > 0;
    }

    public async Task<string> RequireNarrativeRulesAsync(
        AgentContext context,
        string operation,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(context.ActiveNarrativeRules))
        {
            throw CreateFailure(
                context,
                $"Cannot {operation} because no active narrative-rules file is configured. " +
                $"Select a populated rules file or add one under `{ContentPaths.NarrativeRules}/` before retrying.");
        }

        var narrativeRules = await _narrativeRulesStore.LoadAsync(context.ActiveNarrativeRules, ct);
        if (!string.IsNullOrWhiteSpace(narrativeRules))
        {
            return narrativeRules;
        }

        throw CreateFailure(
            context,
            $"Cannot {operation} because the active narrative-rules file \"{context.ActiveNarrativeRules}\" is missing or empty. " +
            $"Add content under `{ContentPaths.NarrativeRules}/{context.ActiveNarrativeRules}.md` or switch to a populated rules file before retrying.");
    }

    public async Task<string> RequireWritingStyleAsync(
        string writingStyleName,
        AgentContext context,
        string operation,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(writingStyleName))
        {
            throw CreateFailure(
                context,
                $"Cannot {operation} because no active writing style is configured. " +
                $"Select a populated writing style or add one under `{ContentPaths.WritingStyles}/` before retrying.");
        }

        var writingStyle = await _writingStyleStore.LoadAsync(writingStyleName, ct);
        if (!string.IsNullOrWhiteSpace(writingStyle))
        {
            return writingStyle;
        }

        throw CreateFailure(
            context,
            $"Cannot {operation} because the active writing style \"{writingStyleName}\" is missing or empty. " +
            $"Add content under `{ContentPaths.WritingStyles}/{writingStyleName}.md` or switch to a populated writing style before retrying.");
    }

    public void EnsureRoleplayCharacterContextAvailable(
        AgentContext context,
        InteractiveSessionContext? sessionContext,
        string operation)
    {
        if (context.ActiveMode != Mode.Roleplay)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(sessionContext?.Character))
        {
            // No selected character is a valid roleplay setup; only fail when a
            // specific character was selected but its card context could not load.
            return;
        }

        if (!string.IsNullOrWhiteSpace(sessionContext.CharacterSection))
        {
            return;
        }

        throw CreateFailure(
            context,
            $"Cannot {operation} because the selected roleplay character \"{sessionContext.Character}\" could not be loaded. " +
            $"Restore the character card under `{ContentPaths.CharacterCards}/` or pick a different character before retrying.");
    }

    private async Task<bool> HasRunLoreAsync(AgentContext context, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(context.RunLorePath))
        {
            return false;
        }

        if (!await _contentFileService.ExistsAsync(context.RunLorePath, ct))
        {
            return false;
        }

        var runLore = await _contentFileService.ReadAsync(context.RunLorePath, ct);
        return !string.IsNullOrWhiteSpace(runLore);
    }

    private CanonPrerequisiteException CreateFailure(AgentContext context, string message)
    {
        _logger.LogWarning(
            "Canon prerequisite failed: session={SessionId}, mode={Mode}, lore={LoreSet}, rules={Rules}, style={Style}, message={Message}",
            context.SessionId,
            context.ActiveMode,
            context.ActiveLoreSet,
            context.ActiveNarrativeRules,
            context.ActiveWritingStyle,
            message);

        return new CanonPrerequisiteException(message);
    }
}

public sealed class CanonPrerequisiteException : InvalidOperationException
{
    public CanonPrerequisiteException(string message)
        : base(message)
    {
    }
}
