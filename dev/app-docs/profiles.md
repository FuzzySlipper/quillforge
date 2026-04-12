---
name: Profiles
summary: How lore, writing style, narrative rules, and librarian prompt compose into a reusable profile
---

# Profiles

A profile bundles together the configuration that shapes how QuillForge behaves in a session. Profiles are reusable and can be switched without losing conversation history.

## Profile Components

| Component | What It Does | Where It Lives |
|-----------|-------------|----------------|
| **Lore Set** | The collection of world-building markdown files available to the Librarian | `user/lore/<set-name>/` |
| **Writing Style** | Prose style guide that shapes generated writing | `user/writing-styles/` |
| **Narrative Rules** | Structural rules for storytelling (pacing, POV, etc.) | `user/narrative-rules/` |
| **Librarian Prompt** | Search/grounding guidance for the lore retrieval layer | `user/librarian-prompts/` |
| **Legacy Conductors** | Older prompt files kept for migration/reference; no longer live routing authority | `user/conductor/` |

## How Profiles Work

1. A profile is a YAML file in `user/profiles/` that references a lore set, writing style, narrative rules, and librarian prompt by name
2. The active profile determines which components are loaded into the session
3. Switching profiles changes those components together for future messages
4. Each component can also be changed independently within a session

## Profile Switching

- Use the profile picker in the UI to select a profile
- Profile changes take effect on the next message — no restart needed
- The session remembers which profile is active

## Creating Profiles

1. Create your content files (lore set, writing style, narrative rules, librarian prompt)
2. Create a profile YAML in `user/profiles/` that references them
3. The profile appears in the picker automatically

## Component Details

### Lore Set
A directory of markdown files containing world-building information. The Librarian agent searches these when `query_lore` is invoked. Organize by topic (characters, locations, history, etc.).

### Writing Style
A markdown guide that describes the desired prose style — sentence structure, vocabulary level, narrative voice, pacing preferences. Applied when generating prose via `write_prose`.

### Narrative Rules
Structural storytelling rules — point of view, tense, chapter structure, pacing guidelines. These complement the writing style with higher-level narrative constraints.

### Librarian Prompt
Guidance for how the lore retrieval layer should search, prioritize, and summarize canon before handing results back to the interactive workflow.

### Legacy Conductors
Older installs may still have conductor files and profile fields. QuillForge keeps them readable during migration, but live routing is app-owned by mode now rather than driven by conductor prompt text.
