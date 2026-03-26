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

        var premise = await LoadPremiseAsync(context, ct);

        var response = await context.Planner.PlanAsync(
            premise,
            context.LoreContext,
            context.WriterTools,
            context.AgentContext,
            ct: ct);

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
            return "No premise file found. The planner should create one.";
        }
    }
}
