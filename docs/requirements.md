# ClaudeMaximus — Requirements

## Project Overview

**ClaudeMaximus** is a cross-platform Avalonia desktop application that wraps the Claude Code CLI tool, providing a structured session management UI. It does not replace Claude Code — it hosts it, stores its history, and presents it in an organized, navigable interface.

---

## Anthropic T&C Compliance Note

This application is a **local desktop tool** that launches the `claude` CLI process already installed on the user's machine. It does not:
- Redistribute or bundle the Claude Code binary
- Bypass authentication or licensing
- Interact with the Anthropic API directly
- Claim affiliation with or endorsement by Anthropic

This is equivalent to writing a custom terminal emulator or IDE plugin that runs `claude` as a subprocess — a well-established and accepted category of tooling. No T&C violations are anticipated for personal use. **If the application is ever distributed publicly**, review Anthropic's usage policies and avoid implying official affiliation in branding.

---

## Functional Requirements

### FR.1 — Session Tree

**FR.1.1** The left panel shall display a hierarchical tree of sessions.

**FR.1.2** The tree shall support exactly three node types:

| Type | Physical existence | Renameable | Purpose |
|---|---|---|---|
| **Directory** | Real filesystem directory | No — name is derived from the path | Top-level entry; defines the working directory for all sessions beneath it |
| **Group** | `appsettings.json` only | Yes | Intermediate organisational node; no session, no working directory of its own |
| **Session** | Session file on disk; name stored in `appsettings.json` | Yes | Terminal node; represents one Claude Code conversation |

**FR.1.3 — Directory node display name:** Derived from the working directory path relative to its `.git` root:
- If `.git` is at the root of the working directory, show only that directory name (e.g., `datum_api` for `C:\Projects\...\datum_api`).
- If the working directory is a subdirectory below a `.git` root, show the path from the `.git` root directory name downward (e.g., `datum_api\Datum.Web\EmailTemplates`).
- If no `.git` root is found anywhere above the path, show the full absolute path.
- Directory node display names are never overridden by the user.

**FR.1.4 — Working directory inheritance:** All Session nodes nested under a Directory node (at any depth, through any number of Group nodes) inherit the Directory node's path as the `claude` process working directory.

**FR.1.5 — Adding nodes:**
- **Add Directory:** user selects a folder via a directory picker; a new top-level Directory node is created. If a Directory node for that path already exists, no duplicate is created.
- **Add Group:** user right-clicks a Directory or Group node and selects "Add Group"; a new named Group node is created as a child. Name is entered inline.
- **Add Session:** user right-clicks a Directory or Group node and selects "Add Session"; a new Session node is created as a child, a session file is created on disk (see FR.3), and a name is entered inline.
- **New Session shortcut (no tree selection):** if the user triggers "New Session" without a tree context, they are first prompted to select a working directory. If a matching Directory node exists it is used; otherwise a new Directory node is created.

**FR.1.6 — Renaming:**
- Group nodes and Session nodes may be renamed via inline editing (double-click label, or context menu → Rename).
- Directory nodes may not be renamed; their display name always reflects the filesystem path.
- Renamed names are stored in `appsettings.json`.

**FR.1.7 — Deletion rules:**
- A **Session** node may be deleted from the tree only if its corresponding session file no longer exists on disk. The UI checks file existence before allowing the action.
- A **Group** node may be deleted only if it has no children.
- A **Directory** node may be deleted only if it has no children.
- No cascading deletion: the UI shall never delete files or child nodes automatically.

**FR.1.8 — Running indicator:** Session nodes with an active `claude` process shall be visually distinguished in the tree (e.g., animated spinner or distinct icon).

**FR.1.9 — Search:** A search box positioned above the tree performs a full-text search across all session files in the configured Session Files Root. The tree filters to show only Session nodes whose file content matches; ancestor nodes (Group, Directory) of matching sessions are expanded. Non-matching sessions are hidden. Search is a linear file scan for v1; indexing is out of scope.

---

### FR.2 — Session View (Right Panel)

**FR.2.1** Selecting a Session node in the tree opens its session view in the right panel.

**FR.2.2** The session view shall display the session's user-assigned name (from `appsettings.json`) at the top as a header.

**FR.2.3** The session view shall render the conversation in a terminal-like format, visually distinguishing:
- User input (prompts sent)
- Claude Code text output
- Tool use / tool result blocks
- System messages and status lines

