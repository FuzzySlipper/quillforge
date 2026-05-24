using Microsoft.Extensions.Logging;
using QuillForge.Core;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class InteractiveSessionContextService : IInteractiveSessionContextService
{
    private const int RecentConversationMessageLimit = 12;
    private const int RecentConversationMessageTrimLength = 280;

    private readonly ISessionStateService _runtimeService;
    private readonly ISessionStore _sessionStore;
    private readonly ICharacterCardStore _characterCardStore;
    private readonly IStoryStateService _storyStateService;
    private readonly IContentFileService _contentFileService;
    private readonly IPlotStore _plotStore;
    private readonly ILogger<InteractiveSessionContextService> _logger;

    public InteractiveSessionContextService(
        ISessionStateService runtimeService,
        ISessionStore sessionStore,
        ICharacterCardStore characterCardStore,
        IStoryStateService storyStateService,
        IContentFileService contentFileService,
        IPlotStore plotStore,
        ILogger<InteractiveSessionContextService> logger)
    {
        _runtimeService = runtimeService;
        _sessionStore = sessionStore;
        _characterCardStore = characterCardStore;
        _storyStateService = storyStateService;
        _contentFileService = contentFileService;
        _plotStore = plotStore;
        _logger = logger;
    }

    public async Task<InteractiveSessionContext> BuildAsync(
        SessionState state,
        CancellationToken ct = default)
    {
        string? characterSection = null;
        string? userCharacterSection = null;
        string? storyStateSummary = null;
        string? fileContext = null;
        string? activePlotContent = null;
        string? plotProgressSummary = null;
        string? recentConversationSummary = null;

        var projectName = state.Mode.ProjectName ?? "default";
        var storyStatePath = $"{projectName}/.state.yaml";

        if (!string.IsNullOrEmpty(state.Mode.Character))
        {
            try
            {
                var card = await _characterCardStore.LoadAsync(state.Mode.Character, ct);
                if (card is not null)
                {
                    var rawSection = _characterCardStore.CardToPrompt(card);
                    characterSection = RoleplayShortcodeResolver.Substitute(
                        rawSection,
                        charName: card.Name,
                        userName: state.Roleplay.ActiveUserCharacter);

                    var unresolved = RoleplayShortcodeResolver.FindUnresolved(characterSection);
                    if (unresolved.Count > 0)
                    {
                        _logger.LogWarning(
                            "Unresolved roleplay shortcodes in character card {Character}: {Shortcodes}",
                            state.Mode.Character,
                            string.Join(", ", unresolved));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load character card {Character}", state.Mode.Character);
            }
        }

        if (!string.IsNullOrEmpty(state.Roleplay.ActiveUserCharacter))
        {
            try
            {
                var userCard = await _characterCardStore.LoadAsync(state.Roleplay.ActiveUserCharacter, ct);
                if (userCard is not null)
                {
                    var rawSection = _characterCardStore.CardToPrompt(userCard);
                    userCharacterSection = RoleplayShortcodeResolver.Substitute(
                        rawSection,
                        charName: userCard.Name,
                        userName: state.Mode.Character);

                    var unresolved = RoleplayShortcodeResolver.FindUnresolved(userCharacterSection);
                    if (unresolved.Count > 0)
                    {
                        _logger.LogWarning(
                            "Unresolved roleplay shortcodes in user character card {Character}: {Shortcodes}",
                            state.Roleplay.ActiveUserCharacter,
                            string.Join(", ", unresolved));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load user character card {Character}", state.Roleplay.ActiveUserCharacter);
            }
        }

        try
        {
            var storyState = await _storyStateService.LoadAsync(storyStatePath, ct);
            if (storyState.Count > 0)
            {
                var lines = storyState.Select(kv => $"- {kv.Key}: {kv.Value}");
                storyStateSummary = string.Join("\n", lines);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No story state found for project {Project}", projectName);
        }

        if (!string.IsNullOrEmpty(state.Mode.ProjectName) && !string.IsNullOrEmpty(state.Mode.CurrentFile))
        {
            try
            {
                var filePath = $"{ContentPaths.Story}/{state.Mode.ProjectName}/{state.Mode.CurrentFile}";
                if (await _contentFileService.ExistsAsync(filePath, ct))
                {
                    var content = await _contentFileService.ReadAsync(filePath, ct);
                    if (content.Length > 500)
                    {
                        fileContext = "...\n" + content[^500..];
                    }
                    else if (!string.IsNullOrWhiteSpace(content))
                    {
                        fileContext = content;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to load file context for {File}", state.Mode.CurrentFile);
            }
        }

        if (!string.IsNullOrWhiteSpace(state.Narrative.ActivePlotFile))
        {
            try
            {
                activePlotContent = await _plotStore.LoadAsync(state.Narrative.ActivePlotFile, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to load active plot file {Plot}", state.Narrative.ActivePlotFile);
            }
        }

        plotProgressSummary = BuildPlotProgressSummary(state.Narrative.PlotProgress);
        recentConversationSummary = await LoadRecentConversationSummaryAsync(
            state.SessionId,
            !string.IsNullOrWhiteSpace(state.Narrative.StickySessionCanon),
            ct);

        return new InteractiveSessionContext
        {
            ActiveMode = state.Mode.ActiveMode,
            ProjectName = projectName,
            StoryStatePath = storyStatePath,
            CurrentFile = state.Mode.CurrentFile,
            Character = state.Mode.Character,
            CharacterSection = characterSection,
            UserCharacter = state.Roleplay.ActiveUserCharacter,
            UserCharacterSection = userCharacterSection,
            StoryStateSummary = storyStateSummary,
            FileContext = fileContext,
            WriterPendingContent = state.Writer.PendingContent,
            DirectorNotes = state.Narrative.DirectorNotes,
            StickySessionCanon = state.Narrative.StickySessionCanon,
            RecentConversationSummary = recentConversationSummary,
            ActivePlotFile = state.Narrative.ActivePlotFile,
            ActivePlotContent = activePlotContent,
            PlotProgressSummary = plotProgressSummary,
        };
    }

    public async Task<InteractiveSessionContext> LoadAsync(
        Guid? sessionId,
        CancellationToken ct = default)
    {
        var state = await _runtimeService.LoadViewAsync(sessionId, ct);
        return await BuildAsync(state, ct);
    }

    private async Task<string?> LoadRecentConversationSummaryAsync(
        Guid? sessionId,
        bool hasStickySessionCanon,
        CancellationToken ct)
    {
        if (!sessionId.HasValue)
        {
            return null;
        }

        try
        {
            var tree = await _sessionStore.LoadAsync(sessionId.Value, ct);
            var relevantMessages = GetRelevantConversationMessages(tree.ToFlatThread());
            if (relevantMessages.Count > RecentConversationMessageLimit && !hasStickySessionCanon)
            {
                _logger.LogInformation(
                    "Recent conversation summary truncated without sticky canon present: session={SessionId} relevantMessageCount={MessageCount} limit={Limit}",
                    sessionId,
                    relevantMessages.Count,
                    RecentConversationMessageLimit);
            }

            return BuildRecentConversationSummary(relevantMessages);
        }
        catch (FileNotFoundException ex)
        {
            _logger.LogDebug(ex, "No conversation thread found for session {SessionId}", sessionId);
            return null;
        }
    }

    private static string? BuildPlotProgressSummary(PlotProgressState progress)
    {
        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(progress.CurrentBeat))
        {
            lines.Add($"Current beat: {progress.CurrentBeat}");
        }

        if (progress.CompletedBeats.Count > 0)
        {
            lines.Add("Completed beats:");
            lines.AddRange(progress.CompletedBeats.Select(beat => $"- {beat}"));
        }

        if (progress.Deviations.Count > 0)
        {
            lines.Add("Deviations:");
            lines.AddRange(progress.Deviations.Select(deviation => $"- {deviation}"));
        }

        if (lines.Count == 0)
        {
            return null;
        }

        return string.Join("\n", lines);
    }

    private static List<MessageNode> GetRelevantConversationMessages(IReadOnlyList<MessageNode> thread)
    {
        var relevantMessages = new List<MessageNode>();

        foreach (var node in thread)
        {
            if (string.Equals(node.Role, "user", StringComparison.OrdinalIgnoreCase)
                || string.Equals(node.Role, "assistant", StringComparison.OrdinalIgnoreCase))
            {
                relevantMessages.Add(node);
            }
        }

        return relevantMessages;
    }

    private static string? BuildRecentConversationSummary(IReadOnlyList<MessageNode> relevantMessages)
    {
        if (relevantMessages.Count == 0)
        {
            return null;
        }

        var lines = new List<string>();
        var recentMessages = relevantMessages.TakeLast(RecentConversationMessageLimit);

        foreach (var node in recentMessages)
        {
            var content = node.Content.GetText().ReplaceLineEndings(" ").Trim();
            if (string.IsNullOrWhiteSpace(content))
            {
                continue;
            }

            if (content.Length > RecentConversationMessageTrimLength)
            {
                content = TrimRecentConversationMessage(content);
            }

            var label = string.Equals(node.Role, "user", StringComparison.OrdinalIgnoreCase)
                ? "User"
                : "Assistant";
            lines.Add($"{label}: {content}");
        }

        return lines.Count == 0 ? null : string.Join("\n", lines);
    }

    private static string TrimRecentConversationMessage(string content)
    {
        if (content.Length <= RecentConversationMessageTrimLength)
        {
            return content;
        }

        var candidate = content[..RecentConversationMessageTrimLength];
        var lastSpace = candidate.LastIndexOf(' ');
        var lastTab = candidate.LastIndexOf('\t');
        var lastWhitespace = Math.Max(lastSpace, lastTab);
        if (lastWhitespace > RecentConversationMessageTrimLength / 2)
        {
            candidate = candidate[..lastWhitespace];
        }

        return candidate.TrimEnd() + "...";
    }
}
