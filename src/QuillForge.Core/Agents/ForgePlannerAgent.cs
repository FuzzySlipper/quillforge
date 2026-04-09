using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Agents;

/// <summary>
/// The Forge Planner designs story structure: outline, style guide, story bible,
/// per-chapter briefs, and character bios. Uses specialized file-writing tools.
/// </summary>
public sealed class ForgePlannerAgent
{
    private readonly ToolLoop _toolLoop;
    private readonly ILogger<ForgePlannerAgent> _logger;
    private readonly string _model;
    private readonly ForgePlannerBudget _budget;

    public ForgePlannerAgent(ToolLoop toolLoop, AppConfig appConfig, ILogger<ForgePlannerAgent> logger)
    {
        _toolLoop = toolLoop;
        _logger = logger;
        _model = appConfig.Models.ForgePlanner;
        _budget = appConfig.Agents.ForgePlanner;
    }

    /// <summary>
    /// Runs the planning phase, producing outline, style, bible, and chapter briefs.
    /// </summary>
    public async Task<AgentResponse> PlanAsync(
        string premise,
        string loreContext,
        IReadOnlyList<IToolHandler> tools,
        AgentContext context,
        string? customPrompt = null,
        CancellationToken ct = default,
        Action<string>? progress = null)
    {
        _logger.LogInformation("ForgePlanner starting for session {SessionId}", context.SessionId);
        progress?.Invoke($"ForgePlanner starting (model={_model}, maxTokens={_budget.MaxTokens})");

        var systemPrompt = customPrompt ?? DefaultPlannerPrompt;

        var config = new AgentConfig
        {
            Model = _model,
            MaxTokens = _budget.MaxTokens,
            SystemPrompt = systemPrompt,
            MaxToolRounds = _budget.MaxToolRounds,
            AgentName = "forge-planner",
        };

        var messages = new List<CompletionMessage>
        {
            new("user", new MessageContent(
                $"## Premise\n\n{premise}\n\n## Available Lore\n\n{loreContext}")),
        };

        var response = await _toolLoop.RunAsync(config, tools, messages, context, ct, progress);

        _logger.LogInformation(
            "ForgePlanner completed: {Rounds} tool rounds used",
            response.ToolRoundsUsed);
        progress?.Invoke($"ForgePlanner completed: {response.ToolRoundsUsed} rounds");

        return response;
    }

    internal const string DefaultPlannerPrompt = """
        You are a master story architect. Your job is to take a premise and available lore,
        then design a complete story structure. You MUST create the following artifacts using
        the provided tools:

        1. **outline.md** — Full plot arc with chapter summaries
        2. **style.md** — Narrative voice specification (POV, tense, tone, pacing)
        3. **bible.md** — Timeline, relationships, world rules, constraints
        4. **ch-NN-brief.md** — Per-chapter implementation specs with plot beats, character arcs,
           foreshadowing cues, and target word count

        Also create character bio files in lore/ for any new characters.

        Be thorough and specific. Each chapter brief should be detailed enough for a writer
        to implement without access to the full outline.

        IMPORTANT: Do NOT embed file paths or file references in any planning document. All documents
        should be self-contained prose. The writing pipeline retrieves lore and character details at
        runtime via query_lore — it does not follow file path references embedded in documents.

        IMPORTANT: The "Available Lore" section in the user message already contains the FULL
        content of all lore files. Do NOT use read_file or list_files to re-read lore —
        everything you need is already in context. Start writing plan artifacts immediately.
        Only use read_file to verify your own previously-written plan files if needed.
        """;
}
