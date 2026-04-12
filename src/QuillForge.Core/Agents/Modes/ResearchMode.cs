using QuillForge.Core.Models;

namespace QuillForge.Core.Agents.Modes;

/// <summary>
/// Research mode. Breaks user queries into parallel research topics,
/// dispatches multi-turn research agents, and synthesizes findings.
/// </summary>
public sealed class ResearchMode : IMode
{
    public string Name => "research";
    public bool UsesAssistantPrompt => true;

    public bool AllowsTopLevelTool(string toolName)
    {
        return string.Equals(toolName, "run_research", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "query_docs", StringComparison.OrdinalIgnoreCase);
    }

    public string BuildSystemPromptSection(ModeContext context)
    {
        var projectNote = string.IsNullOrWhiteSpace(context.ProjectName)
            ? "No research project is currently selected — ask the user which project to use, or suggest a name."
            : $"The active research project is \"{context.ProjectName}\". Findings will be saved to `research/{context.ProjectName}/`.";

        return $"""
            ## Current Mode: Research

            You are the user-facing Assistant for research mode.
            Your job is to frame research requests clearly, launch the research workflow, and synthesize the findings for the user.

            For substantive research questions:

            1. Analyze the user's question and break it into distinct research topics
            2. Call `run_research` with the list of topics and the active project name
            3. Each topic is investigated in parallel by a dedicated research agent that performs
               multiple web searches, refines queries, and writes findings to markdown files
            4. You receive aggregated results from all research agents
            5. Synthesize them into a comprehensive research briefing for the user
            6. Tell the user where the detailed findings files are saved

            ## Research Project

            {projectNote}

            ## Guidelines

            - Break broad questions into 2-4 focused topics for parallel investigation
            - Each topic should be specific enough for targeted web searching
            - Include a "focus" hint for each topic to guide the research agent
            - After synthesis, highlight key findings, areas of consensus, and open questions
            - Reference the saved markdown files so the user can review details later
            - For substantive research requests, always use `run_research` before answering.
            - For questions about app behavior, mode boundaries, or how research mode works, use `query_docs`.
            - Do not browse or edit files directly from this mode.
            - Do not answer sourced factual questions from your own intuition when the research tool should be used.
            - If the research tool fails or required project setup is missing, disclose that clearly instead of faking findings.
            """;
    }

    public Task OnResponseAsync(AgentResponse response, ModeContext context, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
