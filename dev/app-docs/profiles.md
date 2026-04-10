---
name: Profiles
summary: How conductor, lore set, writing style, narrative rules, and librarian prompt compose into a profile
---

# Profiles

A profile bundles together the configuration that shapes how QuillForge behaves in a session. Profiles are reusable and can be switched without losing conversation history.

## Profile Components

| Component | What It Does | Where It Lives |
|-----------|-------------|----------------|
| **Conductor** | The base personality/narrator prompt applied across all modes | `user/conductor/` |
| **Lore Set** | The collection of world-building markdown files available to the Librarian | `user/lore/<set-name>/` |
| **Writing Style** | Prose style guide that shapes generated writing | `user/writing-styles/` |
| **Narrative Rules** | Structural rules for storytelling (pacing, POV, etc.) | `user/narrative-rules/` |

## How Profiles Work

1. A profile is a YAML file in `user/profiles/` that references specific conductor, lore set, writing style, and narrative rules by name
2. The active profile determines which components are loaded into the session
3. Switching profiles changes all components at once — conductor, lore, style, and rules
4. Each component can also be changed independently within a session

## Profile Switching

- Use the profile picker in the UI to select a profile
- Profile changes take effect on the next message — no restart needed
- The session remembers which profile is active

## Creating Profiles

1. Create your content files (conductor prompt, lore set, writing style)
2. Create a profile YAML in `user/profiles/` that references them
3. The profile appears in the picker automatically

## Component Details

### Conductor
The conductor is the base personality prompt. It defines who the AI "is" — narrator tone, personality traits, communication style. It applies across all modes as the foundation layer.

### Lore Set
A directory of markdown files containing world-building information. The Librarian agent searches these when `query_lore` is invoked. Organize by topic (characters, locations, history, etc.).

### Writing Style
A markdown guide that describes the desired prose style — sentence structure, vocabulary level, narrative voice, pacing preferences. Applied when generating prose via `write_prose`.

### Narrative Rules
Structural storytelling rules — point of view, tense, chapter structure, pacing guidelines. These complement the writing style with higher-level narrative constraints.
