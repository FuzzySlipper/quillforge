# Provider Harness for Dual-Sided Synthetic Testing

## Status

Implemented foundation for tasks `579` through `581`.

## Related Work

- Den task `578` — Build agent-driven provider harness for dual-sided Forge test validation
- Den task `579` — Design local OpenAI-compatible harness provider for QuillForge tests
- Den task `580` — Add dual-sided trace schema and evaluator for provider/app comparison
- Den task `581` — Expand synthetic Forge test runner to use harness provider and debug bridge
- Den task `582` — Prototype agent-backed provider workers for exploratory harness runs
- `docs/architecture/provider-harness-agent-worker-prototype.md`
- `docs/architecture/provider-harness-local-provider-contract.md`
- `docs/architecture/provider-harness-dual-sided-trace-contract.md`
- `docs/architecture/llm-transport-boundary.md`
- `docs/architecture/session-runtime-command-event-reference.md`
- `docs/architecture/profile-session-conversation-ownership.md`
- Den document `testing-strategy-analysis-2026-04`

## Current Forge Fixture Shape

Synthetic Forge scenarios are now expressed as typed
`HarnessForgeScenarioFixture` objects in the harness test project.

The fixture currently owns:

- project name
- premise
- provider script selection via `HarnessProviderScenario`
- ordered phase list
- per-phase artifact targets
- per-phase structural expectations

`HarnessForgePhaseFixture` keeps phase intent explicit:

- operation (`design`, `start`, `approve`)
- artifact paths to capture
- expected request sections
- expected manifest stage / paused state
- expected chapter ids
- expected artifact files
- optional pause-surfaced requirement

The canonical first scenario is `HarnessForgeScenarioFixtures.CreateCanonicalPauseResume(...)`.
Tests round-trip it through JSON before execution so the fixture shape is not
only an in-memory convenience.

When extending the fixture format:

- add additive fields first
- keep expectations structural rather than semantic free-form judgments
- translate new expectation fields into `IHarnessAssertion` instances in one place
- avoid bypassing the typed fixture by embedding ad hoc phase logic directly in tests

## Current Regression Lane

The current repeatable entrypoint is the harness test project itself:

```bash
dotnet test tests/QuillForge.ProviderHarness.Tests/QuillForge.ProviderHarness.Tests.csproj
```

That suite currently includes:

- local harness-provider transport coverage
- interactive layered-call cost profiles
- canonical Forge pause/resume regression coverage
- exploratory worker-backed harness coverage
- persisted report and mismatch-report checks

When a harness run fails, inspect the dedicated run directory under the system
temp root:

- `quillforge-harness-runs/<timestamp>-<scenario>-<runId>/run-manifest.json`
- `.../provider/traces/*.json`
- `.../app/*.json`
- `.../artifacts/*.json`
- `.../reports/*.json`

Start with the markdown summary in `reports/`, then move to the linked JSON
artifacts for the smallest useful evidence slice. The regression lane is meant
to fail with those persisted artifacts available, rather than only a bare test
assertion and stack trace.

## Purpose

This document defines a durable implementation plan for a testing harness that
can observe both sides of QuillForge's LLM boundary during synthetic runs:

- what QuillForge sends to a provider
- what the provider streams back
- what QuillForge surfaces to the user
- what QuillForge persists to sessions, manifests, and project files

The main goal is to catch bugs that sit between "the code looks correct" and
"the provider actually received the intended request and the user actually saw
the intended result."

This document is intentionally more concrete than a task description. It exists
to prevent the implementation from collapsing into a thin fake interface that
marks tasks complete without exercising the transport, streaming, and
user-visible boundaries where QuillForge has historically been brittle.

## Problem

QuillForge is now structurally much better at catching internal state and
ownership bugs, but one major class of failure remains difficult to see:

- the app constructs the wrong provider request shape
- the provider stream is chunked or malformed in a way the app handles badly
- tool calls emerge provider-side but are not surfaced app-side
- the provider produced enough output for the app to continue, but not enough
  for the user-facing workflow to make sense
- Forge stages appear to complete, but the provider-visible and user-visible
  traces diverge in subtle ways

