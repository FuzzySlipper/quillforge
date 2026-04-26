# Product Requirements Overview

This is the living product overview for QuillForge. It is intentionally short and points to the implementation and architecture notes that carry the detailed rules.

## Product Goal

QuillForge helps authors build, explore, and write within richly detailed fictional worlds through a conversational interface backed by specialized AI agents.

The product should support:
- lore-backed question answering and retrieval
- guided writing assistance and long-form prose generation
- roleplay and character-driven sessions
- multi-advisor council workflows
- autonomous Forge pipelines for longer-form story production
- durable user-owned content, config, and session history

## Product Shape

QuillForge is a ground-up C#/.NET rewrite of an older Python/FastAPI application. The rewrite favors:
- explicit architecture boundaries
- file-backed user-owned content
- testable services and stores
- session-owned runtime state
- provider integration through QuillForge-owned abstractions

## Current User-Facing Capabilities

The current build centers on:
- Guide, Writer, Roleplay, Lore Builder, Forge, Council, and Research modes
- lore browsing, lore-document creation, and lore-backed answering
- branching conversation history
- profile-driven runtime behavior
- writer pending-content review flows
- Forge pipeline stages for planning through assembly

See `README.md` for the current repo-level feature list and setup instructions.

## Product Boundaries

The most important implementation boundaries for current work are:
- app defaults vs reusable profiles vs session runtime vs conversation artifacts
- typed QuillForge-owned models vs raw provider transport payloads
- store-owned persistence boundaries instead of endpoint-owned file writes
- named services and handlers instead of ambient state or reflection-driven registration

## Reference Specs

Use these as the detailed follow-on docs:

- Ownership model: `docs/architecture/profile-session-conversation-ownership.md`
- Session-owned command/event example: `docs/architecture/session-runtime-command-event-reference.md`
- Service/command/event conventions: `docs/architecture/service-command-event-conventions.md`
- LLM transport and typed boundary rules: `docs/architecture/llm-transport-boundary.md`
- Implementation patterns and examples: `docs/architecture/agent-implementation-patterns.md`
- Content layout and persistence rules: `docs/architecture/content-layout-and-persistence.md`
- Synthetic/manual build testing workflow: `docs/synthetic-testing.md`

## Testing Expectations

QuillForge work should be validated at the right level for the change:
- code changes should include focused automated coverage when practical
- runtime-sensitive behavior should be exercised through synthetic/manual build testing when requested
- review handoffs should include scope, tests run, and any known gaps
