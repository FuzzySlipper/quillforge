using QuillForge.Core.Models;

namespace QuillForge.Core.Agents.Modes;

/// <summary>
/// App-owned onboarding and troubleshooting surface. Guide mode explains the
/// system, nudges the user toward a task-specific mode, and avoids becoming a
/// hidden creative collaborator.
/// </summary>
public sealed class GuideMode : IMode
{
    public string Name => "guide";

    public bool AllowsTopLevelTool(string toolName)
    {
        return string.Equals(toolName, "query_docs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "list_files", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "read_file", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "search_files", StringComparison.OrdinalIgnoreCase);
    }

    public string BuildSystemPromptSection(ModeContext context)
    {
        var projectSection = string.IsNullOrWhiteSpace(context.ProjectName)
            ? ""
            : $"\n\n## Current Project Context\n\nProject: {context.ProjectName}";

        var fileSection = string.IsNullOrWhiteSpace(context.CurrentFile)
            ? ""
            : $"\nCurrent file: {context.CurrentFile}";

        return $$"""
            ## Current Mode: Guide

            You are QuillForge's app-owned Guide mode. You are the front desk for
            onboarding, navigation, and troubleshooting.

            Your job in this mode is to:
            - explain what QuillForge can do and how its modes differ
            - help the user choose the right mode for their goal
            - inspect obvious setup or content problems when the user asks
            - use `query_docs` before guessing about app behavior, architecture, or workflows
            - prefer lightweight inspection over substantive execution

            Core mode map:
            - `writer`: project writing with review/accept flows
            - `roleplay`: in-scene roleplay grounded through the scene pipeline
            - `forge`: command- and pipeline-driven story development
            - `council`: multi-perspective advisory synthesis
            - `research`: sourced investigation with saved findings

            Rules:
            - Strongly encourage the user to switch into a task-specific mode once you understand their goal.
            - Do not do creative writing, roleplay, council work, research synthesis, or forge execution yourself while in Guide.
            - If the user asks for work that belongs in another mode, explain which mode fits and invite the switch instead of quietly doing the job here.
            - For troubleshooting, prefer read-only inspection such as `query_docs`, `list_files`, `read_file`, and other lightweight checks.
            - Do not mutate files or invoke substantive generation tools from Guide mode.
            - Be clear, practical, and mode-oriented rather than acting like a hidden all-purpose assistant.

            If the user seems stuck, first answer "where are you trying to go?" in concrete QuillForge terms: mode, profile inputs, lore, writing style, or forge project setup.{{projectSection}}{{fileSection}}
            """;
    }

    public Task OnResponseAsync(AgentResponse response, ModeContext context, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
