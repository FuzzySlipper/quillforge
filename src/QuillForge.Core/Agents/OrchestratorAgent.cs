using Microsoft.Extensions.Logging;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents;

/// <summary>
/// The user's conversational partner. Delegates to sub-agents via tool handlers.
/// Mode-specific behavior is handled by IMode implementations, not branches in a god class.
/// Stateless — all mutable session state is in SessionRuntimeState, passed per-request.
/// </summary>
public sealed class OrchestratorAgent
{
    private readonly ToolLoop _toolLoop;
    private readonly IReadOnlyDictionary<string, IMode> _modes;
    private readonly IPersonaStore _personaStore;
    private readonly ICharacterCardStore _characterCardStore;
    private readonly IStoryStateService _storyStateService;
    private readonly IContentFileService _contentFileService;
    private readonly ILogger<OrchestratorAgent> _logger;
    private readonly int _maxToolRounds;

    public OrchestratorAgent(
        ToolLoop toolLoop,
        IEnumerable<IMode> modes,
        IPersonaStore personaStore,
        ICharacterCardStore characterCardStore,
        IStoryStateService storyStateService,
        IContentFileService contentFileService,
        AppConfig appConfig,
        ILogger<OrchestratorAgent> logger)
    {
        _maxToolRounds = appConfig.Agents.Orchestrator.MaxToolRounds;
        _toolLoop = toolLoop;
        _personaStore = personaStore;
        _characterCardStore = characterCardStore;
        _storyStateService = storyStateService;
        _contentFileService = contentFileService;
        _logger = logger;

        var modeDict = new Dictionary<string, IMode>(StringComparer.OrdinalIgnoreCase);
        foreach (var mode in modes)
        {
            modeDict[mode.Name] = mode;
        }
        _modes = modeDict;
    }

    /// <summary>
    /// Resolves a mode by name. Throws if not found.
    /// </summary>
    public IMode ResolveMode(string modeName)
    {
        return _modes.TryGetValue(modeName, out var mode)
            ? mode
            : throw new ArgumentException($"Unknown mode: {modeName}");
    }

    /// <summary>
    /// Switches mode in the given runtime state. Resets writer state if leaving writer mode.
    /// </summary>
    public void SetMode(SessionRuntimeState state, string modeName, string? projectName = null, string? fileName = null, string? character = null)
    {
        // Validate the mode exists
        ResolveMode(modeName);

        _logger.LogInformation(
            "Orchestrator switching mode: {OldMode} → {NewMode}, project={Project}, file={File}",
            state.Mode.ActiveModeName, modeName, projectName, fileName);

        // Reset writer state if leaving writer mode
        if (string.Equals(state.Mode.ActiveModeName, "writer", StringComparison.OrdinalIgnoreCase))
        {
            WriterMode.Reset(state.Writer);
        }

        state.Mode.ActiveModeName = modeName;
        state.Mode.ProjectName = projectName;
        state.Mode.CurrentFile = fileName;
        state.Mode.Character = character;
    }

    /// <summary>
    /// Handles a user message through the tool loop with mode-specific behavior.
    /// </summary>
    public async Task<AgentResponse> HandleAsync(
        SessionRuntimeState state,
        string personaName,
        string model,
        int maxTokens,
        IReadOnlyList<IToolHandler> tools,
        List<CompletionMessage> messages,
        AgentContext context,
        ModeContext? modeContext = null,
        CancellationToken ct = default)
    {
        var activeMode = ResolveMode(state.Mode.ActiveModeName);

        _logger.LogInformation(
            "Orchestrator handling message in {Mode} mode, session {SessionId}",
            activeMode.Name, context.SessionId);

        var persona = await _personaStore.LoadAsync(personaName, ct: ct);
        var effectiveModeContext = modeContext ?? await HydrateModeContextAsync(state, ct);

        var systemPrompt = BuildSystemPrompt(persona, activeMode, effectiveModeContext);

        var config = new AgentConfig
        {
            Model = model,
            MaxTokens = maxTokens,
            SystemPrompt = systemPrompt,
            MaxToolRounds = _maxToolRounds,
        };

        var response = await _toolLoop.RunAsync(config, tools, messages, context, ct);

        // Mode-specific post-processing
        await activeMode.OnResponseAsync(response, effectiveModeContext, ct);

        // Writer mode: capture pending content
        if (activeMode is WriterMode)
        {
            WriterMode.CaptureIfPending(response, state.Writer, _logger);
        }

        _logger.LogInformation(
            "Orchestrator completed: mode={Mode}, stop={StopReason}, rounds={Rounds}",
            activeMode.Name, response.StopReason, response.ToolRoundsUsed);

        return response;
    }

