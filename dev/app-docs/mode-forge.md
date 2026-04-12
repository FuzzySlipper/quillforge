---
name: Forge Mode
summary: Autonomous long-form story generation pipeline
---

# Forge Mode

Forge mode is the command-and-pipeline surface for QuillForge's autonomous long-form story workflow. Unlike Writer or Roleplay, Forge chat is not where the app should quietly write story content on its own. The real work happens through explicit `/forge` commands and pipeline stages.

## Pipeline Stages

1. **Planning** — Generate a premise and high-level outline
2. **Design** — Create detailed chapter briefs from the outline
3. **Writing** — Write each chapter using briefs, lore, and writing style
4. **Review** — Score and optionally revise each chapter
5. **Assembly** — Combine approved chapters into a final document

## Workflow

1. Create a new forge project: `/forge new <name>`
2. Use Forge chat for command guidance, file inspection, and setup questions
3. Design the story through the planning/design pipeline: `/forge design <name>`
4. Start autonomous writing: `/forge start <name>`
5. Monitor progress: `/forge status <name>`
6. Approve chapter 1 to continue: `/forge approve <name>`

## Key Rules

- Forge chat should explain commands, stage ownership, and next steps rather than becoming a second hidden planning/writing agent
- Planning documents (premise.md, outlines, briefs) should be self-contained prose
- Do NOT embed file paths or references in planning documents — the pipeline retrieves lore at runtime via `query_lore`
- The `manifest.json` file is auto-managed by the pipeline — do not edit it manually
- Each stage is independently testable and can be paused/resumed

## Available Tools

- `query_docs` for forge workflow questions
- lightweight file inspection such as `list_files`, `read_file`, and `search_files` when you need to inspect a forge project or prompt file
- `query_lore` is intentionally not available in plain Forge chat; lore lookup belongs to the pipeline and its stage workers
- actual stage execution happens through `/forge` commands and the pipeline endpoints, not through normal chat tool calls

## Tips

- If you want actual prose drafting, switch to Writer mode; if you want Forge to generate a full story, use the `/forge` commands
- Customize stage prompts in the `user/forge-prompts/` directory
- Review thresholds and max revisions are configurable in `user/config.yaml`
- Forge projects are stored in `user/forge/<project-name>/`