These are not pure unit-test failures and not pure UI failures. They live at
the app/provider boundary.

## Goals

- Exercise QuillForge through normal provider configuration, HTTP transport, and
  streaming paths.
- Observe provider-side requests and app-side results in one run.
- Support deterministic regression coverage first.
- Support exploratory agent-driven provider workers later.
- Produce structured evidence rather than only free-form logs.
- Reuse the existing debug bridge, scenario runner direction, and cassette
  ideas where they fit.

## Non-Goals

- Do not replace production provider implementations.
- Do not introduce reflection-based registration or discovery.
- Do not bypass normal app/provider transport by injecting special test-only
  behavior into the core runtime path.
- Do not make the evaluator depend on the same free-form agent that generated
  provider responses.
- Do not start with browser-only puppeting as the primary test authority.

## Core Design Principles

### 1. The harness must exercise the real provider boundary

The first implementation must use a local OpenAI-compatible HTTP endpoint,
registered through normal QuillForge provider configuration, so the app still
executes:

- provider resolution
- request serialization
- HTTP transport
- streaming parsing
- finish-reason handling
- timeout handling

An in-process `ICompletionService` fake remains useful for lower-level tests but
is not sufficient as the main harness.

### 2. Provider evidence and app evidence are separate first-class artifacts

The system must not reduce everything to one merged log stream. The evaluator
needs separate authority domains:

- provider-side trace
- app-side trace
- persisted artifact snapshot

Only then can it detect divergence.

### 3. Deterministic regression coverage comes before agent-driven exploration

The harness should first support scripted provider behavior:

- exact streamed text deltas
- exact tool call emergence
- exact malformed payload cases
- exact timeout/disconnect cases

Agent-backed provider workers are valuable, but only after the deterministic
layer exists.

### 4. The evaluator must be structurally opinionated

The evaluator should assert structural facts first:

- request contained expected sections
- tool call appeared on both sides
- manifest transitioned correctly
- expected files were created
- pause/resume surfaced consistently

Semantic judgment can be added later, but structural checks are the durable
baseline.

### 5. Avoid "single interface" shortcuts

The implementation should not be satisfied with:

- "we can swap in a fake completion service"
- "we can log requests somewhere"
- "we can inspect the final chapter file"

That is not enough. The harness is specifically meant to test the spaces
between those points.

## High-Level Architecture

The harness has four main components:

1. Harness Provider
2. App Driver
3. Trace Store
4. Evaluator

### 1. Harness Provider

This is a local HTTP service or test-hosted app exposing an OpenAI-compatible
chat completions API.

Responsibilities:

- receive raw provider requests from QuillForge
- emit raw or streamed responses in realistic provider wire format
- record raw request and response evidence
- support multiple response modes:
  - scripted deterministic fixture mode
  - fault-injection mode
  - agent-backed worker mode

Non-responsibilities:

- directly mutating QuillForge state
- deciding whether a run passed

### 2. App Driver

This drives QuillForge through real app surfaces.

Primary path:

- Development debug bridge
- Forge endpoints
- normal REST/SSE endpoints when needed

Secondary path:

- browser/UI automation for frontend-specific risk

The app driver is responsible for orchestration only. It is not the evaluator.

### 3. Trace Store

This stores normalized evidence from both sides of the run:

- provider-side raw payload references and summaries
- app-side stream/debug-bridge evidence
- manifest snapshots
- file artifact snapshots
- evaluator results

The trace store should produce stable JSON and markdown outputs that can be
archived with test artifacts or attached to Den work.

### 4. Evaluator

This compares provider-side and app-side traces against scenario expectations.

The evaluator should emit structured findings such as:

- `MissingToolSurface`
- `MissingManifestTransition`
- `PromptContextOmitted`
- `PauseSurfacedInManifestOnly`
- `ProviderStreamEndedWithoutUserVisibleCompletion`

The evaluator should produce both:

- machine-readable result objects
- human-readable summary reports

## Component Boundaries

## Harness Provider Boundary

The harness provider belongs at the transport edge. It may handle:

- raw HTTP
- raw JSON
- raw SSE chunking
- provider-shaped finish reasons
- deliberate malformed payloads for fault injection

