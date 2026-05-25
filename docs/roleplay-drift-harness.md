# Roleplay Lore Drift Harness

A deterministic multi-turn roleplay evaluation harness for tracing and detecting lore drift — the leakage of off-character or forbidden facts into active character narratives.

## Purpose

This harness is the formal dependency for task #1661 (roleplay protocol refactor). It provides:

1. **Deterministic scripted multi-turn scenarios** — no live LLM required for CI runs.
2. **Boundary-level trace events** recording what happens at each component boundary (user turn, query_lore, narrative director, prose writer, visible response).
3. **Drift detection** — structural scanner that finds forbidden facts and identifies the first boundary where they appear.
4. **Structured payloads** in `#1661`-compatible format (RoleplayKnowledgePacket / StructuredSceneBrief), ready for the protocol refactor.
5. **Artifact writer** producing run.json, trace.ndjson, evaluation.json, lore-results.json, and summary.md.

## Quick Start (CI / Deterministic)

```bash
# Run the built-in Xavier/Caleb clean scenario
dotnet run --project src/QuillForge.RoleplayDriftHarness \
  -- --scenario xavier-caleb \
     --output-dir /tmp/qf-drift-run
```

This runs without any LLM — purely deterministic scripted boundary simulation. Exit code is 0 if no drift is detected, 2 if drift findings exist.

## Running Tests

```bash
dotnet test tests/QuillForge.RoleplayDriftHarness.Tests
```

Tests cover:
- Clean scenario passes (no drift)
- Contaminated scenarios at each boundary (query_lore, narrative director, prose writer, visible response) detect drift with correct origin classification
- Multiple forbidden facts produce separate findings
- Shared/body-tech evidence is recorded as `shared_world` / `context`
- First-appearance tracking across multiple turns
- Report writer produces all 5 artifact files

## Local LLM Evaluator Seam

The harness supports an optional live LLM evaluator extension point for qualitative analysis:

```bash
dotnet run --project src/QuillForge.RoleplayDriftHarness \
  -- --scenario xavier-caleb \
     --output-dir /tmp/qf-drift-run \
     --base-url http://localhost:1234/v1 \
     --model qwen3-35b
```

The `--base-url` and `--model` arguments accept any OpenAI-compatible endpoint. No cloud API key is required — works with local ollama, llama.cpp, or any local inference server.

**Important**: The deterministic structural drift checks are authoritative. The LLM evaluator mode is an extension seam for qualitative / experimental analysis only.

## Privacy Guardrails

- Only committed synthetic fixtures (in code) are used for CI runs.
- The private corpus at `/home/stash/lore/Deepspace-Linkon` is never committed, dumped into git, or included in artifacts.
- Artifacts include derived metrics, minimal provenance paths (file names), and compact content previews — never raw private lore dumps.
- Harness output defaults to `/tmp/quillforge/lore-drift/`. Use `--output-dir` to set a custom path.

## Scenario Format

Scenarios are defined as code (the `RoleplayScenario` sealed record). Built-in scenarios:
- **xavier-caleb** — Xavier (active character) vs Caleb (off-character). Forbidden details: prosthetic arm, Toring Chip.

To add a new scenario, create a static factory method returning `RoleplayScenario` and register it in `Program.LoadScenario()`.

## Report Schema

### run.json
Complete run metadata, scenario definition, trace event count, drift findings, and evaluation.

### trace.ndjson
One JSON line per boundary event. Each event has:
- `turn`, `component`, `boundary` — identification
- `agent`, `provider`, `model`, `timestamp`, `duration_ms` — when available
- `source_refs` — lore file references
- `preview` — compact content preview (max 200 chars)
- `content` — full content (max 5000 chars, truncated)
- `structured_payload` — #1661-compatible knowledge packet: `active_subject`, `applicability`, `allowed_use`, `lore_refs`, `source_component`

### evaluation.json
Top-level pass/fail, drift count, origin breakdown.

### lore-results.json
Structured knowledge packets extracted from trace events, suitable for #1661 evaluation handoff.

### summary.md
Human-readable markdown report with drift findings, origin breakdown, and trace event table.

## Boundary Types

| Boundary | Component | Description |
|----------|-----------|-------------|
| `UserTurn` | `user_turn` | Driver/user input turn |
| `QueryLore` | `query_lore` | query_lore / query_context or Librarian result |
| `NarrativeDirector` | `scene_brief` | Narrative Director output / structured scene brief |
| `ProseWriter` | `direct_scene` | ProseWriter / direct_scene output |
| `VisibleResponse` | `visible_response` | Visible assistant response |
| `SummaryHistory` | `summary` | (placeholder) Summary/history boundary |

## Drift Origin Classification

| Origin | Meaning |
|--------|---------|
| `retrieval` | Forbidden fact introduced by query_lore / Librarian retrieval |
| `director_synthesis` | Forbidden fact synthesized by Narrative Director |
| `prose_misuse` | Forbidden fact introduced by ProseWriter misuse of context |
| `visible_response` | Forbidden fact appeared only in visible response |
| `summary_history` | Forbidden fact in summary/history boundary |
| `provider_timing` | Provider/timing artifact |
| `uncertain` | Could not determine origin |

## Known Gaps (for #1661 handoff)

1. **Live roleplay app integration**: This harness does not yet drive the real QuillForge app. It simulates component boundaries with deterministic scripted content. The structured payload schema is aligned with #1661 but actual `RoleplayKnowledgePacket` / `StructuredSceneBrief` types should be defined in #1661.
2. **Summary/history boundary**: Placeholder only — not yet exercised with real summary/history data.
3. **Provider/timing classification**: Not yet implemented — always returns `uncertain` or a different origin for timing artifacts.
4. **LLM evaluator**: Extension seam exists (base-url/model args) but no LLM-based evaluator is implemented. The harness uses purely structural drift detection.
5. **Lore frontmatter/metadata visibility**: As per accepted Patch decisions, lore metadata visibility should later be gated behind an advanced/debug/editing toggle. The harness docs do not imply raw metadata is always shown to normal users.
6. **Multi-character scenarios**: Only Xavier/Caleb fixture is built. More complex scenarios with 3+ characters and nested forbidden sets are future work.

## Environment Variables

| Variable | Purpose |
|----------|---------|
| `DRIFT_HARNESS_OUTPUT_DIR` | Output directory override |
| `DRIFT_HARNESS_SCENARIO` | Scenario name override |
| `DRIFT_HARNESS_BASE_URL` | OpenAI-compatible base URL for LLM evaluator |
| `DRIFT_HARNESS_MODEL` | Model name for LLM evaluator |
| `DRIFT_HARNESS_API_KEY` | API key for LLM evaluator |
