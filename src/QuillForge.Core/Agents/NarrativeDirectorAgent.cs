using Microsoft.Extensions.Logging;
using QuillForge.Core.Agents.Tools;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents;

/// <summary>
/// Scene-level creative director for interactive modes. It decides what
/// happens next, updates story state, and delegates final prose to the
/// ProseWriter.
/// </summary>
public sealed class NarrativeDirectorAgent
{
    private readonly ToolLoop _toolLoop;
    private readonly QueryLoreHandler _queryLoreHandler;
    private readonly UpdateStoryStateHandler _updateStoryStateHandler;
    private readonly UpdateNarrativeStateHandler _updateNarrativeStateHandler;
    private readonly WriteProseHandler _writeProseHandler;
    private readonly INarrativeRulesStore _narrativeRulesStore;
    private readonly ILogger<NarrativeDirectorAgent> _logger;
    private readonly string _model;
    private readonly NarrativeDirectorBudget _budget;

    public NarrativeDirectorAgent(
        ToolLoop toolLoop,
        QueryLoreHandler queryLoreHandler,
        UpdateStoryStateHandler updateStoryStateHandler,
        UpdateNarrativeStateHandler updateNarrativeStateHandler,
        WriteProseHandler writeProseHandler,
        INarrativeRulesStore narrativeRulesStore,
        AppConfig appConfig,
        ILogger<NarrativeDirectorAgent> logger)
    {
        _toolLoop = toolLoop;
        _queryLoreHandler = queryLoreHandler;
        _updateStoryStateHandler = updateStoryStateHandler;
        _updateNarrativeStateHandler = updateNarrativeStateHandler;
        _writeProseHandler = writeProseHandler;
        _narrativeRulesStore = narrativeRulesStore;
        _logger = logger;
        _model = appConfig.Models.NarrativeDirector;
        _budget = appConfig.Agents.NarrativeDirector;
    }

