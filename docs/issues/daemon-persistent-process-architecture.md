# Daemon Architecture: Persistent Per-Session Claude Processes

## Goal

Make the desktop GUI a complete frontend for Claude Code — every CLI feature available in the terminal should be available in the desktop app, with no workarounds.

## Current Architecture

The daemon currently spawns `claude` per message:

```
GUI → daemon.run.send → spawn `claude --print --output-format stream-json --resume <id>`
                       → read events from stdout → process exits
                       → next message: spawn again with --resume
```

This is a **one-shot per message** model. It works for simple text in/out but cannot support features that require persistent process state.

## The Problem

Almost everything we're missing in the desktop app traces back to this architectural choice. Features that need a long-lived process between user turns simply can't fit the per-message spawn model.

### Inventory of CLI features by status

**✅ Already working through the daemon's current model:**
- Text input/output, streaming responses
- Tool execution (Read, Write, Edit, Bash, etc.)
- Slash commands and skills
- Model selection (`--model`)
- Reasoning effort (`--effort`)
- Profile/auth selection
- Session resume (`--resume`)

**🟡 Missing today, unlockable with a persistent-process model:**

| Feature | What it needs |
|---|---|
| Image attachments | Send `{type:"image", source:{base64...}}` content blocks via stream-json input |
| File attachments | Same — content blocks; or `@path` syntax in text |
| Interactive permission prompts | Process emits permission request → daemon forwards to GUI → GUI replies → daemon writes to stdin |
| `/fast` toggle mid-conversation | Control message to running process |
| Background bash output (long-running tools) | Streams to the same persistent stdout |
| `--continue` (resume last conversation) | Spawn flag |
| `--fork-session` | Spawn flag |
| Custom agents (`--agents`) | Spawn-time JSON |
| MCP config (`--mcp-config`) | Spawn flag |
| Plugin dirs (`--plugin-dir`) | Spawn flag |
| System prompts (`--append-system-prompt`, `--system-prompt-file`) | Spawn flag |
| Add directories (`--add-dir`) | Spawn flag — needed for attachments outside cwd |
| Cost limits (`--max-budget-usd`) | Spawn flag |
| JSON schema output (`--json-schema`) | Spawn flag |
| Memory (`/memory`, CLAUDE.md auto-discovery) | Already works inside the process — just route the slash commands |
| Hooks | Already work inside the process |
| Allowed/disallowed tools (`--allowedTools`, `--disallowedTools`) | Spawn flag |

**🔴 Truly fundamental limitations (can't be fixed, and don't matter):**
- Terminal status line — the GUI has its own status panel, which is better
- Terminal control sequences — GUI renders everything itself
- ANSI-formatted help output — render help as markdown instead

There are no genuine blockers. Everything else is achievable.

## Proposed Architecture

Switch from per-message spawning to **per-session persistent processes**.

```
GUI → daemon.sessions.create   → daemon spawns `claude --input-format stream-json
                                                       --output-format stream-json
                                                       <spawn flags>` (long-lived)
GUI → daemon.run.send          → daemon writes JSON content blocks to claude's stdin
GUI ← daemon (run.* events)    ← daemon reads JSON events from claude's stdout
GUI → daemon.run.send          → daemon writes next message to the same stdin
...
GUI → daemon.sessions.close    → daemon kills the process
```

Each `externalId` maps to one running `claude` process. Lifetime: spawn on session creation (or first message, lazily), kill on explicit close or idle timeout.

## Required Daemon Changes

### 1. Process model

- Maintain a `Map<externalId, RunningClaudeProcess>` of long-lived processes
- Spawn `claude --input-format stream-json --output-format stream-json --verbose` per session
- Read line-delimited JSON from stdout, route events to subscribers
- Write line-delimited JSON to stdin when GUI calls `run.send`
- Idle timeout (configurable, e.g. 30 min) auto-kills unused processes
- Explicit `sessions.close` RPC to terminate

### 2. `run.send` accepts content blocks, not just a string prompt

**New params shape:**
```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "run.send",
  "params": {
    "externalId": "session-uuid",
    "content": [
      {"type": "text", "text": "What is in this image?"},
      {
        "type": "image",
        "source": {
          "type": "base64",
          "media_type": "image/png",
          "data": "<base64-encoded bytes>"
        }
      }
    ]
  }
}
```

**Backwards compatibility:** keep accepting `prompt: string` — treat it as a single text content block. Existing GUIs continue to work without changes.

The content block format mirrors the Anthropic Messages API exactly, which is also what Claude Code's stream-json input expects. No translation layer needed in the daemon — pass it straight through.

### 3. Permission prompt routing

When the running `claude` process emits a permission request (the user needs to approve a tool use), the daemon forwards it as a notification:

```json
{
  "jsonrpc": "2.0",
  "method": "run.permission_request",
  "params": {
    "runId": "...",
    "externalId": "...",
    "requestId": "perm-uuid",
    "tool": "Bash",
    "input": {"command": "rm -rf foo"},
    "rationale": "Remove the foo directory"
  }
}
```

