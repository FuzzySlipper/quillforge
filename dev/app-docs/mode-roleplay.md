---
name: Roleplay Mode
summary: In-character interactive fiction with immersive dialogue
---

# Roleplay Mode

Roleplay mode enables immersive, in-character interaction. The orchestrator takes on the persona of a selected character (or a narrator role) and responds in-scene.

## Behavior

- Responses are written in-character, maintaining the selected character's voice and personality
- No assistant framing, self-description, or out-of-scene commentary unless explicitly requested
- Lore is consulted to maintain world consistency
- Character card data (personality, scenario, greeting) shapes the interaction

## Available Tools

All standard tools are available, but the orchestrator prioritizes staying in-character. Tool use happens transparently when needed (e.g., looking up lore to answer an in-world question).

## Setup

1. Select a character from your character cards (or use the conductor as narrator)
2. Switch to Roleplay mode via `/mode roleplay` or the mode picker
3. Optionally use `/greet` to have the character introduce themselves

## Tips

- Character cards define personality, scenario context, and greeting messages
- The conductor prompt still applies as a base layer, so it affects the roleplay style
- Use `/greet` at the start of a new session to establish the character's presence
