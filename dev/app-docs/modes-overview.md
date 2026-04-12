---
name: Modes Overview
summary: What each mode is for and when to switch between them
---

# Modes Overview

QuillForge operates in one mode at a time. Each mode changes the system's behavior,
preferred workflows, and prompt guidance. Switch modes with the `/mode` slash
command or the mode picker in the UI.

## Available Modes

| Mode | Purpose | Best For |
|------|---------|----------|
| **Guide** | Onboarding and troubleshooting | Choosing a mode, understanding the app, inspecting obvious setup problems |
| **Writer** | Guided prose generation with approval workflow | Writing scenes, chapters, and prose with review before saving |
| **Roleplay** | In-character interactive fiction | Playing out scenes as/with characters, immersive dialogue |
| **Forge** | Command-and-pipeline surface for autonomous story production | Running forge projects, stage pipelines, approvals, and status checks |
| **Council** | Assistant-mediated advisory panel | Getting diverse opinions on creative decisions, plot analysis |
| **Research** | Assistant-mediated web-backed information gathering | Finding real-world facts, references, and research for worldbuilding |

## How Mode Switching Works

- The system receives mode-specific guidance that defines how it should behave and what workflow it should prefer.
- Mode changes are instant and do not reset conversation history.
- Some modes expect additional context (e.g., Writer mode benefits from a selected file, Roleplay from a character).
- Guide mode is app-owned and focused on explanation rather than execution.
- Council and Research use an Assistant-shaped interface that coordinates specialized tools instead of acting like a broad do-anything agent.
- Forge stays command- and pipeline-owned rather than sharing the Writer/Roleplay scene-grounding path.

## Choosing the Right Mode

- **"Write me a scene"** -> Writer mode (approval workflow, writing style applied)
- **"I just opened this app. What should I use?"** -> Guide mode
- **"Tell me about [character]"** -> Guide mode for orientation, then switch to Writer or Roleplay depending on the goal
- **"Let's play as [character]"** -> Roleplay mode (immersive, in-character)
- **"Write a full story about X"** -> Forge mode, then use `/forge new`, `/forge design`, and `/forge start`
- **"What do you think about this plot direction?"** -> Council mode (multiple perspectives)
- **"Research medieval castle architecture"** -> Research mode (web search backed)