    /// <summary>
    /// Streams a response. Tool dispatch rounds are non-streaming; final text is streamed.
    /// </summary>
    public IAsyncEnumerable<StreamEvent> HandleStreamAsync(
        SessionRuntimeState state,
        string personaName,
        string model,
        int maxTokens,
        IReadOnlyList<IToolHandler> tools,
        List<CompletionMessage> messages,
        AgentContext context,
        ModeContext? modeContext = null,
        CancellationToken ct = default)
    {
        return StreamInternalAsync(state, personaName, model, maxTokens, tools, messages, context, modeContext, ct);
    }

    private async IAsyncEnumerable<StreamEvent> StreamInternalAsync(
        SessionRuntimeState state,
        string personaName,
        string model,
        int maxTokens,
        IReadOnlyList<IToolHandler> tools,
        List<CompletionMessage> messages,
        AgentContext context,
        ModeContext? modeContext,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var activeMode = ResolveMode(state.Mode.ActiveModeName);
        var persona = await _personaStore.LoadAsync(personaName, ct: ct);
        var effectiveModeContext = modeContext ?? await HydrateModeContextAsync(state, ct);
        var systemPrompt = BuildSystemPrompt(persona, activeMode, effectiveModeContext);

        var config = new AgentConfig
        {
            Model = model,
            MaxTokens = maxTokens,
            SystemPrompt = systemPrompt,
            MaxToolRounds = _maxToolRounds,
        };

        await foreach (var evt in _toolLoop.RunStreamAsync(config, tools, messages, context, ct))
        {
            yield return evt;
        }
    }

    private async Task<ModeContext> HydrateModeContextAsync(SessionRuntimeState state, CancellationToken ct)
    {
        string? characterSection = null;
        string? storyStateSummary = null;
        string? fileContext = null;

        // Load character card if one is selected
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

        // Load story state summary if a project is active
        var project = state.Mode.ProjectName ?? "default";
        try
        {
            var statePath = $"{project}/.state.yaml";
            var storyState = await _storyStateService.LoadAsync(statePath, ct);
            if (storyState.Count > 0)
            {
                var lines = storyState.Select(kv => $"- {kv.Key}: {kv.Value}");
                storyStateSummary = string.Join("\n", lines);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No story state found for project {Project}", project);
        }

        // Load recent file context if a current file is set
        if (!string.IsNullOrEmpty(state.Mode.ProjectName) && !string.IsNullOrEmpty(state.Mode.CurrentFile))
        {
            try
            {
                var filePath = $"story/{state.Mode.ProjectName}/{state.Mode.CurrentFile}";
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

        return new ModeContext
        {
            ProjectName = state.Mode.ProjectName,
            CurrentFile = state.Mode.CurrentFile,
            CharacterSection = characterSection,
            StoryStateSummary = storyStateSummary,
            FileContext = fileContext,
            WriterPendingContent = state.Writer.PendingContent,
        };
    }

    internal string BuildSystemPrompt(string persona, IMode activeMode, ModeContext modeContext)
    {
        var modeSection = activeMode.BuildSystemPromptSection(modeContext);

        var stateSummary = string.IsNullOrWhiteSpace(modeContext.StoryStateSummary)
            ? ""
            : $"\n\n## Current Story State\n\n{modeContext.StoryStateSummary}";

        return $"{persona}\n\n{modeSection}{stateSummary}";
    }
}
