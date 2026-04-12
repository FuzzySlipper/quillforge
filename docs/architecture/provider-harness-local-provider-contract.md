# Local Harness Provider Contract

## Status

Implemented foundation for Den task `579`.

## Purpose

This document defines the first durable contract for QuillForge's local
OpenAI-compatible harness provider. The goal is to exercise normal provider
resolution, HTTP transport, and streaming paths without adding special
shortcuts inside QuillForge itself.

The reference implementation currently lives in:

- `tests/QuillForge.ProviderHarness.Tests/`

That project intentionally sits on the testing side of the boundary rather than
inside production web/runtime code.

## Endpoints

The harness exposes two OpenAI-compatible endpoints:

- `GET /v1/models`
- `POST /v1/chat/completions`

`/v1/models` returns the normal OpenAI list shape:

```json
{
  "object": "list",
  "data": [
    { "id": "harness-basic", "object": "model", "created": 0, "owned_by": "quillforge-harness" }
  ]
}
```

`/v1/chat/completions` accepts normal OpenAI-style request payloads from
QuillForge. The harness records the raw request body plus a summarized view of:

- model
- whether streaming was requested
- message count
- tool count
- message role/previews
- content type and selected request headers

## Script Contract

The scripted provider behavior is currently modeled by these types:

- `HarnessProviderScenario`
- `HarnessResponsePlan`
- `HarnessStreamEventPlan`
- `HarnessProviderTrace`
- `IHarnessResponseSource`

`IHarnessResponseSource` is the key extension seam. The current implementation
uses `ScriptedHarnessResponseSource`, and the exploratory prototype now also
includes `WorkerBackedHarnessResponseSource`. Both plug in behind the same
interface without changing the HTTP host contract.

## Response Modes

The harness supports three deterministic response modes:

- `ScriptedComplete`
- `ScriptedStream`
- `FaultInjected`

`ScriptedComplete` emits a normal non-streaming chat completion response.

`ScriptedStream` emits SSE frames with realistic OpenAI chunk envelopes plus a
`[DONE]` marker unless the plan disables it.

`FaultInjected` uses the same streaming transport but allows scripted faults
such as:

- abrupt disconnect after a chosen chunk count
- omitted `[DONE]` marker
- raw custom SSE frames
- raw custom JSON chunk payloads
- chunk delays and initial response delay

This keeps malformed/partial transport behavior under test-host control rather
than smuggling fake states through QuillForge internals.

## Trace Contract

Each `/v1/chat/completions` request produces a `HarnessProviderTrace`.

The trace captures:

- scenario name
- request method/path
- raw request body
- parsed request summary
- response mode
- status code
- emitted SSE frames in order
- finish reason
- fault label
- error text
- total duration

Provider-side traces are intentionally separate from app-side debug-bridge or
session traces. Later evaluator work compares them instead of collapsing them
into one blended log.

## Artifact Directory

Each harness host now creates a dedicated testing-side run directory under the
system temp root:

- `.../quillforge-harness-runs/<timestamp>-<scenario>-<runId>/`

The current layout reserves stable slots for later dual-sided work:

- `run-manifest.json`
- `provider/traces/*.json`
- `app/`
- `artifacts/`
- `reports/`

`run-manifest.json` records the schema version, run id, scenario name, and the
provider trace files written for that run. Provider trace JSON files are stored
separately from normal QuillForge session/config storage.

## Current Guarantees

The current foundation proves:

- QuillForge can register the harness as a normal custom OpenAI-compatible provider
- normal non-streaming completions work through `ProviderRegistry`
- reasoning/tool-call streaming works through the reasoning adapter path
- provider-side traces capture raw payloads and emitted frames
- provider-side traces persist as stable JSON artifacts in a dedicated harness-run directory
- scripted disconnects are observable as provider-side faults

## Deferred Work

The following items belong to later harness tasks:

- dual-sided evaluator documents and structural assertions
- scenario-file loading beyond in-code scripted fixtures
- richer live-model agent-backed provider workers behind `IHarnessResponseSource`
