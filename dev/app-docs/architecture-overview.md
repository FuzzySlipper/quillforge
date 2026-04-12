---
name: Architecture Overview
summary: High-level map of Guide, Assistant, Narrative Director, Prose Writer, and Forge ownership
---

# Architecture Overview

QuillForge is not one big free-form chat prompt. It is a mode-based system with
clear ownership boundaries.

## The Short Map

- **Guide** is the front desk
- **Writer** and **Roleplay** are canon-sensitive prose workflows
- **Narrative Director** grounds Writer and Roleplay before prose is rendered
- **Prose Writer** renders final prose from a grounded brief rather than making first-order scene decisions
- **Council** and **Research** use a constrained **Assistant** as the user-facing interlocutor
- **Forge** is command- and pipeline-owned rather than a free-form chat workflow

## Who Owns What

### Guide

Guide is the startup and fallback mode. It explains the app, helps the user
pick a workflow, and surfaces obvious setup problems. It should not quietly
become a second creative collaborator.

### Writer and Roleplay

These are the canon-sensitive interactive prose paths.

They should flow like this:

1. Gather the relevant lore, story state, character context, writing style, and narrative rules
2. Ground the next response through Narrative Director
3. Hand a prose brief to Prose Writer
4. Render the visible prose

Writer and Roleplay should not skip straight to first-draft prose from the top
level.

### Council and Research

These modes use Assistant as the user-facing surface.

Assistant is not a broad do-anything controller. It is a constrained
interlocutor that:

- frames the user request
- calls the right specialized tool
- synthesizes the result
- explains workflow boundaries when asked

### Forge

Forge is not another scene-writing mode. It is an explicit command and pipeline
domain. Planning, design, writing, review, and assembly belong to Forge
commands and pipeline stages rather than free-form chat turns.

## Routing Model

Top-level routing is app-owned by the active mode plus explicit commands. It is
not driven by user-editable conductor prompt text.

Legacy conductor files may still exist for migration/reference, but they are
not the live authority for routing behavior.

## The Main Rule

If a workflow has a clear owner, do not let another surface quietly take over
that work.
