---
name: Modes Overview
summary: What each mode is for and when to switch between them
---

# Modes Overview

QuillForge operates in one mode at a time. Each mode changes the orchestrator's behavior, available tools, and system prompt. Switch modes with the `/mode` slash command or the mode picker in the UI.

## Available Modes

| Mode | Purpose | Best For |
|------|---------|----------|
| **General** | Free-form conversation and coordination | Lore questions, brainstorming, file management, general tasks |
| **Writer** | Guided prose generation with approval workflow | Writing scenes, chapters, and prose with review before saving |
| **Roleplay** | In-character interactive fiction | Playing out scenes as/with characters, immersive dialogue |
| **Forge** | Autonomous long-form story pipeline | Generating full stories through automated planning/writing/review stages |
| **Council** | Multi-perspective advisory panel | Getting diverse opinions on creative decisions, plot analysis |
| **Research** | Web-backed information gathering | Finding real-world facts, references, and research for worldbuilding |

## How Mode Switching Works

- The orchestrator receives a mode-specific system prompt section that defines its personality and available tools.
- Mode changes are instant and do not reset conversation history.
- Some modes expect additional context (e.g., Writer mode benefits from a selected file, Roleplay from a character).
- The conductor (persona) prompt applies across all modes as the base personality layer.

## Choosing the Right Mode

- **"Write me a scene"** -> Writer mode (approval workflow, writing style applied)
- **"Tell me about [character]"** -> General mode (lore lookup, no prose generation)
- **"Let's play as [character]"** -> Roleplay mode (immersive, in-character)
- **"Write a full story about X"** -> Forge mode (autonomous multi-stage pipeline)
- **"What do you think about this plot direction?"** -> Council mode (multiple perspectives)
- **"Research medieval castle architecture"** -> Research mode (web search backed)