**FR.2.4** The session view shall provide a multi-line input text box at the bottom for composing and submitting prompts to Claude Code. Submission is via `Ctrl+Enter` or a dedicated Send button.

**FR.2.5** When Claude Code is running (processing a prompt), the input area shall be disabled and a visual busy indicator shown.

---

### FR.3 — Session Files

**FR.3.1** Each session is backed by a single plain-text file. Session files are stored in a configurable Session Files Root directory (see FR.4).

**FR.3.2 — Session file naming:** Files are named using the pattern:
```
YYYY-MM-dd-HHmm-{6_random_lowercase_alpha}.txt
```
Example: `2026-03-12-1430-xkqbzf.txt`

The name encodes creation time and a random suffix to avoid collisions. It carries no user-visible semantic meaning; the human-readable session name is stored separately in `appsettings.json`.

**FR.3.3 — Session file format:** Newline-delimited entries. Each entry begins with a single header line followed by the message body:
```
[YYYY-MM-ddTHH:mm:ssZ] ROLE
<message body lines>

```
Where `ROLE` is one of: `USER`, `ASSISTANT`, `SYSTEM`.

**FR.3.4 — Compaction separator:** When Claude Code emits a compaction event, append a separator entry:
```
[YYYY-MM-ddTHH:mm:ssZ] COMPACTION
```
Subsequent messages continue after the separator. The full pre-compaction history above the separator is always preserved and displayed.

**FR.3.5** Each append to a session file is flushed to disk immediately (no buffered writes).

**FR.3.6** Session files are intentionally human- and AI-readable without special tooling, enabling Claude Code and other tools to ingest them directly.

---

### FR.4 — Application Configuration

**FR.4.1** All application configuration is stored in a single `appsettings.json` file in the platform application data directory (e.g., `%APPDATA%\ClaudeMaximus\appsettings.json` on Windows).

**FR.4.2 — Tree structure in appsettings.json:** The complete tree (Directory nodes with their paths, Group nodes with their names, Session nodes with their names and bound session file paths) is persisted in `appsettings.json`. Directory nodes are the roots; Group and Session nodes are nested within.

**FR.4.3 — appsettings.json is written atomically:** On every save, write to a temporary file in the same directory then rename over the target, to prevent corruption on crash.

**FR.4.4 — Settings window:** A Settings window (accessible from the main menu or toolbar) allows the user to configure:
- Session Files Root directory (folder picker)
- `claude` CLI executable path (default: resolved from `PATH`)

**FR.4.5** Application window state (size, position, left-panel splitter position) is also persisted in `appsettings.json` and restored on next launch.

---

### FR.5 — Claude Code Process Management

**FR.5.1** The application shall launch `claude` as a child process per Session using `--output-format stream-json` to receive newline-delimited JSON events.

**FR.5.2** User input is written to the `claude` process stdin.

**FR.5.3** The `claude` process is started with its working directory set to the path of the Directory node that owns the Session.

**FR.5.4** If a `claude` process exits unexpectedly, the session view displays a status message and a Restart button.

**FR.5.5** Multiple sessions may run concurrently; each has an independent `claude` process. Background sessions continue running when another session is selected.

---

### FR.6 — Application Shell

**FR.6.1** The main window uses a two-panel layout:
- Left panel: search box + session tree
- Right panel: active session view (or empty placeholder when no session selected)

**FR.6.2** The left panel width is adjustable via a draggable splitter.

**FR.6.3** A Settings entry in the main menu opens the Settings window (FR.4.4).

---

## Non-Functional Requirements

**NFR.1 — Cross-platform:** Runs on Windows, macOS, and Linux. Platform-specific code (path resolution, process launching, app data directory) is isolated behind interfaces.

**NFR.2 — Performance:** Session tree is responsive for up to 500 sessions. Search via full file scan is acceptable for v1; may be slow on large sets.

**NFR.3 — Durability:** Session file appends are flushed immediately. `appsettings.json` is written atomically.

**NFR.4 — Architecture:** MVVM with Avalonia + ReactiveUI. One ViewModel per significant view. Models and services have no UI dependencies.

**NFR.5 — Testability:** Session file I/O, tree persistence, and process management are behind interfaces; unit tests require no real `claude` process or real filesystem.

---

## Out of Scope (Initial Version)

- Search indexing (linear file scan acceptable for v1)
- Session sharing or sync across machines
- Diff or branching of session history
- Custom themes beyond system light/dark
- Deletion of session files from within the UI
- Plugin system

---

## Glossary Reference

See `/docs/glossary.md` for domain term definitions.
