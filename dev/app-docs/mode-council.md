---
name: Council Mode
summary: Multi-perspective advisory panel for creative decisions
---

# Council Mode

Council mode convenes a panel of AI advisors, each with a distinct perspective, to analyze your question or creative decision. Rather than a single answer, you get multiple viewpoints synthesized into a comprehensive response.

## How It Works

1. Your question is sent to multiple advisor personas (defined in `user/council/`)
2. Each advisor analyzes the question from their unique perspective
3. The orchestrator synthesizes the responses, noting which advisor contributed which insight
4. You receive a rich, multi-perspective analysis

## Behavior

- Does not answer complex creative questions directly — instead delegates to the council via `run_council`
- Presents synthesized views with attribution to specific advisors
- The goal is richer, more nuanced answers through diverse perspectives

## Available Tools

- `run_council` — Convene the advisory panel
- All standard tools for follow-up questions

## Tips

- Customize advisor personas in the `user/council/` directory
- Council mode works best for subjective or creative questions where multiple viewpoints add value
- For factual questions, General mode with `delegate_technical` is more appropriate
