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
    private readonly IInteractiveSessionContextService _sessionContextService;
    private readonly ILogger<OrchestratorAgent> _logger;
    private readonly int _maxToolRounds;

    public OrchestratorAgent(
        ToolLoop toolLoop,
        IEnumerable<IMode> modes,
        IPersonaStore personaStore,
        IInteractiveSessionContextService sessionContextService,
        AppConfig appConfig,
        ILogger<OrchestratorAgent> logger)
    {
        _maxToolRounds = appConfig.Agents.Orchestrator.MaxToolRounds;
        _toolLoop = toolLoop;
        _personaStore = personaStore;
        _sessionContextService = sessionContextService;
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
        var effectiveSessionContext = context.SessionContext ?? await _sessionContextService.BuildAsync(state, ct);
        var effectiveModeContext = modeContext ?? CreateModeContext(effectiveSessionContext, context.ActiveLoreSet);

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
        var effectiveSessionContext = context.SessionContext ?? await _sessionContextService.BuildAsync(state, ct);
        var effectiveModeContext = modeContext ?? CreateModeContext(effectiveSessionContext, context.ActiveLoreSet);
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

    internal string BuildSystemPrompt(string persona, IMode activeMode, ModeContext modeContext)
    {
        var modeSection = activeMode.BuildSystemPromptSection(modeContext);

        var stateSummary = string.IsNullOrWhiteSpace(modeContext.StoryStateSummary)
            ? ""
            : $"\n\n## Current Story State\n\n{modeContext.StoryStateSummary}";

        var loreSection = string.IsNullOrWhiteSpace(modeContext.ActiveLoreSet)
            ? ""
            : $"\n\n## Active Lore Set\n\nThe current lore set is \"{modeContext.ActiveLoreSet}\". "
              + "When using `query_lore`, results come from this lore set. "
              + "Ground your lore references and world-building in this set's content.";

        var fallbackGuidance = """


            ## Tool Failures

            If a tool call fails, times out, or returns an error, you MUST tell the user explicitly
            before continuing. Use an (OOC: ) note to explain which tool failed and what you are
            doing instead. Never silently absorb a tool failure and produce output as if nothing
            happened — the user needs to know when they are getting a fallback response so they
            can decide whether to retry or report the issue.
            """;

        return $"{persona}\n\n{modeSection}{stateSummary}{loreSection}{fallbackGuidance}";
    }

    private static ModeContext CreateModeContext(InteractiveSessionContext sessionContext, string? activeLoreSet)
    {
        return new ModeContext
        {
            ProjectName = sessionContext.ProjectName,
            CurrentFile = sessionContext.CurrentFile,
            CharacterSection = sessionContext.CharacterSection,
            StoryStateSummary = sessionContext.StoryStateSummary,
            FileContext = sessionContext.FileContext,
            WriterPendingContent = sessionContext.WriterPendingContent,
            ActiveLoreSet = activeLoreSet,
        };
    }
}
