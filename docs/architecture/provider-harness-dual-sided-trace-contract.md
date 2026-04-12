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

## Current Boundary

Task `580` establishes:

- the dual-sided trace schema
- app-side and artifact-side capture helpers
- a reusable structural evaluator core

Later tasks extend this with:

- Forge-specific assertion packs
- persisted harness-run artifact directories
- richer report generation
- full debug-bridge and Forge-driver orchestration
