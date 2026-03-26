using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using QuillForge.Core.Models;

namespace QuillForge.Core.Pipeline;

/// <summary>
/// Stage 2: Refines chapter briefs with character arcs, plot threads, and continuity notes.
/// Re-uses the planner agent with a design-focused prompt.
/// </summary>
public sealed class DesignStage : IPipelineStage
{
    private readonly ILogger<DesignStage> _logger;

    public DesignStage(ILogger<DesignStage> logger)
    {
        _logger = logger;
    }

    public string StageName => "Design";
    public ForgeStage StageEnum => ForgeStage.Design;

    public async IAsyncEnumerable<ForgeEvent> ExecuteAsync(
        ForgeContext context, [EnumeratorCancellation] CancellationToken ct)
    {
        _logger.LogInformation("Design stage starting for project {Project}", context.Manifest.ProjectName);
        yield return new StageStartedEvent(StageName);

        // Load the outline produced by planning
        var outlinePath = $"forge/{context.Manifest.ProjectName}/plan/outline.md";
        var hasOutline = await context.FileService.ExistsAsync(outlinePath, ct);
        if (!hasOutline)
        {
            _logger.LogWarning("No outline found at {Path}, design stage has nothing to refine", outlinePath);
            yield return new StageCompletedEvent(StageName);
            yield break;
        }
        var outline = await context.FileService.ReadAsync(outlinePath, ct);

        var designPrompt = $"""
            You are refining the story design. The outline and chapter briefs already exist.
            Review them for:
            1. Character arc consistency across chapters
            2. Plot thread tracking (setup → payoff)
            3. Pacing and tension curves
            4. Continuity between adjacent chapters

            Revise any briefs that need improvement using the write tools.

            ## Current Outline

            {outline}
            """;

        var response = await context.Planner.PlanAsync(
            designPrompt,
            context.LoreContext,
            context.WriterTools,
            context.AgentContext,
            ct: ct);

        _logger.LogInformation("Design stage completed: {Rounds} tool rounds", response.ToolRoundsUsed);
        yield return new StageCompletedEvent(StageName);
    }
}
