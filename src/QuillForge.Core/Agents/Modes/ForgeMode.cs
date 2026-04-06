using QuillForge.Core.Models;

namespace QuillForge.Core.Agents.Modes;

/// <summary>
/// Forge prep mode. Conversationally designs a story before the autonomous pipeline runs.
/// Works through premise, characters, world-building, tone, and structure.
/// Saves artifacts to the forge project directory.
/// </summary>
public sealed class ForgeMode : IMode
{
    public string Name => "forge";

    public string BuildSystemPromptSection(ModeContext context)
    {
        return $"""
            ## Current Mode: Forge (Story Design)

            You are in forge mode, designing a story for project "{context.ProjectName ?? "untitled"}".

            This is the PREP phase — work conversationally with the user to design the story before
            the autonomous writing pipeline runs. Work through:

            1. **Premise** — Core concept, hook, themes
            2. **Characters** — Main cast, motivations, relationships
            3. **World** — Setting details, rules, atmosphere
            4. **Tone** — Narrative voice, genre expectations, mood
            5. **Structure** — Arc type (complete/episodic), chapter count, pacing

            Save design artifacts using:
            - write_file(directory="forge", path="{context.ProjectName}/plan/premise.md") for the story premise
            - write_file(directory="lore", ...) for character bios and world-building entries

            IMPORTANT:
            - Do NOT write manifest.yaml — it is auto-managed by the pipeline.
            - Do NOT embed file paths or file references in planning documents. Planning documents
              (premise.md, outlines, briefs) should be self-contained prose. The autonomous pipeline
              retrieves lore at runtime via the query_lore tool — it does not follow file paths in documents.
            """;
    }

    public Task OnResponseAsync(AgentResponse response, ModeContext context, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
