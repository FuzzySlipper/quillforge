# Provider Harness Agent-Worker Prototype

## Status

Implemented prototype for Den task `582`.

## Related Work

- Den task `582` — Prototype agent-backed provider workers for exploratory harness runs
- Den task `579` — Design local OpenAI-compatible harness provider for QuillForge tests
- Den task `580` — Add dual-sided trace schema and evaluator for provider/app comparison
- Den task `581` — Expand synthetic Forge test runner to use harness provider and debug bridge
- `docs/architecture/provider-harness-dual-sided-testing.md`
- `docs/architecture/provider-harness-local-provider-contract.md`
- `docs/architecture/provider-harness-dual-sided-trace-contract.md`

## Purpose

This document defines the exploratory worker-backed lane for the harness
provider.

The deterministic scripted harness remains the primary regression authority.
Worker-backed mode exists to answer a different question:

- if QuillForge sends a realistic planner/writer/reviewer/librarian request,
  can a role-specific synthetic provider actor respond in a way that still
  exercises transport, tool-loop, and manifest behavior without pre-scripting
  every turn?

That makes this mode useful for exploration, diagnosis, and future richer
synthetic-user runs, but not a replacement for scripted regression fixtures.

## Separation of Authority

The prototype keeps three roles separate:

- provider worker: proposes the assistant response candidate for one provider request
- harness provider host: shapes that candidate into OpenAI-compatible complete or
  streamed wire output and records transport-side traces
- evaluator: reads traces and artifacts only; it never generates provider output

That separation is the key rule. The worker is allowed to author content; it is
not allowed to grade itself.

## Implemented Shape

The current implementation lives in:

- `tests/QuillForge.ProviderHarness.Tests/HarnessWorkerResponseSource.cs`
- `tests/QuillForge.ProviderHarness.Tests/HarnessExploratoryWorkerScenarioTests.cs`

The relevant types are:

- `HarnessWorkerScenario`
- `HarnessWorkerRoute`
- `WorkerBackedHarnessResponseSource`
- `IHarnessProviderWorker`
- `HarnessWorkerTrace`

The worker-backed source plugs in behind the existing `IHarnessResponseSource`
interface, so the HTTP host contract does not change. The harness host still
owns:

- `/v1/models`
- `/v1/chat/completions`
- request capture
- provider trace capture
- complete-vs-stream response shaping
- finish reasons
- fault injection hooks

The worker source only decides what the candidate response should contain.

## Worker Roles

The prototype includes these role-specific workers:

- `Planner`
- `Writer`
- `Reviewer`
- `Librarian`
- `GeneralChat`

For the first exploratory run, the Forge path uses:

- planner worker for planning and design refinement
- writer worker for chapter drafting
- librarian worker for lore lookup during the draft
- reviewer worker for scoring and extracted details

## Current Prototype Behavior

The current workers are request-driven but still deterministic. They inspect the
incoming provider request, then synthesize a role-appropriate response:

- planner worker emits `write_file` tool calls for plan artifacts, then returns a
  planning-complete message on the follow-up request
- writer worker emits a `query_lore` tool call before drafting, then writes the
  chapter after the lore result arrives
- librarian worker returns structured JSON built from the configured harness lore
- reviewer worker returns rubric JSON and extracted details for run-lore capture

This is intentionally a prototype seam, not a claim that the worker backend is
already a live LLM orchestration layer. The current implementation proves the
boundary and trace model first.

## Trace Contract

Each provider trace may now include `WorkerTrace` metadata with:

- worker role
- strategy label
- started/completed timestamps
- request summary
- note string
- proposed tool-call count
- output preview

This keeps exploratory runs debuggable without mixing worker internals into
QuillForge runtime state.

## Exploratory Run Coverage

The prototype demonstration is:

- `HarnessExploratoryWorkerScenarioTests.ExploratoryWorkerBackedForgeScenario_RunsEndToEndAndCapturesWorkerRoles`

That run proves:

- a worker-backed response source can answer real Forge provider requests
- the existing Forge harness runner can execute end-to-end without a scripted
  request queue
- provider traces still capture each request
- worker roles remain visible in trace metadata
- the evaluator can still validate manifest progression and file artifacts

## Risks and Guardrails

### Nondeterminism

If the worker backend later becomes a live LLM or spawned-agent system, output
variation will increase. That creates two risks:

- exploratory runs become harder to compare across time
- assertions tuned for exact content become noisy

Guardrail:

- keep deterministic scripted mode as the regression lane
- keep worker-backed mode focused on structure-first assertions

### Evaluator Bias

If the same worker or model family both generates the provider response and
grades the run, the harness can drift into self-confirming behavior.

Guardrail:

- evaluator authority stays separate from the worker source
- worker traces are evidence only, never verdicts

### Runtime Cost

Worker-backed exploratory runs can become much more expensive than scripted
fixtures, especially if a future backend uses live LLM calls or multi-agent
delegation.

Guardrail:

- keep the current prototype cheap and local
- treat live-model worker backends as optional overlays, not the default CI path

### Transport Drift

If workers start emitting raw provider JSON directly, the harness loses control
over transport realism and fault injection.

Guardrail:

- workers return response candidates
- the harness host continues to own wire shaping and emitted frame recording

## Recommended Next Step

If this exploratory lane is extended further, the next safe improvement is:

- add a second worker runtime behind the same interface for optional live-model
  experimentation, while preserving the current deterministic prototype worker
  runtime for CI and local debugging

That would deepen exploration without weakening the scripted transport-realistic
baseline.
