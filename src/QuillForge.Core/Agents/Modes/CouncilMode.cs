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

            You are in council mode. For each user query:

            1. Automatically route the query through the council advisors first
            2. Receive multiple AI perspectives on the topic
            3. Synthesize them into a coherent, balanced answer
            4. Identify areas of agreement and disagreement
            5. Highlight unique insights from each perspective

            Present the synthesized view clearly, noting which advisor contributed which insight
            when relevant. The goal is richer, more nuanced answers through multi-perspective analysis.
            """;
    }

    public Task OnResponseAsync(AgentResponse response, ModeContext context, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
