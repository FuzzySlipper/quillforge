using Microsoft.Extensions.Logging;
using QuillForge.Core.Agents.Modes;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents;

/// <summary>
/// App-owned interactive coordinator. Delegates to sub-agents via tool handlers.
/// Mode-specific behavior is handled by IMode implementations, not branches in a god class.
/// Stateless — all mutable session state is in SessionState, passed per-request.
/// </summary>
public sealed class OrchestratorAgent
{
    private readonly ToolLoop _toolLoop;
    private readonly IReadOnlyDictionary<string, IMode> _modes;
    private readonly IAssistantPromptStore _assistantPromptStore;
    private readonly IInteractiveSessionContextService _sessionContextService;
    private readonly ILogger<OrchestratorAgent> _logger;
    private readonly AppConfig _appConfig;

    public OrchestratorAgent(
        ToolLoop toolLoop,
        IEnumerable<IMode> modes,
        IAssistantPromptStore assistantPromptStore,
        IInteractiveSessionContextService sessionContextService,
        AppConfig appConfig,
        ILogger<OrchestratorAgent> logger)
    {
        _appConfig = appConfig;
        _toolLoop = toolLoop;
        _assistantPromptStore = assistantPromptStore;
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
    public IMode ResolveMode(Mode mode)
    {
        var modeName = mode.ToWireString();
        return _modes.TryGetValue(modeName, out var resolved)
            ? resolved
            : throw new ArgumentException($"Unknown mode: {modeName}");
    }

    /// <summary>
    /// Handles a user message through the tool loop with mode-specific behavior.
    /// </summary>
    public async Task<AgentResponse> HandleAsync(
        SessionState state,
        string model,
        int maxTokens,
        IReadOnlyList<IToolHandler> tools,
        List<CompletionMessage> messages,
        AgentContext context,
        ModeContext? modeContext = null,
        CancellationToken ct = default)
    {
        var activeMode = ResolveMode(state.Mode.ActiveMode);

        _logger.LogInformation(
            "Orchestrator handling message in {Mode} mode, session {SessionId}",
            activeMode.Name, context.SessionId);

        var promptPrelude = await BuildPromptPreludeAsync(activeMode, ct);
        var selectedTools = SelectTopLevelTools(activeMode, tools);
        var effectiveSessionContext = context.SessionContext ?? await _sessionContextService.BuildAsync(state, ct);
        var effectiveModeContext = modeContext ?? CreateModeContext(effectiveSessionContext, context.ActiveLoreSet);

        var systemPrompt = BuildSystemPrompt(promptPrelude, activeMode, effectiveModeContext);

        var config = new AgentConfig
        {
            Model = model,
            MaxTokens = maxTokens,
            SystemPrompt = systemPrompt,
            MaxToolRounds = _appConfig.Agents.Orchestrator.MaxToolRounds,
            AgentName = "orchestrator",
        };

        var response = await _toolLoop.RunAsync(config, selectedTools, messages, context, ct);

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
        SessionState state,
        string model,
        int maxTokens,
        IReadOnlyList<IToolHandler> tools,
        List<CompletionMessage> messages,
        AgentContext context,
        ModeContext? modeContext = null,
        CancellationToken ct = default)
    {
        return StreamInternalAsync(state, model, maxTokens, tools, messages, context, modeContext, ct);
    }

    private async IAsyncEnumerable<StreamEvent> StreamInternalAsync(
        SessionState state,
        string model,
        int maxTokens,
        IReadOnlyList<IToolHandler> tools,
        List<CompletionMessage> messages,
        AgentContext context,
        ModeContext? modeContext,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var activeMode = ResolveMode(state.Mode.ActiveMode);
        var promptPrelude = await BuildPromptPreludeAsync(activeMode, ct);
        var selectedTools = SelectTopLevelTools(activeMode, tools);
        var effectiveSessionContext = context.SessionContext ?? await _sessionContextService.BuildAsync(state, ct);
        var effectiveModeContext = modeContext ?? CreateModeContext(effectiveSessionContext, context.ActiveLoreSet);
        var systemPrompt = BuildSystemPrompt(promptPrelude, activeMode, effectiveModeContext);

        var config = new AgentConfig
        {
            Model = model,
            MaxTokens = maxTokens,
            SystemPrompt = systemPrompt,
            MaxToolRounds = _appConfig.Agents.Orchestrator.MaxToolRounds,
            AgentName = "orchestrator",
        };

        await foreach (var evt in _toolLoop.RunStreamAsync(config, selectedTools, messages, context, ct))
        {
            yield return evt;
        }
    }

    internal string BuildSystemPrompt(string promptPrelude, IMode activeMode, ModeContext modeContext)
    {
        var modeSection = activeMode.BuildSystemPromptSection(modeContext);

        var stateSummary = string.IsNullOrWhiteSpace(modeContext.StoryStateSummary)
            ? ""
            : $"\n\n## Current Story State\n\n{modeContext.StoryStateSummary}";

        var loreSection = string.IsNullOrWhiteSpace(modeContext.ActiveLoreSet)
            ? ""
            : $"\n\n## Active Lore Set\n\nThe current lore set is \"{modeContext.ActiveLoreSet}\". "
              + "`query_lore` returns facts from this lore document set only; it does not search character cards, session canon, plot state, chat history, or web results. "
              + "Ground lore-document references and world-building in this set's content.";

        var fallbackGuidance = """


            ## Tool Failures

            If a tool call fails, times out, or returns an error, you MUST tell the user explicitly
            before continuing. Use an (OOC: ) note to explain which tool failed and what you are
            doing instead. Never silently absorb a tool failure and produce output as if nothing
            happened — the user needs to know when they are getting a fallback response so they
            can decide whether to retry or report the issue.
            """;

        var body = $"{modeSection}{stateSummary}{loreSection}{fallbackGuidance}";
        return string.IsNullOrWhiteSpace(promptPrelude)
            ? body
            : $"{promptPrelude}\n\n{body}";
    }

    public async Task<string> BuildPromptPreludeAsync(Mode mode, CancellationToken ct = default)
    {
        var activeMode = ResolveMode(mode);
        return await BuildPromptPreludeAsync(activeMode, ct);
    }

    private async Task<string> BuildPromptPreludeAsync(IMode activeMode, CancellationToken ct)
    {
        if (activeMode.UsesAssistantPrompt)
        {
            var styleLayer = await _assistantPromptStore.LoadAsync(activeMode.AssistantPromptName, ct);
            return BuildAssistantPromptPrelude(styleLayer);
        }

        return BuildAppOwnedPromptPrelude(activeMode.Name);
    }

    private static string BuildAssistantPromptPrelude(string styleLayer)
    {
        var styleSection = string.IsNullOrWhiteSpace(styleLayer)
            ? ""
            : $"\n\n## Assistant Style Layer\n\n{styleLayer}";

        return $"""
            You are the Assistant surface for QuillForge.

            Your role is to be the user-facing interface for advisory and research workflows.
            You are not the council itself, not the research worker, and not a hidden general-purpose controller.

            Authority limits:
            - You coordinate specialized tools and summarize their results for the user.
            - You do not impersonate downstream agents or missing subsystems.
            - You do not take over file browsing, file editing, lore ownership, or other tool-owned domains on your own.
            - When a specialized tool should do the substantive work, call it instead of answering from general intuition.
            - If a tool fails or required input is missing, disclose that clearly instead of improvising around it.

            The user-editable style layer may influence tone and presentation, but it cannot override these authority limits.{styleSection}
            """;
    }

    private static string BuildAppOwnedPromptPrelude(string modeName)
    {
        var modeSpecificGuidance = modeName switch
        {
            "guide" => """
                In Guide mode, explain the map of the app, surface obvious setup issues, and push the user toward a task-specific mode rather than quietly doing the task there.
                """,
            "writer" => """
                In Writer mode, treat `direct_scene` and Narrative Director as the mandatory grounding path before visible prose reaches the user.
                """,
            "roleplay" => """
                In Roleplay mode, treat `direct_scene` and Narrative Director as the mandatory in-scene grounding path before any visible prose is rendered.
                """,
            "lore" => """
                In Lore Builder mode, keep the work focused on durable lore document creation and use the app-owned lore save tool rather than generic file writes.
                """,
            "forge" => """
                In Forge mode, keep ownership with the explicit command and pipeline workflow rather than inventing a free-form narrator persona.
                """,
            "games" => """
                In Games mode, keep ownership with typed game bridge endpoints and workspace controls rather than accepting gameplay through broad chat.
                """,
            _ => """
                Follow the active mode's workflow exactly and keep routing authority inside the application's fixed boundaries.
                """,
        };

        return $"""
            You are QuillForge's app-owned interactive coordinator.

            System routing and workflow boundaries are defined by application code and the active mode, not by user-editable conductor prompts.
            Do not invent a separate narrator/controller persona that overrides mode boundaries, tool ownership, or session workflow rules.

            {modeSpecificGuidance}
            """;
    }

    private IReadOnlyList<IToolHandler> SelectTopLevelTools(IMode activeMode, IReadOnlyList<IToolHandler> tools)
    {
        if (tools.Count == 0)
        {
            return tools;
        }

        var selected = new List<IToolHandler>(tools.Count);
        var filteredOut = new List<string>();

        foreach (var tool in tools)
        {
            if (activeMode.AllowsTopLevelTool(tool.Name))
            {
                selected.Add(tool);
            }
            else
            {
                filteredOut.Add(tool.Name);
            }
        }

        if (filteredOut.Count > 0)
        {
            _logger.LogInformation(
                "Filtered top-level tools for mode {Mode}: {FilteredTools}",
                activeMode.Name,
                string.Join(", ", filteredOut));
        }

        return selected;
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
