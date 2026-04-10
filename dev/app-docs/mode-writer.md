---
name: Writer Mode
summary: Guided prose generation with approval workflow before saving
---

# Writer Mode

Writer mode is designed for collaborative prose writing. It generates content using the active writing style, consults lore automatically, and presents drafts for approval before saving.

## Workflow

1. You describe what you want written (a scene, chapter continuation, etc.)
2. The orchestrator generates prose using `write_prose`, applying your writing style and lore context
3. The generated content appears as "pending" — you can:
   - **Accept** it to save to the target file
   - **Reject** it to discard
   - **Request modifications** before accepting
4. Only accepted content is written to disk

## Available Tools

All tools from General mode, plus the writer-specific approval workflow.

## Key Behaviors

- Automatically queries lore when writing to ensure consistency
- Applies the active writing style from your profile
- Will NOT write to files until you explicitly approve
- Maintains context of the file being edited (if selected)

## Tips

- Select a file before entering Writer mode to give it context about what you're continuing
- Use specific, detailed scene descriptions for better results
- The writing style in your profile significantly affects output quality and tone
