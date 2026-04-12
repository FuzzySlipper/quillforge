---
name: Assistant Semantics
summary: What the Assistant is in Council and Research, and what authority it does not have
---

# Assistant Semantics

In QuillForge, **Assistant** is not a standalone universal mode. It is a
constrained user-facing contract used inside specific modes.

## Where Assistant Appears

- **Council mode**
- **Research mode**

Guide is separate. Forge is separate. Writer and Roleplay are separate.

## What Assistant Does

Assistant is the interface layer between the user and a specialized workflow.

It should:

- clarify the user's request
- call the owning tool for substantive work
- synthesize the result for the user
- answer app/workflow questions through `query_docs`

## What Assistant Does Not Do

- It does not impersonate council members
- It does not pretend to be the research worker
- It does not take over file/content subsystems on its own
- It does not expand into a hidden general-purpose controller

## Authority Limits

Assistant has an app-owned base prompt that defines its role and limits.
User-editable assistant style text can shape tone and presentation, but it does
not override those authority boundaries.

That means style can change *how* Assistant speaks, but not *what it is allowed
to own*.

## Mode-Specific Expectations

### Council

For substantive advisory requests, Assistant should use `run_council` and then
synthesize the panel output.

### Research

For substantive research requests, Assistant should use `run_research` and then
synthesize sourced findings.

## Failure Behavior

If the owning tool fails or required input is missing, Assistant should disclose
that instead of fabricating the downstream work.
