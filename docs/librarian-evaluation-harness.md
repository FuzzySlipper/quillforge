# Librarian Evaluation Harness

This directory contains the **messy-corpus Librarian evaluation** harness for QuillForge. It evaluates the LibrarianAgent's retrieval and answer-synthesis quality against a messy lore corpus with collisions, ambiguous references, and off-character leakage.

## Overview

The harness is a console application (`QuillForge.LibrarianEval`) that:

1. Loads a lore corpus (synthetic or real) via `FileSystemLoreStore`
2. Runs a set of evaluation questions through `LibrarianAgent.QueryAsync()`
3. Scores the results **structurally** (no LLM judge) on:
   - **Correct source included** — were expected source files referenced?
   - **Off-character source excluded** — were forbidden sources avoided?
   - **No forbidden graft** — were forbidden facts not present in passages?
   - **Asked for clarification** — did the Librarian acknowledge ambiguity?
   - **Shared facts accessible** — are world-level shared facts available?
   - **Expected passages present** — did expected substrings appear?
4. Distinguishes **retrieval failure** (parse failure, missing expected source, or off-scope source included) from **synthesis failure** (sources acceptable but the answer text failed structural checks)
5. Writes artifacts to an output directory:
   - `questions.jsonl` — the evaluation questions
   - `retrieval-trace.jsonl` — raw Librarian responses with metadata
   - `answers.jsonl` — parsed answers with provenance
   - `evaluation.json` — structured evaluation results
   - `summary.md` — human-readable summary with recommendations

## Running

### Quick CI run (synthetic corpus, no live model)

```bash
dotnet run --project src/QuillForge.LibrarianEval \
  -- --corpus-path tests/QuillForge.LibrarianEval.Tests/Fixtures/synthetic-lore \
     --lore-set default \
     --output-dir /tmp/qf-eval-run
```

This uses the built-in deterministic fake completion service and runs 6 synthetic questions against a noisy corpus with intentional collisions (Link vs Dark Link, Silver Flame vs Black Flame, Capital vs Shadow Capital). The fake answers are only for exercising the harness and structural scoring without a live provider.

### Testing

```bash
dotnet test tests/QuillForge.LibrarianEval.Tests
```

16 tests covering:
- Scorer: correct/forbidden sources, forbidden facts, clarification, shared facts, passages, path normalization, edge cases
- Runner: fake service integration, corpus loading, report writer

### Live evaluation with a real model

```bash
dotnet run --project src/QuillForge.LibrarianEval \
  -- --corpus-path /path/to/lore \
     --lore-set default \
     --output-dir /tmp/qf-eval-live \
     --base-url http://localhost:1234/v1 \
     --model qwen3-35b \
     --questions-file /path/to/questions.json \
     --limit 5
```

Environment variables may be used instead of CLI args:
- `LIBRARIAN_EVAL_CORPUS_PATH`
- `LIBRARIAN_EVAL_OUTPUT_DIR`
- `LIBRARIAN_EVAL_BASE_URL`
- `LIBRARIAN_EVAL_MODEL`
- `LIBRARIAN_EVAL_LORE_SET`
- `LIBRARIAN_EVAL_API_KEY`
- `LIBRARIAN_EVAL_QUESTIONS_FILE`

## Privacy Guardrails

- The private corpus (`/home/stash/lore/Deepspace-Linkon`) is accessed at runtime via `--corpus-path`; it is never committed.
- Only derived metrics and minimal provenance paths (file names, not raw content) appear in evaluation artifacts.
- To run against the private corpus:
  ```bash
  dotnet run --project src/QuillForge.LibrarianEval \
    -- --corpus-path /home/stash/lore/Deepspace-Linkon \
       --lore-set . \
       --output-dir /tmp/qf-eval-deepspace \
       --base-url http://192.168.1.23:13305/v1 \
       --model Qwen3.6-35B-A3B-GGUF \
       --limit 5
  ```

## Question Format

Questions are JSON files with the following structure:

```json
{
  "id": "unique-id",
  "query": "What is the primary weapon of the hero Link?",
  "expectedSources": ["characters/link.md"],
  "forbiddenSources": ["characters/link-dark.md"],
  "forbiddenFacts": ["Dark Link", "cursed blade"],
  "requiresClarification": false,
  "sharedFactSources": ["world/weapons.md"],
  "expectedPassageSubstrings": ["hero's blade"],
  "notes": "Basic retrieval with collision check"
}
```

## Recommendation Output

The `summary.md` report includes explicit recommendations on whether structured lore metadata/storage is needed. The heuristic:
- If **synthesis failures > 0** or **retrieval failures > 0**, metadata is recommended.
- The smallest durable schema: YAML front-matter in each lore markdown file with `type`, `canonical_names`, `canon`, and `excludes` fields.
- App-editing path: `POST /api/lore/{set}/{file}/metadata` endpoint + UI panel in the Librarian prompt editor.