It should not leak provider-owned raw payloads into QuillForge Core types.

## App Driver Boundary

The app driver should operate through QuillForge-owned boundaries:

- debug bridge endpoints
- Forge endpoints
- chat endpoints
- provider configuration endpoints if needed for setup

It should not mutate session state or manifests by writing files directly.

## Trace Boundary

Trace records are test artifacts, not runtime state.

They should not be pushed into `SessionState`, `ConversationTree`, or normal
provider config persistence. The trace store is a separate testing concern.

## Evaluator Boundary

The evaluator consumes traces and expectations. It does not call providers or
mutate QuillForge state directly.

This separation matters because it prevents an exploratory provider worker from
also acting as its own judge.

## Proposed Implementation Shape

## Phase 1: Deterministic Harness Provider

This phase should land task `579`.

### Proposed code location

Use a dedicated testing-side project rather than burying this in production Web
code. The current implementation lives in:

- `tests/QuillForge.ProviderHarness.Tests/`

This project now hosts:

- a lightweight ASP.NET Core app or `WebApplicationFactory`-friendly host
- scripted scenario fixtures
- trace serialization

### Proposed primary types

#### `HarnessProviderScenario`

Defines the provider-side script for one synthetic run.

Suggested responsibilities:

- route matching metadata
- response plan per request
- streaming plan
- fault-injection directives
- optional worker dispatch instructions for later phases

#### `HarnessProviderTrace`

One run's provider-side evidence.

Should include:

- request ID
- timestamp
- raw request path/method/headers summary
- request body path or embedded compact body
- model
- tool definitions summary
- message summaries
- response mode used
- streamed events emitted
- finish reason
- total duration

#### `HarnessChatCompletionsEndpoint`

A named endpoint type or minimal dedicated host wiring inside the harness
project.

Responsibilities:

- deserialize OpenAI-compatible request shape
- write streamed or non-streamed responses
- record provider trace entries
- invoke fixture/worker response generation

### Response modes

The harness provider must support at least three deterministic modes:

1. `ScriptedStream`
2. `ScriptedComplete`
3. `FaultInjected`

`FaultInjected` must support:

- partial tool call arguments across chunks
- malformed tool argument JSON
- text deltas followed by abrupt disconnect
- `tool_use`/equivalent finish reason without tool payload
- delayed responses that exceed configured app/provider thresholds

This is essential because QuillForge already contains recovery code for these
paths. The harness should prove those paths, not just acknowledge them.

## Phase 2: Dual-Sided Trace Model

This phase should land task `580`.

### Top-level trace document

Proposed top-level model:

`DualSidedHarnessRun`

Suggested fields:

- `RunId`
- `ScenarioName`
- `StartedAt`
- `CompletedAt`
- `ProviderTrace`
- `AppTrace`
- `ArtifactTrace`
- `EvaluatorResult`

### Provider trace

Suggested provider-side record families:

- `ProviderRequestObserved`
- `ProviderResponseStarted`
- `ProviderStreamChunkEmitted`
- `ProviderToolCallEmitted`
- `ProviderResponseCompleted`
- `ProviderResponseFaulted`

These are test-trace records, not QuillForge runtime events.

### App trace

Suggested app-side record families:

- `DebugBridgeChatStarted`
- `AppTextDeltaObserved`
- `AppToolEventObserved`
- `AppDiagnosticObserved`
- `AppDoneObserved`
- `AppPersistedObserved`
- `ForgeManifestSnapshotObserved`
- `ForgeStatusSnapshotObserved`

The app trace should be built from:

- debug bridge collected stream events
- Forge endpoint responses
- explicit manifest reads from the owning file service or endpoint-level status

### Artifact trace

Suggested artifact families:

- `ProjectFileSnapshot`
- `ManifestSnapshot`
- `SessionSnapshot`
- `FinalOutputSnapshot`

For Forge, artifact capture should focus on:

- `plan/premise.md`
- `plan/outline.md`
- `plan/style.md`
- `plan/bible.md`
- `plan/ch-*-brief.md`
- `drafts/ch-*.md`
- `output/story.md`
- `manifest.json`
- `run-lore.md`

### Trace storage location

Do not mix these with normal user content.

