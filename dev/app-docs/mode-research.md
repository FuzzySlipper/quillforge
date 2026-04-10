---
name: Research Mode
summary: Web-backed information gathering for worldbuilding and reference
---

# Research Mode

Research mode is designed for gathering real-world information to support your worldbuilding, historical accuracy, or creative research. It uses web search to find current, sourced information rather than relying on the LLM's training data alone.

## Behavior

- Always uses `run_research` for substantive queries — does not answer research questions from its own knowledge alone
- Produces structured research reports with sources
- Research output is saved to `user/research/` organized by project

## Available Tools

- `run_research` — Execute a web-backed research query
- `web_search` — Direct web search (if enabled)
- Standard tools for follow-up and file management

## Requirements

- Web search must be enabled in `user/config.yaml` (provider: searxng or similar)
- A SearXNG instance or compatible search provider must be accessible

## Tips

- Research mode excels at factual, sourced answers — for creative brainstorming, use General or Council mode
- Research results are saved for later reference in your research directory
- You can specify a research project to organize findings by topic
