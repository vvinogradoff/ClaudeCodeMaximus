# Daemon Support for Slash Commands and Skills

## Background

The ClaudeMaximus / Tessyn Desktop app wraps the Claude Code CLI via the Tessyn daemon. Currently, it sends user messages through `run.send` and receives streaming responses. However, the Claude CLI supports slash commands (`/compact`, `/clear`, `/model`, `/help`, etc.) and user-installed skills (`/commit`, `/review-pr`, etc.) that are not accessible through the daemon.

The goal is to expose the full CLI command and skill system through daemon RPCs so that GUI frontends can offer the same functionality as the terminal CLI — including autocomplete.

## What the GUI Needs

### 1. Command Discovery

The GUI needs to know what commands and skills are available so it can:
- Show autocomplete suggestions when the user types `/`
- Validate commands before sending
- Display help text and descriptions

**Proposed RPC: `commands.list`**

```json
// Request
{"jsonrpc": "2.0", "id": 1, "method": "commands.list", "params": {}}

// Response
{
  "commands": [
    {
      "name": "compact",
      "description": "Compact the conversation to reduce context usage",
      "type": "builtin",
      "args": []  // no arguments
    },
    {
      "name": "model",
      "description": "Switch the AI model",
      "type": "builtin",
      "args": [
        {"name": "model", "description": "Model name", "required": false,
         "choices": ["opus", "sonnet", "haiku"]}
      ]
    },
    {
      "name": "commit",
      "description": "Commit changes with a message",
      "type": "skill",
      "source": "/Users/alice/.claude/skills/commit.md",
      "args": [
        {"name": "message", "description": "Commit message", "required": false}
      ]
    },
    {
      "name": "login",
      "description": "Authenticate with Claude",
      "type": "builtin",
      "args": []
    }
  ]
}
```

The `type` field distinguishes built-in commands from user-installed skills. The `args` array describes positional/named arguments for autocomplete and validation. `choices` provides enum values for argument autocomplete.

### 2. Command Execution

Some commands are simple actions (e.g., `/clear` clears context), others produce output (e.g., `/help`), and some modify the active run's behavior (e.g., `/model sonnet`).

**Proposed RPC: `commands.execute`**

```json
// Request
{"jsonrpc": "2.0", "id": 2, "method": "commands.execute", "params": {
  "command": "compact",
  "args": "",
  "externalId": "session-uuid",
  "projectPath": "/path/to/project"
}}

// Response (synchronous result for simple commands)
{
  "output": "Session compacted. Context reduced from 45k to 12k tokens.",
  "sideEffects": ["context_changed"]
}
```

For commands that start a run (like skills), the response could return a `runId` and stream events via the existing `run.*` notification system:

```json
// Response (for skill execution that starts a run)
{
  "runId": "uuid",
  "streaming": true
}
```

### 3. Command-Specific Considerations

| Command | Behavior | Notes |
|---------|----------|-------|
| `/compact` | Triggers context compaction | Could reuse existing compaction logic; emit `run.*` events |
| `/clear` | Resets Claude session context | Nullify session ID; no run needed |
| `/model <name>` | Changes model for subsequent runs | Session-level setting; immediate acknowledgment |
| `/help` | Returns help text | Synchronous response |
| `/login` | Launches auth flow | Currently handled client-side; could stay that way |
| `/commit` (skill) | Runs a skill prompt | Starts a run; streams via `run.*` events |
| `/review-pr` (skill) | Runs a skill prompt | Same as above |

### 4. Skill Management

Optional but useful for future GUI integration:

```json
// List installed skills
{"jsonrpc": "2.0", "id": 3, "method": "skills.list", "params": {}}

// Response
{
  "skills": [
    {"name": "commit", "path": "/Users/alice/.claude/skills/commit.md",
     "description": "Commit changes", "invocable": true},
    {"name": "review-pr", "path": "/Users/alice/.claude/skills/review-pr.md",
     "description": "Review a pull request", "invocable": true}
  ]
}
```

### 5. Reasoning Effort

The GUI also needs to control reasoning effort per-run. This should be a parameter on `run.send`:

```json
{"jsonrpc": "2.0", "id": 4, "method": "run.send", "params": {
  "prompt": "...",
  "projectPath": "...",
  "model": "opus",
  "profile": "home",
  "reasoningEffort": "high",  // "low", "medium", "high" — maps to CLI --reasoning-effort
  "permissionMode": "auto-approve"
}}
```

## Implementation Suggestion

The simplest path is probably:

1. **`commands.list`** — Enumerate built-in commands from a static registry + scan `~/.claude/skills/` for `.md` files with invocable frontmatter. This is read-only and cheap.

2. **`commands.execute`** — For built-in commands, handle directly in the daemon. For skills, translate to a `run.send` with the skill's expanded prompt (the CLI already does this expansion).

3. **`reasoningEffort` on `run.send`** — Pass through to the `claude` subprocess as `--reasoning-effort <value>`.

4. **`commands.list` should be refreshable** — Skills can be added/removed at any time. The GUI calls `commands.list` on startup and optionally on focus/reconnect.

## Priority

- **P0**: `commands.list` — enables autocomplete in the GUI
- **P0**: `reasoningEffort` on `run.send` — simple passthrough parameter
- **P1**: `commands.execute` for built-in commands — `/compact`, `/clear`, `/model`, `/help`
- **P2**: `commands.execute` for skills — requires prompt expansion logic
- **P3**: `skills.list` / `skills.install` — full skill lifecycle management
