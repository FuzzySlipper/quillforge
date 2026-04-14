---
name: Profile, Session & Conversation Ownership
summary: What AppConfig, ProfileConfig, SessionState, and ConversationTree each own
---

# Profile, Session & Conversation Ownership

QuillForge keeps app-wide defaults, reusable profile choices, live session
runtime, and persisted chat history in separate places on purpose. The names
matter because they answer different questions.

## The Short Version

- `AppConfig` is for app-wide defaults and true globals
- `ProfileConfig` is a reusable bundle that many sessions can share
- `SessionState` is the live runtime for one session
- `ConversationTree` is the branching message history artifact

## AppConfig

`AppConfig` is the durable application-level config file.

It owns things that should affect the app broadly, such as:

- provider and model defaults
- app-wide UI defaults
- the default profile for new sessions

It does not own:

- the active profile for one running session
- writer pending-review content
- the conversation history

## ProfileConfig

`ProfileConfig` is a reusable durable bundle of author choices.

It is the right home for settings that should be reused across many sessions,
such as:

- lore set
- writing style
- narrative rules
- librarian prompt
- optional roleplay character defaults

One profile can seed many sessions. A profile is not a transcript and it is not
the live mutable runtime for a single chat.

## SessionState

`SessionState` is the authoritative live runtime for one interactive run.

It owns things that answer "what is this session doing right now?", such as:

- which profile the session is using
- the active mode
- current project or file context
- writer pending-review state
- other session-scoped runtime details

If a change should affect only one running conversation, it belongs in
`SessionState`, not in `AppConfig`.

## ConversationTree

`ConversationTree` is the persisted branching message artifact.

It owns the chat history itself:

- messages identified by stable GUIDs
- parent/child relationships for forks and regenerate
- the active thread through the tree

It does not replace `SessionState`. The conversation tree stores the message
graph, while `SessionState` stores the live runtime that sits beside it.

## How They Fit Together

The usual flow is:

1. `AppConfig` supplies app defaults, including the default profile for new sessions.
2. `ProfileConfig` supplies reusable author-facing choices.
3. `SessionState` decides which profile and runtime context a specific session is using now.
4. `ConversationTree` preserves the branching transcript for that same session.

Changing the active profile in one session should update that session's runtime,
not silently rewrite the app-wide defaults for every future session.

## See Also

- `Profiles`
- `Sessions & Conversations`
- `Architecture Overview`
