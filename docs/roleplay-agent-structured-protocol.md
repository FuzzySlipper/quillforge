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

All types are defined in `QuillForge.Core.Models.RoleplayProtocolTypes.cs` and serialized as JSON using `JsonStringEnumConverter` (PascalCase member names).

### Enums

| Enum | Values | Description |
|------|--------|-------------|
| `RoleplayKnowledgeScope` | `CharacterSpecific`, `UserCharacterSpecific`, `RelationshipSpecific`, `SharedWorld`, `GenericEquipment`, `Organization`, `Location`, `SceneRule`, `SessionCanon`, `RecentConversation`, `Unknown` | Domain of the knowledge |
| `ActiveSubjectApplicability` | `Applies`, `DoesNotApply`, `Unknown`, `Ambiguous`, `Conflicts` | How lore applies to the active subject |
| `AllowedUse` | `AssertAsFact`, `BackgroundOnly`, `OffSubjectEvidence`, `RequiresClarification`, `RejectForActiveSubject` | How knowledge may be used in generation |
| `CanonAuthority` | `Canon`, `SessionCanon`, `UserCorrection`, `Rumor`, `Deprecated`, `Unknown` | Canon authority level |
| `SubjectSourceKind` | `CharacterFile`, `WorldFile`, `FactionFile`, `EventFile`, `ItemFile`, `LocationFile`, `Correction`, `SessionCanon`, `Unknown` | Type of knowledge source |

### Migration from Initial Implementation

The initial implementation (#1661 first pass) used simpler enum names: `Character`, `World`, `Meta` for `RoleplayKnowledgeScope`; `ActiveCharacter`, `OffCharacter`, `SharedWorld`, `Unknown` for `ActiveSubjectApplicability`; `Inline`, `Context`, `Excluded`, `Unknown` for `AllowedUse`; `Primary`, `Secondary`, `Background`, `Override`, `Provisional` for `CanonAuthority`.

The hardening pass (#1661 fix) aligned all enum names and classifier output with the accepted Den protocol concepts. **New structured payloads should use the new names.** Any stored/serialized traces using the old names will fail to deserialize into the new enum members; consumers should regenerate traces after this change.

### Classifier Mapping (Den Protocol)

The deterministic classifier maps to Den-spec protocol values as follows:

| Evidence Pattern | Applicability | AllowedUse | Scope |
|---|---|---|---|
| Active character name/file | `Applies` | `AssertAsFact` | `CharacterSpecific` |
| Shared world / generic equipment | `Unknown` | `BackgroundOnly` | `SharedWorld` / `GenericEquipment` |
| Off-character (not excluded) | `DoesNotApply` | `OffSubjectEvidence` | `CharacterSpecific` |
| Off-character (excluded) | `DoesNotApply` | `RejectForActiveSubject` | `CharacterSpecific` |
| Truly ambiguous | `Ambiguous` | `RequiresClarification` | `Unknown` |

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
  "applicability": "Applies",
  "allowed_use": "AssertAsFact",
  "source_refs": [
    {
      "source_path": "characters/xavier.md",
      "source_kind": "CharacterFile",
      "authority": "Canon"
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
  "scope": "CharacterSpecific",
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
  "knowledge_scope": "CharacterSpecific",
  "allowed_use": "AssertAsFact",
  "reason": "Direct character lore"
}
```

## Applicability Classification

The deterministic classifier (`QuillForge.Core.Services.RoleplayApplicabilityClassifier`) uses structural heuristics mapped to Den-spec protocol values:

1. **Source file path** — if the file name contains the active character name, classify as `Applies`. If it contains an off-character name, classify as `DoesNotApply`. World/shared/faction files classify as `Unknown`.
2. **Name mention count** — 2+ mentions of the active subject's name → `Applies`. 1+ mention of an off-character's name → `DoesNotApply`.
3. **Single active subject mention** — at least one mention → `Applies` (checked before shared-world markers to avoid false positive from words like "standard" or "common").
4. **Shared-world markers** — keywords like `shared`, `common`, `standard`, `generic`, `typical` → `Unknown`.
5. **Fallback** — `Ambiguous`.

The classifier is intentionally conservative: it may return `Unknown` for shared world content and `Ambiguous` for cases that require semantic analysis. Those should be handled by the Librarian's higher-level synthesis.

Allowed-use follows from applicability:
- `Applies` → `AssertAsFact`
- `Unknown` (shared world) → `BackgroundOnly`
- `DoesNotApply` → `OffSubjectEvidence`; `RejectForActiveSubject` if subject is in the excluded set
- `Ambiguous` → `RequiresClarification`

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
- The directives section tells the prose writer which subjects have `AssertAsFact`, `BackgroundOnly`, or `RejectForActiveSubject` knowledge, and includes the core protocol rule about not grafting background facts onto active characters.

### 4. ProseWriterAgent prompt

- `BuildSystemPrompt` includes the protocol rules whenever `## Roleplay Knowledge Directives` is present in the story context.
- Rules: `AssertAsFact` facts → may use as direct character facts; `BackgroundOnly` → general scene description only; `RejectForActiveSubject` → must not appear.
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
- **Enum names changed in the hardening pass** (see Migration section above). Any code or serialized data referencing the old enum names must be updated. The test `DenSpecEnumValues_RoundTrip_Json` validates that all new enum values round-trip correctly.

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
