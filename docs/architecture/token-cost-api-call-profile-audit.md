# Token Cost And API Call Profile Audit

Date: 2026-04-12

## Goal

Revisit the post-rewrite call topology now that the Guide/Assistant/Narrative
Director architecture has stabilized enough to measure realistically, and decide
whether any optimization is worth landing now.

## What Was Measured

Two kinds of evidence were used:

1. Harness-backed interactive baseline scenarios in
   `tests/QuillForge.ProviderHarness.Tests/HarnessInteractiveCostProfileTests.cs`
   to lock in the minimum expected call topology for grounded Writer and
   Roleplay turns.
2. Local operational `user/data/llm-debug.log` inspection to confirm that real
   turns can exceed the harness baseline when agents recurse through extra lore
   checks or additional tool rounds.

For this audit, the provider-side harness traces were used as the exact
baseline source for interactive request counts. That audit also exposed a
session usage tracking bug for streamed top-level orchestrator rounds, which
was fixed in follow-up task `#643`. The current session usage summary now
matches those grounded interactive call floors in the covered Writer,
Roleplay, and streaming chat paths.

## Baseline Findings

### Writer Baseline

A canon-sensitive Writer turn with one lore lookup currently uses:

- 2 orchestrator calls
- 2 narrative-director calls
- 2 prose-writer calls
- 2 librarian calls

Total: **8 provider calls** for a single grounded turn.

This is the expected minimum for the current layered design:

- top-level orchestration decides to route through `direct_scene`
- Narrative Director grounds the request and verifies canon through its own
  `query_lore` call before it hands off to `write_prose`
- Prose Writer then verifies the scene-specific canon again through its own
  `query_lore` call before drafting
- Librarian answers both lore queries
- control unwinds back up through Prose Writer, Narrative Director, and the top-level orchestrator

### Roleplay Baseline

Roleplay uses the same minimum layered shape when the turn is grounded cleanly:

- 2 orchestrator calls
- 2 narrative-director calls
- 2 prose-writer calls
- 2 librarian calls

Total: **8 provider calls**.

### Real-World Expansion Above Baseline

The harness baseline is intentionally minimal. Real turns can exceed it.

During this audit, local operational logs showed a canon-sensitive roleplay turn
expanding beyond the 8-call baseline because:

- Prose Writer issued multiple lore queries before it was satisfied
- Narrative Director needed an additional tool round before returning the final scene

That means the real question is not "is the current architecture multi-call?"
because it clearly is. The real question is whether the extra layers are buying
enough grounding quality and debuggability to justify that cost.

## Budget And Caching Review

### Narrative Director Budget

Current defaults:

- `max_tokens = 4096`
- `max_tool_rounds = 8`

Conclusion:

- Keep them for now.
- The call topology is expensive, but the cost driver is more often the number
  of nested turns and lore retries than the Director's output length alone.
- Lowering the ceiling right now would risk truncating legitimate grounded
  turns before we have enough production evidence that 8 rounds is excessive.

### Prose Writer Budget

Current defaults:

- `max_tokens = 8192`
- `max_tool_rounds = 10`

Conclusion:

- Keep them for now.
- The writer is the visible renderer and benefits most directly from a generous
  output ceiling.
- If future optimization is needed, reducing unnecessary lore retries is likely
  a better first lever than squeezing prose output budget.

### Prompt Caching

Current state:

- Librarian already supports `CacheSystemPrompt`
- Narrative Director and Prose Writer do not opt in

Conclusion:

- Do **not** enable prompt caching for Narrative Director or Prose Writer yet.
- Librarian is the strongest caching candidate because its system prompt is
  mostly a large, stable lore corpus.
- Narrative Director and Prose Writer currently build highly turn-variant system
  prompts that include changing scene context, story state, notes, or file
  context. Marking the entire prompt for caching is unlikely to produce reliable
  wins until the stable and dynamic sections are split more deliberately.

## Session Context Rebuild Cost

`InteractiveSessionContextService.BuildAsync(...)` is not a meaningful cost
driver compared with the LLM stack.

The rebuild path is a small fixed set of file-backed lookups:

- optional character card load
- story-state load
- optional current-file tail read
- optional active-plot read
- in-memory plot-progress summary formatting

This is important to keep explicit and correct, but it is not the first place
to optimize unless profiling later proves content I/O is a real bottleneck.

## Decision

No architecture collapse is justified yet.

Specifically:

- do not inline Prose Writer into Narrative Director yet
- do not remove Narrative Director from Writer/Roleplay
- do not trim budgets aggressively yet
- do not turn on broad prompt caching for dynamic creative prompts yet

The grounded architecture is expensive, but it is also buying:

- canon-first scene planning
- explicit story/narrative state ownership
- a clean separation between planning and rendering
- better failure boundaries than a single giant creative prompt

## Action Taken In This Audit

Instead of flattening layers prematurely, this audit landed two lower-risk
improvements:

1. Harness-backed baseline tests for Writer and Roleplay call topology.
2. Better `llm-debug.log` attribution so live runs log real agent names like
   `orchestrator`, `narrative-director`, `prose-writer`, and `librarian`
   instead of only generic `ToolLoop` labels.

That improves future optimization work because real usage will now be easier to
inspect without guessing which layer made which call.

## Revisit Triggers

Re-open optimization when one of these becomes true:

- users report latency on grounded Writer/Roleplay turns as a top pain point
- real logs show repeated multi-lore-query churn is common enough to target
- prompt caching can be applied to stable prompt prefixes instead of entire
  dynamic prompt blocks
- the harness expands to capture more representative research/council cost paths

Until then, the current recommendation is:

**keep the layered architecture, improve observability, and optimize only when
real usage data shows a specific hotspot worth trading quality for.**