The GUI shows a modal, and the user's choice goes back via:

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "run.permission_response",
  "params": {
    "requestId": "perm-uuid",
    "decision": "approve" | "deny" | "always_approve_for_session" | "always_approve_globally"
  }
}
```

The daemon writes the response to claude's stdin in whatever format the CLI expects.

### 4. Spawn-time configuration via `sessions.create`

Currently sessions are created implicitly on first `run.send`. Change this to an explicit `sessions.create` RPC that accepts all the spawn-time configuration:

```json
{
  "jsonrpc": "2.0",
  "id": 3,
  "method": "sessions.create",
  "params": {
    "projectPath": "/path/to/project",
    "model": "opus",
    "profile": "home",
    "permissionMode": "auto-approve",
    "reasoningEffort": "high",
    "addDirs": ["/extra/dir1", "/extra/dir2"],
    "mcpConfig": "/path/to/mcp.json",
    "agents": {"reviewer": {"description": "...", "prompt": "..."}},
    "pluginDirs": ["/path/to/plugin"],
    "systemPromptAppend": "Extra system prompt text",
    "allowedTools": ["Read", "Edit", "Bash(git:*)"],
    "maxBudgetUsd": 5.00,
    "jsonSchema": null,
    "fastMode": false,
    "continueLastConversation": false,
    "forkSession": false,
    "title": "Optional human-readable title"
  }
}
```

Returns:
```json
{"externalId": "new-session-uuid", "spawned": true}
```

Existing implicit creation via `run.send` (without `externalId`) continues to work for simple cases.

### 5. Mid-process control messages

For things that change during a session (without restarting the process):

```json
// Toggle fast mode
{"method": "run.set_fast_mode", "params": {"externalId": "...", "enabled": true}}

// Cancel a tool that's currently running
{"method": "run.cancel_tool", "params": {"runId": "..."}}
```

The daemon translates these into whatever stdin commands or signals the CLI expects.

### 6. Session lifecycle

```json
// List all running sessions (process state, not just persisted sessions)
{"method": "sessions.runningList"}

// Close a session (kill the process)
{"method": "sessions.close", "params": {"externalId": "..."}}

// Get session config (the spawn-time params for an existing session)
{"method": "sessions.getConfig", "params": {"externalId": "..."}}
```

### 7. Idle management

- Configurable idle timeout (default 30 min)
- After timeout, daemon kills the process but keeps the session metadata
- Next `run.send` to a closed session re-spawns it with `--resume <externalId>` and the same config
- Optional GUI hint via `sessions.runningList` so it can show which sessions are "warm" vs "cold"

## Required Claude Code (CLI) Changes

Most of what we need already exists. Worth confirming with the CLI team:

1. **Stable stream-json input format with content blocks** — the `--input-format stream-json` flag exists; we need to confirm the schema is stable for GA and that it accepts image content blocks via stdin.

2. **Permission request format in stream-json output** — when `--input-format stream-json` is used, does the CLI emit a structured permission request event that the daemon can intercept? If not, this is a small CLI ask.

3. **Permission response via stdin** — corresponding format for sending the user's decision back. Same dependency.

If these three exist or can be added, we have everything needed.

## Migration Strategy

The change is non-breaking if done in phases:

**Phase 1 — Daemon adds new API surface, keeps old:**
- Add `sessions.create`, `sessions.close`, `sessions.runningList`, `sessions.getConfig`
- Add content-block form of `run.send` (alongside the existing `prompt: string`)
- Add `run.permission_request` / `run.permission_response`
- Add the spawn-flag fields to `sessions.create`
- Existing GUIs that use `prompt: string` keep working

**Phase 2 — Desktop adopts new API:**
- Switch to `sessions.create` for new sessions
- Use content blocks for messages with attachments
- Wire up permission dialog
- Add settings panels for spawn-time config
- Implement attachment paste/drop/picker (now trivially supported)

**Phase 3 — Optional cleanup:**
- Deprecate `prompt: string` on `run.send` (keep working but add a deprecation note)
- Eventually remove the per-message-spawn fallback path

## Open Questions

1. **Process startup cost** — how slow is it to spawn `claude` vs amortized cost over a long session? Per-message spawning has a per-turn latency hit; persistent processes have a one-time cost. Probably persistent wins, but worth measuring.

2. **Memory usage** — N persistent processes vs N short-lived ones. For typical use (5-10 active sessions), probably negligible. Idle timeout handles the upper bound.

3. **Crash recovery** — if the daemon dies, all running claude processes also die. On restart, sessions are "cold" and need re-spawning. Acceptable; document as expected behavior.

4. **Permission request format** — does the current CLI in stream-json mode emit a clean permission event, or is it embedded in something else? Need to confirm with the CLI before fully committing.

5. **What does the CLI expect on its stdin for stream-json input?** The existing test we've done sends a single message. Need to confirm that subsequent messages over the same stdin work as expected (i.e. that `--input-format stream-json` actually supports a multi-turn stream, not just a single line).

## Why This Matters

Every workaround we don't build now is a workaround we don't have to maintain or migrate later. The desktop app is positioned to become a first-class Claude Code frontend — the question is whether it's a thin shell over the daemon (which then needs to be a thin shell over the CLI) or a translation layer with its own quirks. The first option is dramatically simpler to maintain and means new CLI features show up in the GUI for free.

The persistent-process model is also what makes the daemon genuinely useful — without it, the daemon is just a JSONL parser and process spawner that the GUI could replicate inline. With it, the daemon becomes the runtime owner of Claude sessions, which is a meaningful abstraction.
