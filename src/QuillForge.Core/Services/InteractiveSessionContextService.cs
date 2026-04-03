using Microsoft.Extensions.Logging;
using QuillForge.Core;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed class InteractiveSessionContextService : IInteractiveSessionContextService
{
    private readonly ISessionRuntimeService _runtimeService;
    private readonly ICharacterCardStore _characterCardStore;
    private readonly IStoryStateService _storyStateService;
    private readonly IContentFileService _contentFileService;
    private readonly IPlotStore _plotStore;
    private readonly ILogger<InteractiveSessionContextService> _logger;

    public InteractiveSessionContextService(
        ISessionRuntimeService runtimeService,
        ICharacterCardStore characterCardStore,
        IStoryStateService storyStateService,
        IContentFileService contentFileService,
        IPlotStore plotStore,
        ILogger<InteractiveSessionContextService> logger)
    {
        _runtimeService = runtimeService;
        _characterCardStore = characterCardStore;
        _storyStateService = storyStateService;
        _contentFileService = contentFileService;
        _plotStore = plotStore;
        _logger = logger;
    }

    public async Task<InteractiveSessionContext> BuildAsync(
        SessionRuntimeState state,
        CancellationToken ct = default)
    {
        string? characterSection = null;
        string? storyStateSummary = null;
        string? fileContext = null;
        string? activePlotContent = null;
        string? plotProgressSummary = null;

        var projectName = state.Mode.ProjectName ?? "default";
        var storyStatePath = $"{projectName}/.state.yaml";

        if (!string.IsNullOrEmpty(state.Mode.Character))
        {
            try
            {
                var card = await _characterCardStore.LoadAsync(state.Mode.Character, ct);
                if (card is not null)
                {
                    characterSection = _characterCardStore.CardToPrompt(card);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load character card {Character}", state.Mode.Character);
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

        return new InteractiveSessionContext
        {
            ActiveModeName = state.Mode.ActiveModeName,
            ProjectName = projectName,
            StoryStatePath = storyStatePath,
            CurrentFile = state.Mode.CurrentFile,
            Character = state.Mode.Character,
            CharacterSection = characterSection,
            StoryStateSummary = storyStateSummary,
            FileContext = fileContext,
            WriterPendingContent = state.Writer.PendingContent,
            DirectorNotes = state.Narrative.DirectorNotes,
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
}
