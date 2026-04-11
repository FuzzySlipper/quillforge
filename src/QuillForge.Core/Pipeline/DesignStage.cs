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
        context.Log($"Checking for outline at {outlinePath}...", "design");
        var hasOutline = await context.FileService.ExistsAsync(outlinePath, ct);
        if (!hasOutline)
        {
            _logger.LogWarning("No outline found at {Path}, design stage has nothing to refine", outlinePath);
            context.Log("No outline found — skipping design refinement", "design");
            yield return new StageCompletedEvent(StageName);
            yield break;
        }
        var outline = await context.FileService.ReadAsync(outlinePath, ct);
        context.Log($"Outline loaded ({outline.Length} chars)", "design");

        // Pre-load all plan files so the planner doesn't waste rounds reading them individually
        var planDir = $"forge/{context.Manifest.ProjectName}/plan";
        var planFiles = await context.FileService.ListAsync(planDir, "*.md", ct);
        var existingContent = new System.Text.StringBuilder();
        var loadedCount = 0;
        foreach (var file in planFiles)
        {
            // Skip outline (already included separately) and premise (input, not plan output)
            var fileName = Path.GetFileName(file);
            if (fileName is "outline.md" or "premise.md") continue;

            try
            {
                var content = await context.FileService.ReadAsync($"{planDir}/{fileName}", ct);
                existingContent.AppendLine($"### {fileName}");
                existingContent.AppendLine();
                existingContent.AppendLine(content);
                existingContent.AppendLine();
                existingContent.AppendLine("---");
                existingContent.AppendLine();
                loadedCount++;
            }
            catch (FileNotFoundException)
            {
                // Skip files that disappeared between list and read
            }
        }
        context.Log($"Pre-loaded {loadedCount} plan files for design review", "design");

        const string designSystemPrompt = """
            You are refining an existing story design, not creating a new one from scratch.

            The outline and plan files will be provided in the user message. Do NOT use
            read_file or list_files to re-read those files unless you are verifying a file
            you already rewrote during this run.

            Focus on:
            1. Character arc consistency across chapters
            2. Plot thread tracking (setup -> payoff)
            3. Pacing and tension curves
            4. Continuity between adjacent chapters

            Revise only the files that actually need changes, using write_file.
            Do not regenerate the whole project unless the existing files are missing.
            """;

        var designUserPrompt = $"""
            Review and refine this existing story design.

            ## Current Outline

            {outline}

            ## Existing Plan Files

            {existingContent}
            """;

        context.Log("Calling ForgePlanner for design refinement (this may take several minutes)...", "design");

        var response = await context.Planner.PlanAsync(
            premise: "",
            loreContext: "",
            context.PlannerTools,
            context.AgentContext,
            customPrompt: designSystemPrompt,
            userPrompt: designUserPrompt,
            ct: ct,
            progress: msg => context.Log(msg, "design"));

        context.StatsTracker.RecordCompletion("forge-planner", response.Usage);

        context.Log(
            $"Design refinement complete — {response.ToolRoundsUsed} rounds, " +
            $"{response.Usage.InputTokens + response.Usage.OutputTokens} total tokens",
            "design");

        _logger.LogInformation("Design stage completed: {Rounds} tool rounds", response.ToolRoundsUsed);
        yield return new StageCompletedEvent(StageName);
    }
}
