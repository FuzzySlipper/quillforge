using QuillForge.Core.Agents;
using QuillForge.Core.Models;
using QuillForge.Core.Services;

namespace QuillForge.Core.Pipeline;

/// <summary>
/// Shared context passed between forge pipeline stages.
/// Carries the manifest, agent references, and project file access.
/// </summary>
public sealed class ForgeContext
{
    public required ForgeManifest Manifest { get; set; }
    public required string ProjectPath { get; set; }
    public required ForgePlannerAgent Planner { get; init; }
    public required ForgeWriterAgent Writer { get; init; }
    public required ForgeReviewerAgent Reviewer { get; init; }
    public required IReadOnlyList<IToolHandler> WriterTools { get; init; }
    public required IContentFileService FileService { get; init; }
    public required AgentContext AgentContext { get; init; }

    /// <summary>
    /// Loaded writing style for the project.
    /// </summary>
    public string WritingStyle { get; set; } = "";

    /// <summary>
    /// Loaded lore context summary for the planner.
    /// </summary>
    public string LoreContext { get; set; } = "";

    /// <summary>
    /// Path to the run-specific lore file (e.g. "forge/myproject/run-lore.md").
    /// This file accumulates small details extracted from chapters during the current run
    /// and is cleared at the start of each new pipeline run.
    /// </summary>
    public string RunLorePath { get; set; } = "";

    /// <summary>
    /// Review pass threshold. Chapters scoring below this get revised.
    /// </summary>
    public double ReviewPassThreshold { get; init; } = 7.0;

    /// <summary>
    /// Maximum revision attempts per chapter before flagging.
    /// </summary>
    public int MaxRevisions { get; init; } = 3;
}
