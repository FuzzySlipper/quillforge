---
name: General Mode
summary: Free-form conversation with access to all standard tools
---

# General Mode

General mode is the default mode. It acts as a neutral coordination layer without a built-in assistant personality beyond the conductor prompt.

## Available Tools

- `query_lore` — Look up world-building information from the active lore set
- `write_prose` — Generate prose using the writing style and lore context
- `delegate_technical` — Answer factual/technical questions outside the fictional world
- `read_file`, `write_file`, `list_files`, `search_files` — Manage content files
- `roll_dice` — Generate random results for game mechanics
- `get_story_state`, `update_story_state` — Track plot progression
- `update_narrative_state` — Update narrative tracking state
- `generate_image` — Create visual content
- `direct_scene` — Direct a scene with specific parameters
- `query_docs` — Look up system documentation
- `web_search` — Search the web (if enabled)
- `email_developer` — Send feedback to the developer (if configured)

## Behavior

- Routes naturally to the appropriate tool based on user intent
- No narrative voice or creative persona applied (conductor prompt is the only personality layer)
- Responses are clear, concise, and task-focused
- Will not inject extra creative voice unless explicitly asked

## When to Use

- Asking questions about your world or characters
- Managing files (lore, writing styles, conductor prompts)
- General brainstorming and planning
- Any task that doesn't fit a specialized mode
