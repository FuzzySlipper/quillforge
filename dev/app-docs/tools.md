---
name: Tools Reference
summary: Every tool available to the orchestrator, what it does, and when it fires
---

# Tools Reference

Tools are capabilities the orchestrator can invoke during a conversation. The orchestrator decides which tools to use based on your message and the current mode.

## Lore & World

| Tool | Description | When It Fires |
|------|-------------|---------------|
| `query_lore` | Queries the Librarian agent for world-building details | When you ask about characters, locations, events, or lore |
| `query_docs` | Looks up system documentation about QuillForge itself | When you ask about modes, tools, how the system works |

## Writing

| Tool | Description | When It Fires |
|------|-------------|---------------|
| `write_prose` | Generates prose using the active writing style and lore context | When you request creative writing, scenes, or chapters |
| `direct_scene` | Directs a scene with specific parameters and constraints | When you need precise control over scene generation |

## File Management

| Tool | Description | When It Fires |
|------|-------------|---------------|
| `read_file` | Reads a file from the content directory | When you ask to see file contents |
| `write_file` | Writes content to a file (atomic write) | When saving content (often after approval in Writer mode) |
| `list_files` | Lists files in a content directory | When you ask what files exist |
| `search_files` | Searches file contents for matching text | When you search for specific content across files |

## Story & State

| Tool | Description | When It Fires |
|------|-------------|---------------|
| `get_story_state` | Retrieves the current story/plot state | When checking story progress or continuity |
| `update_story_state` | Updates story state tracking data | When plot events occur that should be recorded |
| `update_narrative_state` | Updates narrative tracking information | When narrative context changes need recording |

## Advisory & Research

| Tool | Description | When It Fires |
|------|-------------|---------------|
| `run_council` | Convenes the advisory panel (Council mode) | In Council mode when you ask for multi-perspective analysis |
| `run_research` | Executes a web-backed research query | In Research mode for factual information gathering |
| `delegate_technical` | Answers factual/technical questions | When the question is outside the fictional world |

## Utility

| Tool | Description | When It Fires |
|------|-------------|---------------|
| `roll_dice` | Generates random dice rolls | When you need randomness or game mechanics |
| `generate_image` | Creates AI-generated images | When you request visual content |
| `web_search` | Searches the web directly | When enabled and you need current information |
| `email_developer` | Sends feedback to the developer | When you want to report an issue or send feedback |
| `request_code_change` | Requests a code change to QuillForge itself | For development/meta requests about the system |

## Tool Availability by Mode

All tools are registered globally and available in every mode, but the mode's system prompt guides the orchestrator toward mode-appropriate tools. For example:
- Writer mode prioritizes `write_prose` and the approval workflow
- Council mode prioritizes `run_council`
- Research mode prioritizes `run_research` and `web_search`
- General mode routes naturally to any tool based on context
