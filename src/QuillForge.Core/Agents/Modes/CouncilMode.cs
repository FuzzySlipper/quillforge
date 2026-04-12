using QuillForge.Core.Models;

namespace QuillForge.Core.Agents.Modes;

/// <summary>
/// Council mode. Routes queries through multiple AI advisors first,
/// then synthesizes their perspectives into a coherent answer.
/// </summary>
public sealed class CouncilMode : IMode
{
    public string Name => "council";
    public bool UsesAssistantPrompt => true;

    public bool AllowsTopLevelTool(string toolName)
    {
        return string.Equals(toolName, "run_council", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "query_docs", StringComparison.OrdinalIgnoreCase);
    }

    public string BuildSystemPromptSection(ModeContext context)
    {
        return """
            ## Current Mode: Council

            You are the user-facing Assistant for council mode.
            Your job is to clarify the user's advisory question, convene the council, and synthesize the result.

            For substantive advisory questions:

            1. Call the `run_council` tool with the user's question or topic
            2. The tool fans the query to multiple AI council advisors in parallel
            3. You will receive all advisors' perspectives in the tool result
            4. Synthesize them into a coherent, balanced answer
            5. Identify areas of agreement and disagreement
            6. Highlight unique insights from each perspective

            Rules:
            - For substantive advisory requests, always call `run_council` before answering.
            - For questions about app behavior, mode boundaries, or how council mode works, use `query_docs`.
            - Do not pretend to be the individual council members yourself.
            - Do not take over file or content-management work from this mode.
            - If the council tool fails, say so plainly instead of replacing it with your own invented panel.

            Present the synthesized view clearly, noting which advisor contributed which insight
            when relevant. The goal is richer, more nuanced answers through multi-perspective analysis.
            """;
    }

    public Task OnResponseAsync(AgentResponse response, ModeContext context, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
