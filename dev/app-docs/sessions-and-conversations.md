---
name: Sessions & Conversations
summary: Session continuity, conversation branching, fork/regenerate, and persistence
---

# Sessions & Conversations

## Sessions

A session represents one continuous interaction with QuillForge. Each session has:
- A unique ID
- An active profile (conductor, lore set, writing style, narrative rules)
- An active mode
- Runtime state (story state, pending content, etc.)
- A conversation tree

Sessions persist across browser refreshes and app restarts. Your conversation history, mode, and profile selection are preserved.

## Conversation Tree

Conversations in QuillForge are trees, not flat lists. This enables:

### Branching / Forking
- Fork from any message to explore an alternative direction
- The original branch is preserved — you can switch between branches
- Each branch maintains its own thread of messages

### Regeneration
- Regenerate the last assistant response to get a different answer
- This creates a new branch from the parent message
- Previous responses are preserved on their original branches

### Message Operations
- **Delete** — Remove a message and its orphaned descendants
- **Fork** — Branch from a message to explore alternatives
- **Regenerate** — Get a new response to the same prompt

## Thread Navigation

The "active thread" is the path from the root to the current leaf message. When you fork or regenerate, the active thread switches to the new branch. You can navigate back to previous branches through the conversation tree.

## Session Lifecycle

- **New Session** — `/new` or the new session button creates a fresh session
- **Session Continuity** — Messages persist; reopening the app resumes where you left off
- **Profile Switching** — Changing profiles within a session affects future messages but doesn't retroactively change past ones
- **Mode Switching** — Modes can be changed freely within a session

## Data Storage

- Conversation trees are stored as JSON in `user/data/sessions/`
- Session runtime state is stored in `user/data/session-state/`
- All messages use stable GUIDs for identification, never array indices
