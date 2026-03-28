using Microsoft.Extensions.Logging;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents;

/// <summary>
/// The user's conversational partner. Delegates to sub-agents via tool handlers.
/// Mode-specific behavior is handled by IMode implementations, not branches in a god class.
/// </summary>
public sealed class OrchestratorAgent
{
    private readonly ToolLoop _toolLoop;
    private readonly IReadOnlyDictionary<string, IMode> _modes;
    private readonly IPersonaStore _personaStore;
    private readonly ILogger<OrchestratorAgent> _logger;

    private IMode _activeMode;
    private string? _projectName;
    private string? _currentFile;
    private string? _character;

    public OrchestratorAgent(
        ToolLoop toolLoop,
        IEnumerable<IMode> modes,
        IPersonaStore personaStore,
        ILogger<OrchestratorAgent> logger)
    {
        _toolLoop = toolLoop;
        _personaStore = personaStore;
        _logger = logger;

        var modeDict = new Dictionary<string, IMode>(StringComparer.OrdinalIgnoreCase);
        foreach (var mode in modes)
        {
            modeDict[mode.Name] = mode;
        }
        _modes = modeDict;

        _activeMode = modeDict.GetValueOrDefault("general")
            ?? throw new InvalidOperationException("GeneralMode must be registered.");
    }

    public string ActiveModeName => _activeMode.Name;
    public string? ProjectName => _projectName;
    public string? CurrentFile => _currentFile;
    public string? Character => _character;

    /// <summary>
    /// Returns pending content if the active mode is Writer and has content awaiting review.
    /// </summary>
    public string? WriterPendingContent =>
        _activeMode is WriterMode w ? w.PendingContent : null;

    /// <summary>
    /// Switches to a new mode. Resets mode-specific state.
    /// </summary>
    public void SetMode(string modeName, string? projectName = null, string? fileName = null, string? character = null)
    {
        if (!_modes.TryGetValue(modeName, out var mode))
        {
            throw new ArgumentException($"Unknown mode: {modeName}");
        }

        _logger.LogInformation(
            "Orchestrator switching mode: {OldMode} → {NewMode}, project={Project}, file={File}",
            _activeMode.Name, modeName, projectName, fileName);

        // Reset the old mode if it has state
        if (_activeMode is WriterMode writerMode)
        {
            writerMode.Reset();
        }

        _activeMode = mode;
        _projectName = projectName;
        _currentFile = fileName;
        _character = character;
    }

    /// <summary>
    /// Handles a user message through the tool loop with mode-specific behavior.
    /// </summary>
    public async Task<AgentResponse> HandleAsync(
        string personaName,
        string model,
        int maxTokens,
        IReadOnlyList<IToolHandler> tools,
        List<CompletionMessage> messages,
        AgentContext context,
        ModeContext? modeContext = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Orchestrator handling message in {Mode} mode, session {SessionId}",
            _activeMode.Name, context.SessionId);

        var persona = await _personaStore.LoadAsync(personaName, ct: ct);
        var effectiveModeContext = modeContext ?? new ModeContext
        {
            ProjectName = _projectName,
            CurrentFile = _currentFile,
        };

        var systemPrompt = BuildSystemPrompt(persona, effectiveModeContext);

        var config = new AgentConfig
        {
            Model = model,
            MaxTokens = maxTokens,
            SystemPrompt = systemPrompt,
            MaxToolRounds = 15,
        };

        var response = await _toolLoop.RunAsync(config, tools, messages, context, ct);

        // Mode-specific post-processing
        await _activeMode.OnResponseAsync(response, effectiveModeContext, ct);

        _logger.LogInformation(
            "Orchestrator completed: mode={Mode}, stop={StopReason}, rounds={Rounds}",
            _activeMode.Name, response.StopReason, response.ToolRoundsUsed);

        return response;
    }

    /// <summary>
    /// Streams a response. Tool dispatch rounds are non-streaming; final text is streamed.
    /// </summary>
    public IAsyncEnumerable<StreamEvent> HandleStreamAsync(
        string personaName,
        string model,
        int maxTokens,
        IReadOnlyList<IToolHandler> tools,
        List<CompletionMessage> messages,
        AgentContext context,
        ModeContext? modeContext = null,
        CancellationToken ct = default)
    {
        // For streaming, we build the same config but use RunStreamAsync
        // Note: persona load is sync here since we can't await in the iterator method signature.
        // The caller should pre-load the persona or we handle it in the first iteration.
        var effectiveModeContext = modeContext ?? new ModeContext
        {
            ProjectName = _projectName,
            CurrentFile = _currentFile,
        };

        return StreamInternalAsync(personaName, model, maxTokens, tools, messages, context, effectiveModeContext, ct);
    }

    private async IAsyncEnumerable<StreamEvent> StreamInternalAsync(
        string personaName,
        string model,
        int maxTokens,
        IReadOnlyList<IToolHandler> tools,
        List<CompletionMessage> messages,
        AgentContext context,
        ModeContext modeContext,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var persona = await _personaStore.LoadAsync(personaName, ct: ct);
        var systemPrompt = BuildSystemPrompt(persona, modeContext);

        var config = new AgentConfig
        {
            Model = model,
            MaxTokens = maxTokens,
            SystemPrompt = systemPrompt,
            MaxToolRounds = 15,
        };

        await foreach (var evt in _toolLoop.RunStreamAsync(config, tools, messages, context, ct))
        {
            yield return evt;
        }
    }

    internal string BuildSystemPrompt(string persona, ModeContext modeContext)
    {
        var modeSection = _activeMode.BuildSystemPromptSection(modeContext);

        var stateSummary = string.IsNullOrWhiteSpace(modeContext.StoryStateSummary)
            ? ""
            : $"\n\n## Current Story State\n\n{modeContext.StoryStateSummary}";

        return $"{persona}\n\n{modeSection}{stateSummary}";
    }
}
