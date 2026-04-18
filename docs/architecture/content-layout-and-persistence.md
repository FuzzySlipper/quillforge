# Content Layout And Persistence

This note collects the user-content layout and persisted-document rules that agents need when changing storage, config, or runtime-state behavior.

## Content Layout

QuillForge preserves a user-owned content-root layout for compatibility and portability.

The current high-level layout is:

```text
<content-root>/
├── config.yaml
├── lore/
├── assistant/
├── conductor/
├── librarian-prompts/
├── narrative-rules/
├── profiles/
├── plots/
├── writing-styles/
├── story/
├── writing/
├── chats/
├── forge/
├── forge-prompts/
├── council/
├── layouts/
├── character-cards/
├── backgrounds/
├── generated-images/
├── generated-audio/
├── artifacts/
├── research/
└── data/
    ├── providers.json
    ├── sessions/
    ├── session-state/
    └── llm-debug/
```

Current content-root expectations:
- source/dev runs normally use repo-local `user/`
- portable published runs normally use a sibling `user/` next to the binary
- desktop-mode published runs default to `Documents/QuillForge`
- desktop-mode migration should import an existing sibling published `user/` tree into `Documents/QuillForge` when the desktop workspace is still empty

The single source of truth for content directory names in code is:
- `src/QuillForge.Core/ContentPaths.cs`

Reference material:
- `README.md` for the current user-facing content tree
- `src/QuillForge.Core/ContentPaths.cs` for the canonical code constants

Path rules:
- use `ContentPaths.*` constants instead of bare string literals
- use those constants for both `Path.Combine(...)` and content-service relative paths
- preserve the compatibility layout unless there is an explicit migration plan
- treat `data/` under the content root as app-owned persisted state rather than normal content-editing surface
- `conductor/` is retained as legacy migration/reference material, not the routing authority for current app behavior

## Persistence Boundary

Persisted config and state must go through the owning store or service boundary. Endpoints should not hand-roll file writes for durable documents.

Primary references:
- `src/Den.Persistence/PersistedDocumentBase.cs`
- `src/Den.Persistence/JsonPersistedDocumentStore.cs`
- `src/Den.Persistence/YamlPersistedDocumentStore.cs`
- `src/QuillForge.Storage/Configuration/AppConfigStore.cs`
- `docs/architecture/profile-session-conversation-ownership.md`

Core rules:
- app-wide config writes go through `IAppConfigStore`
- reusable profile documents go through the profile store/service boundary
- session runtime goes through `ISessionStateStore` and session-owned services
- durable `ProfileConfig` and `SessionState` changes should be coordinated through their owning services, not serialized directly from endpoints
- all durable file mutation uses atomic write behavior

## When To Use `Den.Persistence`

Use the persisted-document infrastructure when the data is a single document at a known path and the store should own load/default/normalize/validate/save behavior.

Current example:
- `AppConfig` is stored through `AppConfigStore`, which wraps a `YamlPersistedDocumentStore<AppConfig>`

Cases that are intentionally different:
- profile configs are a keyed collection, so they stay behind the profile store/service layer
- session state is a keyed collection, so it stays behind `ISessionStateStore`
- provider config has an encryption boundary and does not map directly to a simple document-store shape

## Adding Fields Or New Documents

For additive fields on an existing persisted document:
1. add the field to the model
2. normalize or backfill it in the owning persisted-document definition if needed
3. keep validation in the owning document/store boundary

For a new persisted document:
1. define the model
2. create a `PersistedDocumentBase<T>` definition with `RelativePath` and `CreateDefault`
3. override `Normalize(...)` and `ThrowIfInvalid(...)` only when needed
4. create a store around `JsonPersistedDocumentStore<T>` or `YamlPersistedDocumentStore<T>`
5. register the store explicitly in DI

## Schema Versioning

Additive fields do not need explicit schema versions if deserialization plus normalization is enough to land on the current shape.

Breaking shape changes do need explicit versioning.

Primary reference:
- `src/Den.Persistence/IVersionedPersistedDocument.cs`

Rules:
- version per document, not per module
- keep migrations sequential and small
- use raw-object migration for renamed or removed fields before typed deserialization
- keep `Normalize(...)` for non-breaking cleanup, defaults, and clamping
- add at least one round-trip test for legacy shapes when introducing a breaking schema change

## Product-Neutral Boundary

`Den.Persistence` must remain product-neutral.

Rules:
- do not move QuillForge-specific domain types into `Den.Persistence`
- keep dependency direction as `QuillForge.Storage -> Den.Persistence`
- do not let Web or Core bypass the storage boundary to talk directly to raw persisted-document infrastructure for app-owned writes
