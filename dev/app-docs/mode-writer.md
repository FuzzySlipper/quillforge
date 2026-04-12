---
name: Writer Mode
summary: Guided prose generation with approval workflow before saving
---

# Writer Mode

Writer mode is designed for collaborative prose writing. It generates content using the active writing style, consults lore automatically, and presents drafts for approval before saving.

## Workflow

1. You describe what you want written (a scene, chapter continuation, etc.)
2. The request is grounded through the Narrative Director first
3. The Narrative Director checks canon, current state, narrative rules, and session context, then hands a prose brief to the Prose Writer
4. The writing pipeline generates prose using your writing style and grounded session context
5. The generated content appears as "pending" — you can:
   - **Accept** it to save to the target file
   - **Reject** it to discard
   - **Request modifications** before accepting
6. Only accepted content is written to disk

## Available Tools

Writer mode uses the full writing pipeline and the writer-specific approval workflow.

## Key Behaviors

- Uses the Narrative Director as the mandatory grounding layer before prose is written
- Automatically queries lore when writing to ensure consistency
- Applies the active writing style from your profile
- Will NOT write to files until you explicitly approve
- Maintains context of the file being edited (if selected)
- If lore, narrative rules, or writing style inputs are missing or misconfigured, the system should stop and disclose the missing prerequisite instead of improvising around it
- If you correct canon or characterization, the grounded path should re-check the relevant canon before drafting a revision

## Tips

- Select a file before entering Writer mode to give it context about what you're continuing
- Use specific, detailed scene descriptions for better results
- The writing style in your profile significantly affects output quality and tone