Recommended location:

- `user/data/harness-runs/`

with one directory per run:

- `user/data/harness-runs/{timestamp}-{scenario-name}/`

Suggested contents:

- `provider-trace.json`
- `app-trace.json`
- `artifact-trace.json`
- `evaluation.json`
- `summary.md`

This keeps the trace durable and inspectable without polluting session storage.

## Phase 3: App Driver Integration

This phase lands task `581`.

### Primary orchestration path

The first-class orchestration surface should be the Development debug bridge.

Why:

- deterministic
- already shaped for testing
- avoids browser noise for backend/transport assertions
- gives collected event arrays without SSE parsing in test code

The driver should still be able to call real Forge endpoints for behaviors that
the debug bridge does not currently cover.

### Required driver capabilities

The app driver should support:

- boot/reset session
- switch mode
- run debug-bridge chat stream
- start Forge design run
- start Forge full run
- approve/resume Forge
- poll Forge status
- snapshot relevant files

### Current implemented slice

The current harness runner covers a real Forge pause/resume workflow through an
in-process Development app plus the Forge-specific debug bridge endpoints:

- create Forge project
- run planning + design until the pipeline pauses before writing
- run writing/review until the pipeline pauses after chapter one
- approve/resume to final assembly
- collect provider-side request traces for each phase
- collect app-side Forge events and status snapshots for each phase
- capture manifest and key file artifacts (`plan/*`, `drafts/*`, `output/*`,
  `run-lore.md`)

This lives primarily in:

- `tests/QuillForge.ProviderHarness.Tests/HarnessForgeScenarioRunner.cs`
- `tests/QuillForge.ProviderHarness.Tests/HarnessForgeScenarioTests.cs`
- `src/QuillForge.Web/Endpoints/DebugBridgeEndpoints.cs`
- `src/QuillForge.Web/Contracts/DebugBridgeContracts.cs`

### Scenario model

Extend the scenario runner concept rather than creating an unrelated harness DSL.

Proposed additional scenario step families:

- `ConfigureProviderHarness`
- `RunForgeDesign`
- `RunForgeStart`
- `RunForgeApprove`
- `CaptureManifest`
- `CaptureArtifacts`
- `AssertDualTrace`

The scenario definition should be explicit enough that a future agent
implementing it does not invent ad hoc sequencing logic at every test site.

## Phase 4: Evaluator

This phase also belongs primarily to task `580`, with scenario use in `581`.

### Evaluator design

Use named, structural checks.

Do not begin with a single method like:

- `bool Evaluate(run)`

That shape will become opaque quickly.

Prefer:

- `IHarnessAssertion`
- `HarnessAssertionResult`
- `HarnessFinding`

with named assertion types such as:

- `ExpectedProviderPromptSectionAssertion`
- `ExpectedToolMirroredAcrossBoundaryAssertion`
- `ExpectedForgeChapterDiscoveryAssertion`
- `ExpectedPauseConsistencyAssertion`
- `ExpectedFinalOutputConsistencyAssertion`

### Example structural assertions

#### Prompt composition

Assert that a planner request included:

- premise
- lore context summary or full lore
- expected writing style section when applicable
- expected tool definitions

#### Tool call mirroring

Assert that:

- provider trace showed a tool call
- app trace showed tool dispatch or diagnostic handling
- resulting manifest/files changed consistently with the tool result

#### Forge chapter discovery

Assert that:

- provider/planner output caused `ch-*-brief.md` files to appear
- manifest snapshot contained matching chapter entries
- Writing stage processed those chapter IDs

#### Pause consistency

Assert that:

- pause appeared in app-side stream or explicit endpoint result
- manifest became paused
- status endpoint reflected paused state

#### Final output consistency

Assert that:

- provider stream produced substantive completion content
- app-side done event reflected completion
- final file output existed when the run reached Assembly/Done

### Evaluator output

The evaluator must produce:

- summary status
- flat list of findings
- references to evidence locations

Each finding should include:

- finding code
- severity
- expected behavior
- actual behavior
- evidence references

## Phase 5: Agent-Backed Provider Workers

This phase should land only after the deterministic harness is stable.

This is task `582`.

### Design intent

