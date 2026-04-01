using System.Text;
using System.Text.Json;
using QuillForge.Core.Models;

namespace QuillForge.Core.Services;

public sealed record ProbeScenario(string Id, string Title, string UserMessage);

/// <summary>
/// Versioned scenario battery for the interpretation probe.
/// Each scenario is designed to expose how models interpret tool boundaries,
/// workflow sequencing, and mode rules under ambiguity.
/// </summary>
public static class ProbeBattery
{
    public const string Version = "1.0";

    public static IReadOnlyList<ProbeScenario> Scenarios { get; } =
    [
        new("tool-ambiguity-lore-vs-prose",
            "Lore Lookup vs Creative Writing",
            "Write me a paragraph about the history of the Ironclad Mountains."),

        new("mode-boundary-refusal",
            "Out-of-Band Command Recognition",
            "Run the forge pipeline on my project 'legacy'."),

        new("workflow-sequencing",
            "Multi-Tool Workflow Order",
            "Continue the scene where Elena confronts the council. Make sure it matches the established lore about the Silverwatch faction."),

        new("tool-rejection-reasoning",
            "Tool Selection with Explicit Rejection",
            "My character casts a healing spell on the wounded soldier. Also, can you generate an image of this scene?"),

        new("cross-tool-coordination",
            "Conditional Tool Chain",
            "Search my lore files for any mention of the Treaty of Ashvale, and if nothing exists, draft a new lore entry for it."),

        new("state-management-judgment",
            "Story State Update Judgment",
            "The siege has ended and the city is rebuilding. Three months pass."),

        new("research-boundary",
            "Research Mode Boundary",
            "I need to understand the real-world history of Byzantine siege warfare for my novel's next chapter."),
    ];

    public static string BuildProbePrompt(
        string systemPrompt,
        IReadOnlyList<ToolDefinition> tools,
        string activeModeName)
    {
        var sb = new StringBuilder();

        sb.AppendLine("You are being asked to analyze how you would interpret and execute instructions. This is a diagnostic exercise — do NOT generate creative content or execute tools. Instead, explain your reasoning about each scenario.");
        sb.AppendLine();

        sb.AppendLine("## Your Current System Prompt");
        sb.AppendLine();
        sb.AppendLine("<system_prompt>");
        sb.AppendLine(systemPrompt);
        sb.AppendLine("</system_prompt>");
        sb.AppendLine();

        sb.AppendLine("## Available Tools");
        sb.AppendLine();
        foreach (var tool in tools)
        {
            sb.AppendLine($"### `{tool.Name}`");
            sb.AppendLine();
            sb.AppendLine(tool.Description);
            sb.AppendLine();
            sb.AppendLine("Input schema:");
            sb.AppendLine("```json");
            sb.AppendLine(JsonSerializer.Serialize(tool.InputSchema, new JsonSerializerOptions { WriteIndented = true }));
            sb.AppendLine("```");
            sb.AppendLine();
        }

        sb.AppendLine("## Analysis Instructions");
        sb.AppendLine();
        sb.AppendLine($"You are currently in **{activeModeName}** mode. For each scenario below, provide:");
        sb.AppendLine();
        sb.AppendLine("1. **Initial Assessment** — What does the user want? Is the request ambiguous? How would you resolve the ambiguity?");
        sb.AppendLine("2. **Tool Selection** — Which tool(s) would you invoke, in what order, and with what arguments?");
        sb.AppendLine("3. **Tools Considered but Rejected** — At least one tool you evaluated but chose not to use, and why.");
        sb.AppendLine("4. **Mode Boundaries** — Is this within scope for the current mode? Would you refuse, redirect, or handle it differently than in another mode?");
        sb.AppendLine("5. **Assumptions** — What implicit assumptions are you making about the user's intent or the state of the world?");
        sb.AppendLine();

        sb.AppendLine("## Scenarios");
        sb.AppendLine();
        for (int i = 0; i < Scenarios.Count; i++)
        {
            var scenario = Scenarios[i];
            sb.AppendLine($"### Scenario {i + 1}: {scenario.Title}");
            sb.AppendLine();
            sb.AppendLine($"> \"{scenario.UserMessage}\"");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
