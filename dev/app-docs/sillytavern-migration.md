---
name: SillyTavern Migration
summary: How to split a SillyTavern-style mega prompt into QuillForge's owned prompt and canon layers
---

# SillyTavern Migration

If you are coming from SillyTavern, the biggest mindset shift is this:

QuillForge does **not** want one giant prompt or one giant character card doing
every job at once.

Instead, it splits reusable material across several owned layers so canon,
characterization, prose style, and workflow rules do not all fight inside one
blob of text.

## The Short Rule

- put **world facts** in lore
- put **character identity and voice** in character cards
- put **storytelling constraints** in narrative rules
- put **sentence-level prose taste** in writing style
- put **canon-retrieval behavior** in the librarian prompt
- put **Council/Research interface tone** in the assistant prompt

If a detail is only for the current request, keep it in the current user message
instead of turning it into a permanent prompt file.

## Quick Mapping

| If your SillyTavern material contains... | Put it in QuillForge | Why |
|---|---|---|
| world history, setting canon, timelines, locations, relationship facts, gifts, promises, lorebook entries | `user/lore/<set-name>/` | Lore is the canonical source the Librarian retrieves from |
| a character's personality, mannerisms, description, scenario, greeting, in-character framing | `user/character-cards/` | Character cards shape roleplay and character-specific context |
| POV rules, tense, pacing constraints, continuity rules, "do not freestyle canon", scene-structure requirements | `user/narrative-rules/` | Narrative rules are the structural guardrails above prose style |
| sentence rhythm, diction, density, dialogue ratio, sensuality level, "lush/minimal/plain" prose taste | `user/writing-styles/` | Writing style shapes how prose sounds once the scene is already grounded |
| instructions about verifying canon, re-reading lore, preferring source truth over invention, reconciling conflicting files | `user/librarian-prompts/` | The Librarian owns retrieval and canon-grounding behavior |
| friendly/concise/formal/helpful interface tone for Council or Research | `user/assistant/` | Assistant style only affects Council and Research presentation, not Writer or Roleplay canon |
| old "main prompt", "JB", "conductor", or routing persona text | nowhere as a live control layer | Routing is app-owned by mode now; legacy conductors are migration-only reference |

## What Goes Wrong If You Do Not Split It

When one file tries to contain canon, voice, prose style, workflow policy, and
retrieval behavior all at once, the model tends to:

- treat canon as flavor instead of as binding source material
- overwrite characterization with generic genre instincts
- blur "how to write" with "what is true"
- keep patching the last correction instead of re-grounding from source files
- become harder to debug because every failure looks like "the prompt was weird"

QuillForge's separation exists to make those failures easier to prevent and
easier to diagnose when they still happen.

## Before and After

### Typical SillyTavern-Style Mega Prompt

```text
Aurora is graceful, composed, classically elegant, and never manic-pixie.
Zayne gave her sapphires. Their relationship is rooted in mutual steadiness and
career support. Write in close third person past tense with lush but controlled
prose. Keep continuity tight. Always check canon before inventing details. Be
romantic but not goofy. If the user asks for council-style analysis, answer
warmly and clearly.
```

### Better QuillForge Split

- `user/character-cards/aurora.yaml`
  - Aurora's personality, composure, elegance, scenario framing, greeting
- `user/lore/<set>/relationships/aurora-zayne.md`
  - sapphires, relationship milestones, promises, support dynamic, timeline facts
- `user/narrative-rules/default.md`
  - close third person past tense, continuity expectations, re-ground on canon corrections
- `user/writing-styles/literary.md`
  - lush but controlled prose, romantic but not goofy, sentence texture choices
- `user/librarian-prompts/default.md`
  - check canon before inventing, cross-reference relevant lore before drafting
- `user/assistant/default.md`
  - warm and clear summary tone for Council/Research only

That split lets Writer and Roleplay ground scenes from canon first and then
render them in the right style, instead of hoping one giant prompt keeps every
concern straight at once.

## Important Non-Obvious Boundary

The editable **Assistant** prompt is **not** QuillForge's universal main prompt.

It only shapes the user-facing Assistant contract used in:

- Council mode
- Research mode

Do **not** put Writer or Roleplay canon, scene rules, or character identity in
the Assistant prompt and expect it to steer creative generation. Those modes use
their own grounded path through Narrative Director and Prose Writer.

## Character Cards from SillyTavern

QuillForge can import SillyTavern/TavernAI PNG cards into
`user/character-cards/`.

Use character cards for:

- personality
- description
- scenario framing
- greeting
- portrait

Do not treat an imported character card as the whole canon database for the
setting. If the card includes important world facts that multiple scenes depend
on, move those facts into lore as well.

## Good Default Migration Order

1. Import or recreate your important character cards.
2. Move reusable world facts into a lore set.
3. Write a narrative-rules file for structural constraints.
4. Write a writing-style file for prose texture and taste.
5. Add a librarian prompt that reinforces canon-first retrieval behavior.
6. Optionally customize the Assistant prompt if you use Council or Research.
7. Test the result in Writer or Roleplay, not in Guide mode.

## Common Migration Mistakes

- Putting all canon in a character card instead of in lore.
- Putting prose style in lore instead of in writing style.
- Using narrative rules as a biography dump.
- Putting Writer or Roleplay instructions into the Assistant prompt.
- Expecting legacy conductor files to still control routing.
- Storing request-specific scene instructions in permanent prompt files.

## Mental Model to Keep

QuillForge works best when you treat it like:

- **Lore:** what is true
- **Character card:** who this person is
- **Narrative rules:** how scenes must be structured
- **Writing style:** how the prose should sound
- **Librarian prompt:** how canon should be gathered and checked
- **Assistant prompt:** how Council/Research should talk to you

Canon first. Synthesis second. Invention last.
