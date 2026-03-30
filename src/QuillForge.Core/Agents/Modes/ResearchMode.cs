using QuillForge.Core.Models;

namespace QuillForge.Core.Agents.Modes;

/// <summary>
/// Research mode. Breaks user queries into parallel research topics,
/// dispatches multi-turn research agents, and synthesizes findings.
/// </summary>
public sealed class ResearchMode : IMode
{
    public string Name => "research";

    public string BuildSystemPromptSection(ModeContext context)
    {
        var projectNote = string.IsNullOrWhiteSpace(context.ProjectName)
            ? "No research project is currently selected — ask the user which project to use, or suggest a name."
            : $"The active research project is \"{context.ProjectName}\". Findings will be saved to `research/{context.ProjectName}/`.";

        return $"""
            ## Current Mode: Research

            You are in research mode. You have the `run_research` tool available. For each user query:

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

            IMPORTANT: Always use `run_research` for substantive queries. Do not answer research
            questions from your own knowledge alone — the value of research mode is in gathering
            current, sourced information.
            """;
    }

    public Task OnResponseAsync(AgentResponse response, ModeContext context, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
