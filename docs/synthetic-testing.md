# Synthetic Testing

When the user asks to perform a synthetic user build test, manual build test, or similar, do not stop at unit/integration tests. Treat it as a live-build exercise using the Development-only debug bridge plus normal UI/manual verification.

## Goal

Simulate a careful human tester using the running app end-to-end, with emphasis on:
- frontend/backend contract mismatches
- stale runtime state
- SSE and streaming failures
- tool-loop failures
- issues that only appear in a live build

## Required Approach

1. Build and run the app in Development.
2. Use the debug bridge endpoints for deterministic backend/manual probing:
   - `POST /api/debug/bridge/session/reset`
   - `POST /api/debug/bridge/mode`
   - `POST /api/debug/bridge/chat`
   - `GET /api/debug/bridge/session/{id}`
   - `GET /api/debug/bridge/state`
3. Also exercise the real web UI and normal endpoints whenever frontend contracts, browser behavior, or SSE handling could be involved.
4. Prefer short realistic prompts and commands over shallow no-op checks.
5. For every failure, capture:
   - exact action taken
   - endpoint or UI surface used
   - expected behavior
   - actual behavior
   - the smallest code-path evidence you can find afterward
6. If the user asked for bug discovery rather than an immediate fix, add or update concrete Den tasks instead of leaving only prose notes.

## Minimum Coverage

Unless the user explicitly narrows scope, cover:
- app boot and `/api/status`
- normal chat streaming via `/api/chat/stream`
- a lore question that should invoke `query_lore`
- session continuity across multiple turns
- session reload showing persisted assistant turns
- profile switching affecting lore, narrative rules, writing style, librarian prompt, and roleplay defaults in runtime behavior
- guide mode
- writer mode, including pending-content review behavior when applicable
- roleplay mode, including character selection if applicable
- council mode
- artifact generation if available
- Forge smoke paths if available
- diagnostics visibility for tool calls/warnings/empty responses
- one message delete/fork/regenerate flow when exposed
- one content browsing/editing flow when exposed

## Desktop Addendum

When the work touches the Tauri wrapper, release artifacts, or desktop-specific startup behavior, also cover:
- desktop shell launch without a separate browser window
- workspace creation under `Documents/QuillForge`
- `Open Workspace` and `Restart Backend` from the shell UI
- default local-only binding behavior
- optional LAN/mobile access toggle when that path is part of scope
- any platform-specific install/update docs that changed as part of the task

For pre-release manual install/update verification, pair this guide with `docs/desktop-release-validation.md`.

## Required Prompt Set

Include prompts that force real behavior:
- a lore question that should obviously trigger the librarian
- a writer-mode scene request that should use lore and writing-style context
- a roleplay prompt that depends on the selected character/profile
- a council-style question that should produce multi-member output
- a prompt that mutates session state plus a follow-up that verifies persistence

## Multi-Agent Game Harness

For deterministic social-game regression and benchmarking traces, use the scripted game harness documented in `docs/architecture/game-harness-trace-artifacts.md`:

```bash
dotnet test tests/QuillForge.ProviderHarness.Tests/QuillForge.ProviderHarness.Tests.csproj \
  -p:AllowMissingPrunePackageData=true \
  --filter HarnessGameScenarioTests
```

These runs are prompt-level deterministic with fake completion sources. Future live-provider exploratory game runs should be interpreted from their trace artifacts rather than asserted as golden semantic outcomes.

For manual/live social-games validation, pair this harness command with the Games-mode smoke path and troubleshooting guide in `docs/social-games-setup-testing-extension.md`.

### Games Diagnostic Log

The Games workspace includes a **Diagnostic Log** button next to **Refresh Table**. Use it when a game starts and then stalls, an agent appears to take no action, or a message/action endpoint returns a 400/409.

The log is a local host-level debug stream, not a player-visible projection. It can include private game facts, prompt/response previews, prompt cursors, memory summaries, provider/model names, token usage, rejection/no-action reason codes, rules-engine events, communication events, endpoint/service operations, and inferred session-state persistence outcomes. It must not include provider API keys or encrypted secrets.

Recommended bug-report capture:
1. Reproduce the game issue in Games mode.
2. Click **Diagnostic Log**.
3. Use the in-panel **Category** and **Page size** controls to focus the stream when the game has run for many turns. Start with **Rejection**, **Error**, **LlmProvider**, or **AgentPrompt** and **Latest 50**.
4. Click **Refresh Log** immediately after the failed action.
5. Use **Load Older** if the selected page has more matching entries.
6. Use **Copy JSON** for paste-friendly reports or **Export JSON** for an attachment.
7. Include the failed UI action or endpoint call, the visible status/stage, and the first rejection/error event in the exported stream.

Focused endpoint examples:

```bash
# Latest 50 rejection/error events for the active game scope shown in the UI.
curl "http://localhost:5000/api/sessions/$SESSION_ID/game/diagnostics?gameInstanceId=$GAME_INSTANCE_ID&limit=50&categories=Rejection,Error"

# Page older focused provider/prompt events. Use nextBeforeSequence from the prior response.
curl "http://localhost:5000/api/sessions/$SESSION_ID/game/diagnostics?gameInstanceId=$GAME_INSTANCE_ID&limit=50&beforeSequence=$NEXT_BEFORE_SEQUENCE&categories=LlmProvider,AgentPrompt"
```

Omitting `limit`, `beforeSequence`, and `category`/`categories` preserves the default full active-game diagnostic log for local debugging.

## Expected Deliverable

Return:
1. findings first, ordered by severity
2. clear reproduction steps
3. file or endpoint evidence
4. Den task IDs created or updated, if bug logging was requested
5. a short coverage note listing what was exercised and what was not

## Reusable Internal Prompt

```text
Perform a synthetic user build test of QuillForge against a live Development build.

Do not stop at dotnet test. Start the app, use the debug bridge endpoints for deterministic probing, and also use the real UI/endpoints for any feature where frontend/backend contract mismatches, SSE parsing, session persistence, or browser behavior could hide bugs.

Cover at minimum:
- status/bootstrap
- normal chat streaming
- lore lookup through the orchestrator
- session continuity and reload
- profile switching (lore/narrative rules/writing style/librarian prompt/roleplay defaults)
- guide mode
- writer mode
- roleplay mode
- council mode
- artifact generation if available
- forge smoke paths if available
- diagnostics visibility
- one message delete/fork/regenerate flow
- one content browsing/editing flow

Use realistic prompts that force tool use and stateful behavior, not just "say hello".
For every failure, capture the exact action, expected vs actual behavior, and then trace it back to the smallest relevant code path.
If the request is bug-hunting rather than immediate implementation, add/update Den tasks with concrete acceptance tests.
Return findings first, then coverage notes, then any task IDs created.
```
