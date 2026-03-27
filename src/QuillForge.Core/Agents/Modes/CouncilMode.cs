using QuillForge.Core.Models;

namespace QuillForge.Core.Agents.Modes;

/// <summary>
/// Council mode. Routes queries through multiple AI advisors first,
/// then synthesizes their perspectives into a coherent answer.
/// </summary>
public sealed class CouncilMode : IMode
{
    public string Name => "council";

    public string BuildSystemPromptSection(ModeContext context)
    {
        return """
            ## Current Mode: Council

            You are in council mode. You have the `run_council` tool available. For each user query:

            1. Call the `run_council` tool with the user's question or topic
            2. The tool fans the query to multiple AI council advisors in parallel
            3. You will receive all advisors' perspectives in the tool result
            4. Synthesize them into a coherent, balanced answer
            5. Identify areas of agreement and disagreement
            6. Highlight unique insights from each perspective

            IMPORTANT: Always call `run_council` before responding to the user. Do not answer
            directly — the value of council mode is in gathering diverse perspectives first.

            Present the synthesized view clearly, noting which advisor contributed which insight
            when relevant. The goal is richer, more nuanced answers through multi-perspective analysis.
            """;
    }

    public Task OnResponseAsync(AgentResponse response, ModeContext context, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
