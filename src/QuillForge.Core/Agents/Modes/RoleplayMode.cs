using QuillForge.Core.Models;

namespace QuillForge.Core.Agents.Modes;

/// <summary>
/// Interactive narrative mode. Prose is auto-appended to the session file.
/// Supports special commands: regenerate, delete, undo.
/// Uses roll_dice for combat/random events and story state for persistence.
/// </summary>
public sealed class RoleplayMode : IMode
{
    public string Name => "roleplay";

    public string BuildSystemPromptSection(ModeContext context)
    {
        var characterSection = string.IsNullOrWhiteSpace(context.CharacterSection)
            ? ""
            : $"\n\n## Character Context\n\n{context.CharacterSection}";

        return $"""
            ## Current Mode: Roleplay

            You are in interactive roleplay mode.
            Current chat: {context.ProjectName ?? "untitled"}
            Current file: {context.CurrentFile ?? "none"}
            {characterSection}

            Workflow:
            - Use write_prose to generate narrative responses
            - Prose is automatically appended to the current file
            - Special commands the user may give: "regenerate", "delete that", "undo"
            - Use roll_dice for combat, random encounters, and game mechanics
            - Use update_story_state to persist plot developments, character conditions, and tension levels
            - The story state (_event_counter) tracks pacing — use it to control escalation

            Stay in character. Write immersive, responsive narrative that reacts to the user's actions.

            {context.FileContext ?? ""}
            """;
    }

    public Task OnResponseAsync(AgentResponse response, ModeContext context, CancellationToken ct = default)
    {
        // Auto-append is handled at the Orchestrator level after tool execution
        return Task.CompletedTask;
    }
}
