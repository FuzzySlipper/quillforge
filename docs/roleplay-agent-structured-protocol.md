# Roleplay Agent Structured Communication Protocol

This document defines the typed structured communication protocol for roleplay agent handoffs in QuillForge. It is the formal specification for task #1661 and replaces ad-hoc string-based lore pass-through with typed, validated payloads that carry active-subject context, applicability classifications, and allowed-use directives at every agent boundary.

## Motivation

In multi-agent roleplay (Narrative Director → ProseWriter → visible response), knowledge flows through several boundaries:

```
User Turn → query_lore/query_context → Narrative Director → ProseWriter → Visible Response
```

Without structured payloads at each boundary, the ProseWriter receives undifferentiated lore text and must decide what to inline vs. use as context vs. exclude — a task LLMs are unreliable at, especially with shared/body-tech lore that mentions off-character details.

The structured protocol encodes the classification decision at each boundary so downstream agents can reliably obey allowed-use constraints.

## Core Data Model

All types are defined in `QuillForge.Core.Models.RoleplayProtocolTypes.cs` and serialized as JSON with snake_case naming.

### Enums

| Enum | Values | Description |
|------|--------|-------------|
| `RoleplayKnowledgeScope` | `character`, `world`, `meta` | Domain of the knowledge |
| `ActiveSubjectApplicability` | `active_character`, `off_character`, `shared_world`, `unknown` | How lore applies to the active subject |
| `AllowedUse` | `inline`, `context`, `excluded`, `unknown` | How knowledge may be used in generation |
| `CanonAuthority` | `primary`, `secondary`, `background`, `override`, `provisional` | Canon authority level |
| `SubjectSourceKind` | `character_file`, `world_file`, `faction_file`, `event_file`, `item_file`, `location_file`, `correction`, `session_canon`, `unknown` | Type of knowledge source |

### Main Payloads

**RoleplayKnowledgeRequest** — what the query_lore/query_context handler sends to the Librarian or underlying service.

```json
{
  "query": "What augmentations does Xavier have?",
  "active_subject": "Xavier",
  "excluded_subjects": ["Caleb"],
  "lore_set": "deepspace",
  "include_shared_context": true
}
```

**RoleplayEvidenceItem** — a single classified passage with provenance:

```json
{
  "passage": "Xavier has a standard neural interface.",
  "applicability": "active_character",
  "allowed_use": "inline",
  "source_refs": [
    {
      "source_path": "characters/xavier.md",
      "source_kind": "character_file",
      "authority": "primary"
    }
  ],
  "subject_ref": null,
  "ambiguity": null
}
```

**RoleplayKnowledgePacket** — the full structured response from query_lore/query_context:

```json
{
  "query": "What augmentations does Xavier have?",
  "active_subject": "Xavier",
  "scope": "character",
  "evidence": [ ... ],
  "source_files": ["characters/xavier.md", "world/body-tech.md"],
  "confidence": "high",
  "source_component": "query_lore"
}
```

**StructuredSceneBrief** — passed from Narrative Director to ProseWriter:

```json
{
  "scene_description": "Xavier enters the command center.",
  "active_subject": "Xavier",
  "excluded_subjects": ["Caleb"],
  "knowledge_packets": [ ... ],
  "tone_notes": "Tense, urgent",
  "source_component": "narrative_director"
}
```

**RoleplayDirective** — encodes a per-subject or per-scope allowed-use rule:

```json
{
  "for_subject": "Xavier",
  "knowledge_scope": "character",
  "allowed_use": "inline",
  "reason": "Direct character lore"
}
```

## Applicability Classification

The deterministic classifier (`QuillForge.Core.Services.RoleplayApplicabilityClassifier`) uses structural heuristics:

1. **Source file path** — if the file name contains the active character name, classify as `active_character`. If it contains an off-character name, classify as `off_character`. World/shared/faction files classify as `shared_world`.
2. **Name mention count** — 2+ mentions of the active subject's name → `active_character`. 1+ mention of an off-character's name → `off_character`.
3. **Single active subject mention** — at least one mention → `active_character` (checked before shared-world markers to avoid false positive from words like "standard" or "common").
4. **Shared-world markers** — keywords like `shared`, `common`, `standard`, `generic`, `typical` → `shared_world`.
5. **Fallback** — `unknown`.

The classifier is intentionally conservative: it may return `unknown` for ambiguous cases that require semantic analysis. Those should be handled by the Librarian's higher-level synthesis.

Allowed-use follows from applicability:
- `active_character` → `inline`
- `shared_world` → `context`
- `off_character` → `excluded` if subject is in the excluded set, else `context`
- `unknown` → `unknown`

## Boundary Integration

### 1. query_lore (QueryLoreHandler)

- Calls `LibrarianAgent` as before for the raw LoreBundle.
- After receiving the result, enriches the bundle with a `RoleplayKnowledgePacket` if an active subject can be inferred from `AgentContext.SessionContext.Character`.
- Uses `RoleplayApplicabilityClassifier.ClassifyEvidenceItem` to classify each passage.
- The enriched bundle (with `StructuredPacket`) is serialized as the tool result.

### 2. query_context (QueryContextHandler)

