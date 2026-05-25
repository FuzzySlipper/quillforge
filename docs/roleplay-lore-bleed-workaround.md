# Temporary User Workaround: Roleplay Lore Bleed

**Applies to:** QuillForge roleplay sessions where character-owned details from
non-active characters appear (bleed) into active-character narration.

**Task #1641 provides a durable structured protocol fix.** The items below are
temporary workarounds until the protocol is fully integrated into the UI and
prompts. Do NOT treat lore-file splitting or negative exclusion blocks as the
durable fix.

## Workaround: Use Scene-Specific Context Corrections

If you see off-character details bleeding into the active character's narration
(e.g., Caleb's prosthetic arm appears in Xavier's description), you can correct
it per-session without changing lore files:

1. **During the scene**, say something like:
   > "That detail is about Caleb, not Xavier. Rewrite without it."

2. **Use the correction as session canon.** The Narrative Director prompt
   treats user corrections as a signal to re-ground against canon. The non-
   bleeding correction will be captured in sticky session canon for the
   remainder of the session.

3. **If the same bleed pattern repeats across sessions**, file a Den task
   with the exact text of the lore file, the query that triggered the bleed,
   and the incorrect output. The structured protocol diagnostics (available
   since #1641) can trace the offending fact to its source file for debugging.

## What NOT to Do

The following approaches are NOT durable fixes:

- **Lore-file splitting** (e.g., moving Caleb's prosthetic detail to a separate
  file named `caleb-secret.md`). Lore file organization helps readability but is
  not a protocol-level fix. The structured classifier uses file-path heuristics
  and content analysis, not file-naming conventions, to determine applicability.

- **Negative exclusion blocks** (e.g., "NOT Caleb" in the narrative rules or
  persona card). The Narrative Director prompt explicitly warns against durable
  negative exclusion blocks as the primary solution. User-authored corrections
  are appropriate as temporary overrides for a single session, but they should
  not replace the structured knowledge protocol.

- **Adding "Caleb-only" prefixes** to lore passages. The structured classifier
  already detects off-character details by name mentions and source file path.
  Content-level markers are redundant once the protocol is fully wired.

## When to Use This Workaround

Use this workaround when:

- You are running a pre-#1641 build of QuillForge.
- You have not yet enabled the roleplay structured protocol.
- The structured protocol is enabled but a specific edge case slips through
  (report as a Den task).

The structured protocol (#1641, #1661) is the durable fix. When fully deployed,
it classifies every lore passage by applicability (does it apply to the active
character or not?) and allowed-use (can it be asserted as fact, or only used as
background/context?). The query_lore and query_context handlers filter off-
subject evidence by default, and the ProseWriter prompt enforces the
classification at generation time.

## See Also

- `docs/roleplay-agent-structured-protocol.md` — the structured protocol spec
- `docs/roleplay-drift-harness.md` — drift detection harness
