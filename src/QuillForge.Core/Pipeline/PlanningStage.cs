using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Pipeline;

/// <summary>
/// Stage 1: ForgePlanner produces the story outline, style guide, bible, and chapter briefs.
/// </summary>
public sealed class PlanningStage : IPipelineStage
{
    private readonly ILogger<PlanningStage> _logger;

    public PlanningStage(ILogger<PlanningStage> logger)
    {
        _logger = logger;
    }

    public string StageName => "Planning";
    public ForgeStage StageEnum => ForgeStage.Planning;

    public async IAsyncEnumerable<ForgeEvent> ExecuteAsync(
        ForgeContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogInformation("Planning stage starting for project {Project}", context.Manifest.ProjectName);
        yield return new StageStartedEvent(StageName);
        context.Log("Loading premise file...", "planning");

        var premise = await LoadPremiseAsync(context, ct);
        context.Log($"Premise loaded ({premise.Length} chars)", "planning");

        var loreLen = context.LoreContext.Length;
        context.Log($"Lore context: {loreLen} chars, {context.PlannerTools.Count} tools available", "planning");
        context.Log("Calling ForgePlanner agent (this may take several minutes)...", "planning");

        var response = await context.Planner.PlanAsync(
            premise,
            context.LoreContext,
            context.PlannerTools,
            context.AgentContext,
            ct: ct,
            progress: msg => context.Log(msg, "planner"));

        context.StatsTracker.RecordCompletion("forge-planner", response.Usage);

        if (response.StopReason == StopReason.Error)
        {
            var error = response.Content.GetText();
            _logger.LogWarning("Planning stage stopped after tool-loop error: {Error}", error);
            context.Log($"Planning failed: {error}", "planning");
            yield return new ForgeErrorEvent(error, StageName);
            yield break;
        }

        context.Log(
            $"Planning complete — {response.ToolRoundsUsed} rounds, " +
            $"{response.Usage.InputTokens + response.Usage.OutputTokens} total tokens",
            "planning");

        _logger.LogInformation(
            "Planning stage completed: {Rounds} tool rounds",
            response.ToolRoundsUsed);

        yield return new StageCompletedEvent(StageName);
    }

    private static async Task<string> LoadPremiseAsync(ForgeContext context, CancellationToken ct)
    {
        var premisePath = $"forge/{context.Manifest.ProjectName}/plan/premise.md";
        try
        {
            return await context.FileService.ReadAsync(premisePath, ct);
        }
        catch (FileNotFoundException)
        {
            context.Log($"No premise file at {premisePath}, planner will create one", "planning");
            return "No premise file found. The planner should create one.";
        }
    }
}
