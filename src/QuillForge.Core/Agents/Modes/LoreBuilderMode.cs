using QuillForge.Core.Models;

namespace QuillForge.Core.Agents.Modes;

/// <summary>
/// Dedicated world-building surface for creating and refining durable lore
/// files that the Librarian can later retrieve.
/// </summary>
public sealed class LoreBuilderMode : IMode
{
    public string Name => "lore";

    public bool AllowsTopLevelTool(string toolName)
    {
        return string.Equals(toolName, "query_docs", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "query_context", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "query_lore", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "list_files", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "read_file", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "search_files", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "web_search", StringComparison.OrdinalIgnoreCase)
            || string.Equals(toolName, "save_lore_file", StringComparison.OrdinalIgnoreCase);
    }

    public string BuildSystemPromptSection(ModeContext context)
    {
        var loreSet = string.IsNullOrWhiteSpace(context.ActiveLoreSet)
            ? "default"
            : context.ActiveLoreSet;

        return $$"""
            ## Current Mode: Lore Builder

            You help the user create, expand, and revise durable lore documents for the active lore set.
            Active lore set: {{loreSet}}
            Lore files live under `lore/{{loreSet}}/`.

            Your job in this mode:
            - turn user-approved world-building into clear markdown lore files
            - inspect existing lore before creating duplicates or contradictions
            - use `query_context` to check selected character cards, session canon, plot/story state, recent conversation, and lore-document snippets with source labels
            - use `query_lore` only when you need semantic retrieval from the active lore documents
            - use `web_search` only for real-world reference or inspiration, never as fictional canon by itself
            - save lore only with `save_lore_file`

            Rules:
            - Do not write story prose, roleplay replies, council synthesis, or Forge stage output in this mode.
            - Do not use generic `write_file` for lore. `save_lore_file` is the only write path for lore documents.
            - Before saving web-derived facts as fictional canon, clearly distinguish the real-world source material from the proposed fictional lore and get the user's explicit approval unless the user already directly asked you to create/save that lore.
            - Prefer compact, reference-friendly markdown: headings, bullets, short prose sections, aliases, relationships, open questions, and source notes where useful.
            - If you are unsure whether a proposed fact should become canon, draft it in chat and ask before saving.
            - After saving, tell the user the exact `lore/{{loreSet}}/...` path.

            If the user asks about your behavior, mode boundaries, available tools, or how to use the system,
            consult `query_docs` rather than guessing.
            """;
    }

    public Task OnResponseAsync(AgentResponse response, ModeContext context, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }
}
