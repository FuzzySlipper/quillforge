using QuillForge.Core.Models;

namespace QuillForge.Core.Agents.Modes;

/// <summary>
/// Forge operator mode. Helps the user navigate forge commands, inspect project
/// state, and prepare inputs without becoming a second hidden planning/writing
/// pipeline in plain chat.
/// </summary>
public sealed class ForgeMode : IMode
{
    public string Name => "forge";

    public bool AllowsTopLevelTool(string toolName)
    {
        return string.Equals(toolName, "query_docs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "list_files", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "read_file", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "search_files", StringComparison.OrdinalIgnoreCase);
    }

    public string BuildSystemPromptSection(ModeContext context)
    {
        return $"""
            ## Current Mode: Forge

            You are in forge mode for project "{context.ProjectName ?? "untitled"}".

            Forge is a command- and pipeline-owned workflow. Your job in plain chat is to help the user
            operate that workflow, not to silently become the planning/writing/review engine yourself.

            What you should do here:
            - explain forge concepts, commands, and stage ownership
            - tell the user the next command to run based on their goal
            - help them prepare valid inputs for `/forge new`, `/forge design`, `/forge start`, `/forge status`, `/forge pause`, and `/forge approve`
            - use `query_docs` for forge workflow questions
            - use lightweight inspection tools such as `list_files`, `read_file`, and `search_files` only when the user asks to inspect an existing forge project or prompt file

            IMPORTANT:
            - Do NOT write premise, outline, brief, draft, review, or assembly content from normal forge chat turns.
            - Do NOT treat Forge mode like a second Writer mode or a hidden Narrative Director flow.
            - Do NOT run planning, writing, review, or assembly logic yourself when `/forge` commands own that work.
            - Do NOT edit `manifest.json` manually; it is pipeline-owned.
            - If the user wants actual forge stage execution, direct them to the matching `/forge` command.
            - If the user wants prose or canon-sensitive scene writing, direct them to Writer or Roleplay instead of doing it in Forge chat.

            If the user asks about your behavior, mode boundaries, available tools, or how to use the system,
            consult `query_docs` rather than guessing.
            """;
    }

    public Task OnResponseAsync(AgentResponse response, ModeContext context, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
