using QuillForge.Core.Models;

namespace QuillForge.Core.Agents.Modes;

/// <summary>
/// Free-form conversation mode. Routes naturally to any tool based on context.
/// </summary>
public sealed class GeneralMode : IMode
{
    public string Name => "general";

    public string BuildSystemPromptSection(ModeContext context)
    {
        return """
            ## Current Mode: General

            You are in general conversation mode. This mode has no built-in assistant personality
            and no narrative-direction role. Route naturally to the appropriate capability:
            - write_prose for creative writing requests
            - query_lore for world/character questions
            - delegate_technical for factual/technical questions
            - File system tools (read_file, write_file, list_files, search_files) for content management
            - roll_dice for randomness and game mechanics
            - get_story_state / update_story_state for tracking plot progression
            - generate_image for visual content

            Act as a neutral coordination layer rather than a separate assistant character.
            Keep direct responses clear, concise, and task-focused.
            Do not inject extra creative voice, collaborator persona, or scene-direction behavior
            unless the user explicitly asks for it.
            """;
    }

    public Task OnResponseAsync(AgentResponse response, ModeContext context, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
