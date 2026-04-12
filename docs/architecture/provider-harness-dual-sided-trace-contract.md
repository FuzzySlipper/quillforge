# Dual-Sided Harness Trace Contract

## Status

Implemented foundation for Den task `580`.

## Purpose

This document defines the first stable schema for comparing:

- what the local harness provider observed and emitted
- what QuillForge surfaced app-side
- what QuillForge persisted or wrote to disk

The concrete implementation currently lives in:

- `tests/QuillForge.ProviderHarness.Tests/`

## Top-Level Run Shape

The root model is `DualSidedHarnessRun`.

It currently carries:

- run id
- scenario name
- timestamps
- provider traces
- app trace
- artifact trace

This shape is intentionally reusable for both general chat and Forge-focused
scenarios. Nothing in the schema assumes a Forge-only run.

## Current Run Artifact Layout

Provider-side persistence now uses a dedicated testing-only run directory rooted
under the system temp path:

- `quillforge-harness-runs/<timestamp>-<scenario>-<runId>/run-manifest.json`
- `quillforge-harness-runs/<timestamp>-<scenario>-<runId>/provider/traces/*.json`
- `quillforge-harness-runs/<timestamp>-<scenario>-<runId>/app/`
- `quillforge-harness-runs/<timestamp>-<scenario>-<runId>/artifacts/`
- `quillforge-harness-runs/<timestamp>-<scenario>-<runId>/reports/`

`run-manifest.json` is versioned independently from QuillForge runtime state.
The current schema version is `1`. Additive fields should preserve the current
version; breaking shape changes should increment it and keep the old manifest
readable by explicit migration or fallback handling in the test-side loaders.

## Correlation Model

The current correlation chain is:

- `DualSidedHarnessRun.RunId` is the stable run identifier
- `run-manifest.json` repeats that `runId` on disk
- each `HarnessProviderTrace` has a stable `TraceId` per provider request
- each provider stream frame has a monotonic `Sequence`
- `HarnessAppTrace`, `HarnessForgeAppTrace`, and `HarnessArtifactTrace` carry
  `RunId` plus `RelatedProviderTraceIds`
- persisted reports link back to provider trace files and therefore to the
  underlying `TraceId` values

That gives the evaluator and a human reader one stable chain:

`runId -> provider trace ids -> app/artifact traces -> persisted report`

The app-side event stream currently uses ordered event lists rather than a
second synthetic event-id layer. Sequence is preserved by list order and by the
provider frame sequence numbers.

## Provider Trace

`HarnessProviderTrace` is the provider-side authority.

It includes:

- raw request body
- parsed request summary
- response mode
- optional worker trace metadata for exploratory worker-backed runs
- emitted SSE frames
- parsed text deltas
- parsed reasoning deltas
- parsed tool calls
- finish reason
- usage
- duration
- fault and error information

The raw body and emitted frames stay in the trace even when the evaluator also
uses parsed summaries. This keeps the evidence inspectable.

## App Trace

`HarnessAppTrace` is the app-side authority.

It includes:

- final content and stop reason
- usage and tool rounds
- typed app events
- summarized text deltas, reasoning deltas, tools, and diagnostics
- persisted message snapshots
- optional writer state

The current builder is `HarnessAppTraceBuilder.FromCollectedStream(...)`.

It accepts a neutral collected-stream shape rather than directly depending on
debug-bridge DTO types. That keeps the evaluator layer transport-agnostic while
still matching QuillForge's debug-bridge event vocabulary:

- `text_delta`
- `reasoning_delta`
- `tool`
- `diagnostic`
- `done`

## Artifact Trace

`HarnessArtifactTrace` captures selected filesystem observations.

Each `HarnessArtifactSnapshot` records:

- relative path
- absolute path
- whether the file exists
- content
- last-modified timestamp
- byte length

The current collector is `HarnessArtifactCollector.CaptureAsync(...)`.

This is deliberately selective. It snapshots named files relevant to a run
rather than crawling the entire user tree.

## Evaluator Contract

The evaluator model is:

- `IHarnessAssertion`
- `HarnessAssertionResult`
- `HarnessFinding`
- `HarnessEvaluationResult`

The evaluator is intentionally structural. It compares evidence instead of
free-form semantic judgment.

Current assertion types:

- `ExpectedProviderRequestSectionAssertion`
- `ExpectedToolMirroredAcrossBoundaryAssertion`
- `ExpectedFinalContentConsistencyAssertion`
- `ExpectedPersistedAssistantContentAssertion`
- `ExpectedArtifactPresenceAssertion`
- `ExpectedStopReasonConsistencyAssertion`
- `ExpectedWorkerRoleObservedAssertion`

These cover both general chat runs and file-producing flows without hardcoding
Forge-specific workflow rules into the evaluator core.

## Report Format

Persisted run reports are now written as `HarnessPersistedRunReport` JSON plus a
matching markdown summary in the run `reports/` directory.

The JSON report currently includes:

- schema version
- kind (`interactive` or `forge-phase`)
- run id
- scenario name
- scope name (mode or phase)
- created-at timestamp
- overall status
- linked provider trace files
- linked app/manifest/artifact files when present
- session usage summary when present
- assertion results
- findings

The markdown summary is intentionally human-first. It repeats the run identity,
status, linked evidence files, usage summary, and the recorded findings.

Treat `HarnessPersistedRunReport` and `run-manifest.json` as additive-first
schemas. New optional fields can be added without a version bump. Breaking
shape changes should increment the schema version and update the test-side
readers explicitly rather than silently changing report meaning.

## Worked Forge Example

For a canonical Forge pause/resume run, the artifact directory now looks like:

- `run-manifest.json`
- `provider/traces/001-<traceId>.json`
- `provider/traces/002-<traceId>.json`
- `app/forge-design-trace.json`
- `artifacts/forge-design-manifest.json`
- `artifacts/forge-design-artifact-trace.json`
- `reports/forge-design-evaluation.json`
- `reports/forge-design-summary.md`

The same pattern repeats for `start` and `approve`, all sharing the same
`runId` while pointing to the provider traces relevant to that phase.

## Current Fixture Workflow

Deterministic regression fixtures are currently stored as typed
`HarnessProviderScenario` objects inside the harness test project rather than
as free-form external files.

Current replay flow:

1. Build a typed `HarnessProviderScenario` in a test or helper.
2. Start `HarnessProviderHost` with that scenario.
3. Drive QuillForge through `HarnessInteractiveScenarioRunner` or
   `HarnessForgeScenarioRunner`.
4. Inspect the persisted provider traces, app traces, artifact snapshots, and
   evaluator reports in the run directory.

This keeps replay deterministic without depending on live external providers.
If later tasks introduce file-backed fixture formats, they should deserialize
into the same typed models instead of inventing a second ad hoc scripting
surface.

## Current Boundary

Task `580` establishes:

- the dual-sided trace schema
- app-side and artifact-side capture helpers
- a reusable structural evaluator core

Later tasks extend this with:

- Forge-specific assertion packs
- persisted app-side and evaluator artifacts in the run directory
- scenario-fixture and regression-workflow formalization
- full debug-bridge and Forge-driver orchestration
