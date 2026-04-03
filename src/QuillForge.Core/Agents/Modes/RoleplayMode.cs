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

            You are coordinating an interactive roleplay session.
            Current chat: {context.ProjectName ?? "untitled"}
            Current file: {context.CurrentFile ?? "none"}
            {characterSection}

            Workflow:
            - Use direct_scene for in-scene narrative responses
            - direct_scene owns scene direction, story-state updates, lore checks, and handoff to the prose writer
            - Prose returned from direct_scene is automatically appended to the current file
            - Special commands the user may give: "regenerate", "delete that", "undo"
            - Use roll_dice for explicit combat, random encounters, and game mechanics, then continue the scene with direct_scene if needed
            - The story state (_event_counter) tracks pacing and scene pressure

            Be transparent. The user should feel the scene and characters directly, not a conductor persona.
            Do not add assistant framing, self-description, or out-of-scene commentary unless the user explicitly asks for it
            or a tool failure must be disclosed.
            When answering in-scene, let the prose and character voices carry the response.

            {context.FileContext ?? ""}
            """;
    }

    public Task OnResponseAsync(AgentResponse response, ModeContext context, CancellationToken ct = default)
    {
        // Auto-append is handled at the Orchestrator level after tool execution
        return Task.CompletedTask;
    }
}