- Detects the active subject from context (same heuristic).
- When `include_lore_documents` is true, enriches each lore document match with `applicability` and `allowed_use` fields.
- Builds an optional `QueryContextResult.StructuredPacket` containing a `RoleplayKnowledgePacket` with classified evidence.
- Non-lore sources (character cards, session canon, etc.) get a source kind mapping but no applicability classification (they are directly about the session context).

### 3. Narrative Director → ProseWriter (write_prose)

- `WriteProseArgs` now accepts an optional `StructuredSceneBrief` alongside the legacy `scene_description` and `tone_notes`.
- `WriteProseHandler.BuildStoryContext` adds a `## Roleplay Knowledge Directives` section when the brief provides knowledge packets or directives.
- The directives section tells the prose writer which subjects are inline, context-only, or excluded, and includes the core protocol rule about not grafting background facts onto active characters.

### 4. ProseWriterAgent prompt

- `BuildSystemPrompt` includes the protocol rules whenever `## Roleplay Knowledge Directives` is present in the story context.
- Rules: inline facts → may use as direct character facts; background/context → general scene description only; excluded → must not appear.
- Shared/background knowledge must be presented as common/shared, not unique to the active character.

### 5. NarrativeDirectorAgent prompt

- `BuildSystemPrompt` includes a `## Roleplay Knowledge Protocol` section when a character is active.
- Warns against durable negative exclusion blocks ("NOT [name]") as primary solution.
- Instructs the director to include active-subject/applicability/allowed-use markers in the scene brief.

## Backward Compatibility

- `LoreBundle` retains its original `RelevantPassages`, `SourceFiles`, and `Confidence` fields. `StructuredPacket` is an optional additive field.
- `QueryContextResult` retains all original fields. `ActiveSubject` and `StructuredPacket` are additive.
- `WriteProseArgs` retains `SceneDescription` and `ToneNotes`. `SceneBrief` is optional.
- When no active subject is detected, no structured enrichment occurs — all agents behave as before.

## Drift Origin Classification

The drift harness (`QuillForge.RoleplayDriftHarness`) uses the structured payloads to identify drift origins:

| Origin | Detect at |
|--------|-----------|
| `retrieval` | query_lore boundary — forbidden fact in lore results |
| `director_synthesis` | Narrative Director boundary — director synthesized forbidden fact |
| `prose_misuse` | ProseWriter boundary — prose writer used context incorrectly |
| `visible_response` | Visible Response boundary — fact appeared only in final output |
| `summary_history` | Summary/history boundary (placeholder) |
| `provider_timing` | Provider/timing artifact (not yet implemented) |
| `uncertain` | Could not determine |

## Design Decisions

### (Accepted) Keep ProseWriter query_lore/query_context access

For the first public release, ProseWriter retains access to `query_lore` and `query_context` tools. These are constrained by the active-subject/applicability/allowed-use protocol described above. Removing ProseWriter lore/context access is deferred.

### (Accepted) Gate lore frontmatter/metadata

Lore frontmatter/metadata visibility is gated behind an advanced/debug/editing toggle. The protocol classes parse and use metadata internally (via `RoleplaySourceRef.Authority` and `SubjectSourceKind`), but these are not exposed in the normal lore editing UI. UI wiring is scoped to future work.

### (Accepted) No durable negative exclusion blocks

The protocol explicitly warns against using durable negative exclusion blocks (e.g., "NOT Caleb") as the primary solution. User-authored corrections may remain temporary overrides, but the structured protocol is the durable mechanism for preventing drift.

## How #1641 Should Use This Protocol

Task #1641 (the "NOT Caleb" prompt/hack) should be addressed by:
1. Removing any hard-coded negative exclusion text from prompts.
2. Ensuring the active subject is always set in `AgentContext.SessionContext.Character` during roleplay sessions.
3. Letting the query_lore/query_context structured classification handle off-character exclusions at the retrieval boundary.
4. Using the Narrative Director protocol section and ProseWriter protocol rules (in prompts) as the behavioral guard, not narrow "NOT Caleb" rules.
5. Running the drift harness regression tests to verify Caleb-only prosthetic/Toring details do not enter Xavier prose under any scenario.

## Future Work

- Implement summary/history boundary structured payload
- Add provider/timing drift origin classification
- Expose a debug/editing toggle for lore frontmatter/metadata visibility
- Extend to 3+ character scenarios with nested forbidden sets
- Add live LLM evaluator extension point in the drift harness

## References

- `src/QuillForge.Core/Models/RoleplayProtocolTypes.cs` — typed model definitions
- `src/QuillForge.Core/Services/RoleplayApplicabilityClassifier.cs` — deterministic classifier
- `src/QuillForge.Core/Agents/Tools/QueryLoreHandler.cs` — structured enrichment
- `src/QuillForge.Core/Agents/Tools/QueryContextHandler.cs` — structured enrichment
- `src/QuillForge.Core/Agents/Tools/WriteProseHandler.cs` — scene brief + directives
- `src/QuillForge.Core/Agents/ProseWriterAgent.cs` — protocol-aware prompt
- `src/QuillForge.Core/Agents/NarrativeDirectorAgent.cs` — protocol-aware prompt
- `src/QuillForge.RoleplayDriftHarness/` — drift trace harness
- `tests/QuillForge.Core.Tests/RoleplayProtocolTypesTests.cs` — JSON round-trip tests
- `tests/QuillForge.Core.Tests/RoleplayApplicabilityClassifierTests.cs` — classification tests
