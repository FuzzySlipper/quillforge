---
name: Research Mode
summary: Web-backed information gathering for worldbuilding and reference
---

# Research Mode

Research mode is designed for gathering real-world information to support your worldbuilding, historical accuracy, or creative research. You interact through a user-facing Assistant that frames the request, launches the research workflow, and synthesizes the findings.

## Behavior

- Always uses `run_research` for substantive queries — the Assistant does not answer research questions from its own knowledge alone
- Produces structured research reports with sources
- Research output is saved to `user/research/` organized by project
- Uses `query_docs` for questions about app behavior or workflow boundaries
- Does not browse or edit files directly as a substitute for the research workflow

## Available Tools

- `run_research` — Execute a web-backed research query
- `query_docs` — Explain app behavior, modes, and workflow boundaries

## Requirements

- Web search must be enabled in `user/config.yaml` (provider: searxng or similar)
- A SearXNG instance or compatible search provider must be accessible

## Tips

- Research mode excels at factual, sourced answers — use Guide when you need help choosing a mode, or Council when you want advisory perspectives on what the findings mean
- Research results are saved for later reference in your research directory
- You can specify a research project to organize findings by topic
- Customize the Assistant's tone in `user/assistant/default.md` without giving it broader execution authority
