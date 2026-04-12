---
name: Roleplay Mode
summary: In-character interactive fiction with immersive dialogue
---

# Roleplay Mode

Roleplay mode enables immersive, in-character interaction. Scene responses are
grounded through the Narrative Director before the final prose is written.

## Behavior

- `direct_scene` is the mandatory grounding layer before roleplay prose is generated
- Responses are written in-character, maintaining the selected character's voice and personality
- No assistant framing, self-description, or out-of-scene commentary unless explicitly requested
- Lore is consulted to maintain world consistency
- Character card data (personality, scenario, greeting) shapes the interaction
- If the required lore or character-card context is missing, the system should surface that problem instead of freestyling through it
- User corrections to canon or characterization should trigger re-grounding before the next response

## Available Tools

Roleplay mode uses `direct_scene` for in-scene responses. Supporting tools such
as lore lookup and state updates happen under that grounded path rather than as
direct top-level prose generation.

## Setup

1. Select a character from your character cards when you want character-specific roleplay context
2. Switch to Roleplay mode via `/mode roleplay` or the mode picker
3. Optionally use `/greet` to have the character introduce themselves

## Tips

- Character cards define personality, scenario context, and greeting messages
- Roleplay routing is app-owned and grounded through Narrative Director rather than through a user-editable conductor prompt
- Use `/greet` at the start of a new session to establish the character's presence
- Roleplay prose is grounded first, then rendered, so continuity-sensitive scenes should stay more canon-aware than a direct prose-only flow
