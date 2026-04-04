# ProfileConfig Lifecycle Note

This note defines the currently supported lifecycle rules for durable
`ProfileConfig` files.

## Supported Operations

- create or update: `PUT /api/profile-configs/{profileId}`
- clone: `POST /api/profile-configs/{profileId}/clone`
- select as the default profile for future sessions: `POST /api/profile-configs/{profileId}/select`
- delete: `DELETE /api/profile-configs/{profileId}`

## Deferred Operation

- rename is intentionally not supported yet

Why:

- profile IDs are durable references stored in persisted session runtime state
- a safe rename needs explicit migration of those references or a durable alias
  story
- until that exists, rename should be treated as `clone + select + delete`
  only when the old profile is not the default profile and is not in use by any
  persisted session

## Safety Rules

- the default profile cannot be deleted
- a profile referenced by any persisted session cannot be deleted
- selecting a profile only changes the default profile for future sessions plus
  the remaining compatibility snapshot in `AppConfig`
- editing an existing profile is allowed even if active sessions reference it;
  sparse session bindings will continue to resolve through the durable profile

## In-Use Session Semantics

Deletion is rejected for in-use profiles.

Why:

- sessions now bind sparsely by `ProfileId`
- silently deleting a referenced profile would force affected sessions onto the
  fallback compatibility path instead of preserving an intentional durable
  configuration
- rejecting deletion keeps the lifecycle rule explicit and prevents accidental
  behavior changes in existing sessions

## Compatibility Note

`SessionRuntimeService` still has a fallback to the default profile if a
referenced durable profile is missing on disk. That remains a compatibility
guard for out-of-band file edits or legacy content, not a supported lifecycle
path for normal API operations.
