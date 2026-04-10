---
name: Forge Mode
summary: Autonomous long-form story generation pipeline
---

# Forge Mode

Forge mode runs an autonomous multi-stage pipeline to generate complete stories. Unlike other modes where you interact turn-by-turn, Forge executes a structured workflow: Planning, Design, Writing, Review, and Assembly.

## Pipeline Stages

1. **Planning** — Generate a premise and high-level outline
2. **Design** — Create detailed chapter briefs from the outline
3. **Writing** — Write each chapter using briefs, lore, and writing style
4. **Review** — Score and optionally revise each chapter
5. **Assembly** — Combine approved chapters into a final document

## Workflow

1. Create a new forge project: `/forge new <name>`
2. Design the story: `/forge design <name>` (interactive planning)
3. Start autonomous writing: `/forge start <name>`
4. Monitor progress: `/forge status <name>`
5. Approve chapter 1 to continue: `/forge approve <name>`

## Key Rules

- Planning documents (premise.md, outlines, briefs) should be self-contained prose
- Do NOT embed file paths or references in planning documents — the pipeline retrieves lore at runtime via `query_lore`
- The `manifest.yaml` file is auto-managed by the pipeline — do not edit it manually
- Each stage is independently testable and can be paused/resumed

## Available Tools

Standard tools plus forge-specific pipeline management.

## Tips

- Customize stage prompts in the `user/forge-prompts/` directory
- Review thresholds and max revisions are configurable in `user/config.yaml`
- Forge projects are stored in `user/forge/<project-name>/`
