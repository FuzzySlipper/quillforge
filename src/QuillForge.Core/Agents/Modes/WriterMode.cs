using QuillForge.Core.Models;

namespace QuillForge.Core.Agents.Modes;

/// <summary>
/// Long-form project writing mode with accept/reject workflow.
/// Stateless — writer pending state lives in WriterRuntimeState.
/// </summary>
public sealed class WriterMode : IMode
{
    public string Name => "writer";

    public bool AllowsTopLevelTool(string toolName)
    {
        return !string.Equals(toolName, "write_prose", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(toolName, "update_story_state", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(toolName, "update_narrative_state", StringComparison.OrdinalIgnoreCase);
    }

    public string BuildSystemPromptSection(ModeContext context)
    {
        var pendingNote = !string.IsNullOrEmpty(context.WriterPendingContent)
            ? "\n\nThere is pending content awaiting user review. If the user asks for changes, generate a revised draft. The app handles accept/reject actions and saving."
            : "";

        return $"""
            ## Current Mode: Writer

            You are in long-form writing mode for project "{context.ProjectName ?? "untitled"}".
            Current file: {context.CurrentFile ?? "none"}

            Workflow:
            1. Use direct_scene for canon-sensitive drafting requests
            2. direct_scene is the mandatory grounding layer before prose: it owns lore checks, scene planning, story-state updates, narrative-state updates, and handoff to the prose writer
            3. Present the returned draft prose to the user for review
            4. IMPORTANT: Do NOT write draft prose to the story file yourself
            5. If the user requests changes, produce a revised draft for review
            6. The app, not the model, handles accept/reject actions and saving accepted drafts

            Rules:
            - Do not call write_prose directly from the top level in Writer mode.
            - Do not use write_file for the standard Writer draft acceptance flow.
            - Do not bypass the grounding layer for scene writing, chapter drafting, or canon-sensitive revision.
            - The visible prose should come back through direct_scene, then enter the normal review workflow.
            - If the user corrects canon, characterization, chronology, or relationship details, re-ground against the relevant lore/profile context before generating a revision. Do not patch only the single sentence they flagged.

            {context.FileContext ?? ""}{pendingNote}

            If the user asks about your behavior, mode boundaries, available tools, or how to use the system,
            consult `query_docs` rather than guessing.
            """;
    }

    public Task OnResponseAsync(AgentResponse response, ModeContext context, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