    public async Task<NarrativeDirectionResult> DirectSceneAsync(
        NarrativeDirectionRequest request,
        AgentContext context,
        CancellationToken ct = default)
    {
        var sessionContext = context.SessionContext;
        var narrativeRules = await _narrativeRulesStore.LoadAsync(context.ActiveNarrativeRules, ct);
        var systemPrompt = BuildSystemPrompt(narrativeRules, context, sessionContext);

        _logger.LogInformation(
            "NarrativeDirector starting: session={SessionId}, rules={Rules}, mode={Mode}",
            context.SessionId,
            context.ActiveNarrativeRules,
            context.ActiveMode);

        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent(BuildUserTurnPrompt(request, sessionContext, context.LastAssistantResponse))),
        };

        var config = new AgentConfig
        {
            Model = _model,
            MaxTokens = _budget.MaxTokens,
            MaxToolRounds = _budget.MaxToolRounds,
            SystemPrompt = systemPrompt,
        };

        var response = await _toolLoop.RunAsync(
            config,
            [_queryLoreHandler, _updateStoryStateHandler, _updateNarrativeStateHandler, _writeProseHandler],
            messages,
            context,
            ct);

        _logger.LogInformation(
            "NarrativeDirector completed: session={SessionId}, stop={StopReason}, rounds={Rounds}",
            context.SessionId,
            response.StopReason,
            response.ToolRoundsUsed);

        return new NarrativeDirectionResult
        {
            ResponseText = response.Content.GetText(),
            ToolRoundsUsed = response.ToolRoundsUsed,
        };
    }

    public async Task<PlotGenerationResult> GeneratePlotAsync(
        PlotGenerationRequest request,
        AgentContext context,
        CancellationToken ct = default)
    {
        var sessionContext = context.SessionContext;
        var narrativeRules = await _narrativeRulesStore.LoadAsync(context.ActiveNarrativeRules, ct);
        var systemPrompt = BuildPlotPrompt(narrativeRules, context, sessionContext);

        _logger.LogInformation(
            "NarrativeDirector generating plot: session={SessionId}, rules={Rules}",
            context.SessionId,
            context.ActiveNarrativeRules);

        var prompt = string.IsNullOrWhiteSpace(request.Prompt)
            ? "Generate a reusable plot arc from the active narrative rules, character context, and lore."
            : $"Generate a reusable plot arc guided by this direction:\n{request.Prompt}";

        var response = await _toolLoop.RunAsync(
            new AgentConfig
            {
                Model = _model,
                MaxTokens = _budget.MaxTokens,
                MaxToolRounds = _budget.MaxToolRounds,
                SystemPrompt = systemPrompt,
            },
            [_queryLoreHandler],
            [new CompletionMessage("user", new MessageContent(prompt))],
            context,
            ct);

        _logger.LogInformation(
            "NarrativeDirector generated plot: session={SessionId}, stop={StopReason}, rounds={Rounds}",
            context.SessionId,
            response.StopReason,
            response.ToolRoundsUsed);

        return new PlotGenerationResult
        {
            Markdown = response.Content.GetText(),
            ToolRoundsUsed = response.ToolRoundsUsed,
        };
    }

    internal static string BuildSystemPrompt(
        string narrativeRules,
        AgentContext context,
        InteractiveSessionContext? sessionContext)
    {
        var rulesSection = string.IsNullOrWhiteSpace(narrativeRules)
            ? "No narrative rules file is active. Apply sensible scene-direction judgment."
            : narrativeRules;

        var characterSection = string.IsNullOrWhiteSpace(sessionContext?.CharacterSection)
            ? ""
            : $"\n\n## Character Context\n\n{sessionContext.CharacterSection}";

        var storyStateSection = string.IsNullOrWhiteSpace(sessionContext?.StoryStateSummary)
            ? ""
            : $"\n\n## Current Story State\n\n{sessionContext.StoryStateSummary}";

        var narrativeNotesSection = string.IsNullOrWhiteSpace(sessionContext?.DirectorNotes)
            ? ""
            : $"\n\n## Director Notes From Prior Turns\n\n{sessionContext.DirectorNotes}";

        var activePlotSection = string.IsNullOrWhiteSpace(sessionContext?.ActivePlotFile)
            ? ""
            : $"\n\n## Active Plot File\n\n{sessionContext.ActivePlotFile}";

        var activePlotContentSection = string.IsNullOrWhiteSpace(sessionContext?.ActivePlotContent)
            ? ""
            : $"\n\n## Active Plot Content\n\n{sessionContext.ActivePlotContent}";

        var plotProgressSection = string.IsNullOrWhiteSpace(sessionContext?.PlotProgressSummary)
            ? ""
            : $"\n\n## Plot Progress In This Session\n\n{sessionContext.PlotProgressSummary}";

        var fileContextSection = string.IsNullOrWhiteSpace(sessionContext?.FileContext)
            ? ""
            : $"\n\n## Recent File Context\n\n{sessionContext.FileContext}";

        var loreSection = string.IsNullOrWhiteSpace(context.ActiveLoreSet)
            ? ""
            : $"\n\n## Active Lore Set\n\nThe active lore set is \"{context.ActiveLoreSet}\".";

        return $"""
            You are the Narrative Director for an interactive fiction session.
            Your job is to decide what happens next in the scene, keep the world
            and characters coherent, update story state when the scene changes,
            and delegate the final prose to the Prose Writer.

            Responsibilities:
            - Decide the next beat of the scene based on the user's action.
            - Control NPC reactions, pacing, and immediate consequences.
            - Use `query_lore` before making specific world claims when established lore matters.
            - Use `update_story_state` when the turn changes relationships, conditions, plot pressure, or scene facts.
            - Use `update_narrative_state` to save concise running director notes for the next turn.
            - If an active plot is loaded, treat it as a reusable plan and track this session's progress or deviations separately.
            - Use `write_prose` to generate the actual player-visible response.

            Rules:
            - You do not speak as a separate assistant persona.
            - You do not output planning notes, tool commentary, or OOC framing unless a tool failure must be disclosed.
            - For roleplay turns, the final response must be only the scene prose that should be shown to the user.
            - Use `write_prose` for the final visible scene response rather than writing that prose yourself.
            - Before finishing the turn, update narrative state with concise notes that will help the next turn continue cleanly.
            - When an active plot materially advances or is bypassed, update plot progress in `update_narrative_state`.

            ## Narrative Rules

            {rulesSection}{characterSection}{storyStateSection}{narrativeNotesSection}{activePlotSection}{activePlotContentSection}{plotProgressSection}{fileContextSection}{loreSection}
            """;
    }

    internal static string BuildPlotPrompt(
        string narrativeRules,
        AgentContext context,
        InteractiveSessionContext? sessionContext)
    {
        var rulesSection = string.IsNullOrWhiteSpace(narrativeRules)
            ? "No narrative rules file is active. Build a strong, coherent plot arc from the available context."
            : narrativeRules;

        var characterSection = string.IsNullOrWhiteSpace(sessionContext?.CharacterSection)
            ? ""
            : $"\n\n## Character Context\n\n{sessionContext.CharacterSection}";

        var loreSection = string.IsNullOrWhiteSpace(context.ActiveLoreSet)
            ? ""
            : $"\n\n## Active Lore Set\n\nThe active lore set is \"{context.ActiveLoreSet}\". Use `query_lore` when world details matter.";

        return $"""
            You are the Narrative Director preparing a reusable plot arc document for an interactive fiction session.

            Responsibilities:
            - Create a markdown plot plan that can guide multiple future sessions.
            - Use `query_lore` before making specific world claims when established lore matters.
            - Produce structure that is useful to the future director: premise, beats, character arcs, and tension curve.

            Rules:
            - Output markdown only.
            - Do not write prose scenes or in-character dialogue.
            - Keep the plan reusable: it should guide a session, not recap one specific turn.
            - Favor concrete beats and turning points over vague brainstorming.

            Recommended sections:
            - Title
            - Premise
            - Core tensions
            - Major beats
            - Character arcs
            - Tension curve
            - Open threads or adaptation notes

            ## Narrative Rules

            {rulesSection}{characterSection}{loreSection}
            """;
    }

    private static string BuildUserTurnPrompt(
        NarrativeDirectionRequest request,
        InteractiveSessionContext? sessionContext,
        string? lastAssistantResponse)
    {
        var projectSection = string.IsNullOrWhiteSpace(sessionContext?.ProjectName)
            ? ""
            : $"\nProject: {sessionContext.ProjectName}";

        var fileSection = string.IsNullOrWhiteSpace(sessionContext?.CurrentFile)
            ? ""
            : $"\nCurrent file: {sessionContext.CurrentFile}";

        var lastResponseSection = string.IsNullOrWhiteSpace(lastAssistantResponse)
            ? ""
            : $"\n\nLast assistant prose response:\n{lastAssistantResponse}";

        return $"""
            Direct the next turn of the interactive scene.
            Decide what happens, update story state and narrative notes if needed, and use write_prose for the final visible response.{projectSection}{fileSection}{lastResponseSection}

            User message:
            {request.UserMessage}
            """;
    }
}