The harness provider should optionally delegate request handling to role-specific
workers:

- planner worker
- writer worker
- reviewer worker
- general chat worker

These workers are not authoritative judges. They are synthetic provider-side
actors.

### Worker constraints

Agent-backed workers should be wrapped by provider-side response adapters so the
harness still controls:

- exact wire shape
- chunking strategy
- finish reason
- timing
- malformed/fault modes when desired

This keeps provider realism in the harness rather than trusting a worker to emit
perfect provider JSON directly.

### Evaluator separation rule

The evaluator must not share authority with the worker that generated the
provider response.

The minimum separation is:

- worker generates response candidate
- harness adapter emits provider wire output
- evaluator reads traces only

The stronger separation for exploratory runs is:

- one worker acts as provider-side author
- one driver acts as app-side user/test runner
- one evaluator analyzes the traces

## Anti-Shortcut Guardrails

These are explicit because this work is very easy to underbuild.

### Guardrail 1

Do not satisfy task `579` with only a fake `ICompletionService`.

Reason:

- it does not test provider configuration, HTTP transport, serialization, or
  timeout behavior

### Guardrail 2

Do not satisfy task `580` with only free-form logs.

Reason:

- the evaluator needs stable structured evidence and references

### Guardrail 3

Do not satisfy task `581` with only browser-driven testing.

Reason:

- browser-only runs are slower, noisier, and weaker at isolating provider/app
  divergence than the debug bridge plus endpoint evidence

### Guardrail 4

Do not begin task `582` before deterministic scripted provider mode exists.

Reason:

- otherwise exploratory runs will be hard to interpret because both the actor
  and the instrumentation will still be unstable

### Guardrail 5

Do not let the evaluator rely primarily on semantic judgment.

Reason:

- structural mismatches are the durable source of truth and should fail first

## Suggested Implementation Order

1. Build the harness provider host with deterministic streaming and trace output.
2. Define the dual-sided trace schema and file layout.
3. Extend synthetic Forge scenarios to drive QuillForge against the harness.
4. Add structural evaluator assertions for the known Forge and streaming failure
   modes.
5. Only then layer in agent-backed provider workers for exploratory runs.

## Initial End-to-End Scenario

The first scenario should be intentionally narrow and high-value:

### Scenario: Forge design and first chapter pause

Setup:

- register QuillForge against the local harness provider
- create a Forge project
- write a premise
- run design
- run start until pause-after-chapter-1

Provider script should produce:

- planner output that results in `outline.md`, `style.md`, `bible.md`, and
  `ch-01-brief.md`
- writer output for chapter 1
- reviewer pass or pause-ready outcome

Required assertions:

- planner request contained premise and lore context
- planner tool/file effects surfaced on disk
- manifest discovered `ch-01`
- writer request included previous context and chapter brief
- chapter 1 draft existed
- pause surfaced consistently in:
  - stream/debug trace
  - manifest
  - Forge status endpoint

This single scenario would already catch a large class of Forge regressions.

## Open Questions

These should be resolved during implementation, but they are not blockers for
the architecture direction.

- Should the harness host live in its own test project or in an existing
  architecture/integration test host project?
- Should trace files be kept only on failure by default, or always written and
  pruned later?
- Should the first app driver use only the debug bridge, or should Forge
  endpoint coverage be mixed in from the start for greater realism?
- Should the evaluator emit Den tasks automatically for exploratory runs that
  find new failures, or should that remain a manual follow-up step?

## Recommended Defaults

- start with a separate harness test project
- always write traces for now; optimize retention later
- use debug bridge as the primary app driver and Forge endpoints where the debug
  bridge does not yet cover a needed surface
- keep Den task creation manual until the evaluator signal quality is proven

## Definition of Done for the Initiative

This initiative is complete when:

- QuillForge can run at least one Forge scenario against a local
  OpenAI-compatible harness provider
- the run produces provider-side and app-side structured traces
- the evaluator can detect divergence between provider-visible and
  user-visible behavior
- the scenario is strong enough to fail on real regressions involving provider
  payloads, streaming/tool boundaries, or Forge state transitions
- exploratory agent-backed workers are optional additions rather than the only
  mode
