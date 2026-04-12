---
name: Canon-First Grounding
summary: How Writer and Roleplay should ground lore, rules, and character context before prose
---

# Canon-First Grounding

Canon-sensitive prose in QuillForge should be grounded before it is rendered.

## Where This Applies

- **Writer mode**
- **Roleplay mode**

These modes should not jump straight from user request to first-order prose.

## Required Grounding Inputs

Depending on the request, the grounded flow may need:

- active lore set
- relevant lore/profile files
- narrative rules
- writing style
- story state
- roleplay character context
- current file or project context

## Intended Flow

1. Gather the relevant canon and runtime context
2. Route through Narrative Director
3. Produce a grounded scene/response brief
4. Render the visible prose through Prose Writer

Narrative Director owns the grounding and next-beat decision. Prose Writer owns
the final wording.

## Corrections

If the user says the characterization, relationship dynamic, or lore detail is
wrong, that should trigger re-grounding.

Do not:

- patch one sentence
- keep writing from the old assumption

Do:

- revisit the relevant canon inputs
- reconcile the correction with the grounded brief
- continue from the corrected understanding

## Missing Canon

If required canon or mode prerequisites are missing, the system should disclose
that and stop rather than improvising around it.

Examples:

- missing lore for a canon-sensitive scene
- missing narrative rules or writing style
- missing selected character context in Roleplay

The correct fallback is disclosure, not freestyle prose.
