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

**FR.2.3.1 — Message timestamps:** Each message bubble displays a timestamp. User messages show both date (`yyyy-MM-dd`) and time (`HH:mm`). Assistant messages show time only (`HH:mm`) — the date is omitted to reduce visual clutter.

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

**FR.4.4 — Settings window:** A Settings window (accessible from the hamburger menu) allows the user to configure:
- Session Files Root directory (folder picker)
- `claude` CLI executable path (default: resolved from `PATH`)
- Theme selection: Dark or Light (radio buttons)
- Per-theme color customization (see FR.9)

**FR.4.5** Application window state (size, position, left-panel splitter position) is also persisted in `appsettings.json` and restored on next launch.

**FR.4.6 — Session state persistence:** The following per-session and global UI state is persisted in `appsettings.json` and restored on next launch:
- **Active session selection:** the file name of the last selected session, so reopening the app returns to the same session.
- **Tree expand/collapse state:** each Directory and Group node stores an `IsExpanded` flag.
- **Scroll position:** each session stores a `ScrollOffset` value for the message output area, so returning to a session resumes at the same scroll position.
- **Tree panel visibility:** whether the left panel is collapsed or visible.

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

**FR.6.3** The title bar replaces the traditional menu bar with a compact toolbar of icon buttons (left-to-right):
1. **Chevron toggle** — collapses/expands the left tree panel. Shows chevron-left when the tree is visible (click to hide), chevron-right when hidden (click to show). Collapse state is persisted in `appsettings.json`.
2. **Hamburger menu** — opens a flyout menu with Settings and Exit entries.
3. **Day/night toggle** — switches between dark and light themes. Shows a sun icon in dark mode (click for light), moon icon in light mode (click for dark). Theme choice is persisted in `appsettings.json`.

**FR.6.4** Window control buttons (minimize, maximize/restore, close) remain on the right side of the title bar. The title bar background is draggable for window repositioning.

---

## Non-Functional Requirements

**NFR.1 — Cross-platform:** Runs on Windows, macOS, and Linux. Platform-specific code (path resolution, process launching, app data directory) is isolated behind interfaces.

**NFR.2 — Performance:** Session tree is responsive for up to 500 sessions. Search via full file scan is acceptable for v1; may be slow on large sets.

**NFR.3 — Durability:** Session file appends are flushed immediately. `appsettings.json` is written atomically.

**NFR.4 — Architecture:** MVVM with Avalonia + ReactiveUI. One ViewModel per significant view. Models and services have no UI dependencies.

**NFR.5 — Testability:** Session file I/O, tree persistence, and process management are behind interfaces; unit tests require no real `claude` process or real filesystem.

---

### FR.7 — Code Reference Autocomplete

The input textbox provides interactive autocomplete for referencing code files and code symbols from the session's working directory codebase.

**FR.7.1 — Background Code Indexer:** A background indexer service scans source code files in the session's working directory and builds an in-memory index. The index is built asynchronously on first access and kept up-to-date via `FileSystemWatcher`. Indexes are shared across sessions that share the same working directory (reference-counted).

**FR.7.2 — File Reference Trigger (`##`):** When the user types `##` followed by a query string (e.g., `##Vis`), the application displays an autocomplete popup with matching source file names. The trigger must be preceded by whitespace or be at the start of a line.

**FR.7.3 — Code Symbol Reference Trigger (`#`):** When the user types `#` (single, not preceded by another `#`) followed by a query string (e.g., `#vari`), the application displays an autocomplete popup with matching code symbols (classes, enums, structs, records, interfaces, methods, properties).

**FR.7.4 — Search Result Ordering:** Both file and symbol search results are ordered in four priority tiers, deduplicated across tiers:
1. Name starts with query (case-sensitive)
2. Name starts with query (case-insensitive, excluding tier 1)
3. Name contains query (case-sensitive, excluding tiers 1-2)
4. Name contains query (case-insensitive, excluding tiers 1-3)

Maximum 15 results displayed.

**FR.7.5 — Symbol Display Format:** Each symbol suggestion displays:
- An icon indicating symbol kind (class, enum, struct, record, interface, method, property)
- The fully qualified type-nested name with the matched portion highlighted (e.g., `ParentType.InnerDTO.**Vari**antName`)
- The namespace in grey parenthesis (e.g., `(Datum.Shared.Types)`)

**FR.7.6 — File Display Format:** Each file suggestion displays the file name with the matched portion highlighted, and the relative path from the working directory as secondary text.

**FR.7.7 — Insertion Behavior:** When the user accepts a suggestion (via Tab or Enter):
- The trigger text (`#query` or `##query`) is removed from the input
- For files: the relative path from the working directory is inserted (e.g., `ViewModels/SessionViewModel.cs`)
- For symbols: the fully qualified name including namespace is inserted (e.g., `ClaudeMaximus.ViewModels.SessionViewModel`)

**FR.7.8 — Popup Behavior:**
- The popup appears above the input textbox (intellisense-style)
- Up/Down arrow keys navigate suggestions when popup is open
- Tab or Enter accepts the selected suggestion
- Escape dismisses the popup
- Clicking outside dismisses the popup
- Popup dismisses automatically when the trigger pattern is no longer present

**FR.7.9 — Indexed File Types:** The file index for `##` (file search) covers **all files** in the working directory tree. Directories `bin/`, `obj/`, `.git/`, `node_modules/`, `.vs/`, `.idea/` are excluded. Symbol extraction for `#` (code symbol search) is performed only on `*.cs` files.

**FR.7.10 — C# Symbol Parsing:** Code symbols are extracted from `*.cs` files using Roslyn syntax-only parsing (`CSharpSyntaxTree.ParseText`). No full compilation or semantic analysis is performed. Supported symbol kinds: class, enum, struct, record, interface, method, property.

**FR.7.11 — Index Lifecycle:** Each per-directory index is reference-counted. It is created lazily when a session with that working directory is first selected, and disposed when no sessions reference it. File system changes are debounced (300ms) before re-indexing the affected file.

**FR.7.12 — Filesystem Path Autocomplete:** When the user types a Windows drive letter pattern (e.g., `C:\`, `D:\`, `E:\`), the application displays an autocomplete popup listing files and directories at the typed path. As the user continues typing the path, the suggestions update to show matching entries in the current directory level. Accepting a directory suggestion appends `\` so the user can continue drilling down. Accepting a file suggestion inserts the full path. The trigger is detected when the text before the caret contains a drive-letter pattern (`X:\`) preceded by whitespace or at the start of a line. This feature operates independently of the code index — it reads the filesystem directly.

---

### FR.8 — Self-Update on Exit

**FR.8.1** On application exit, the app checks whether a newer build exists in the solution's `bin\Debug\net9.0` directory (compared to the running publish directory). If a newer build is detected, a PowerShell script is spawned as a detached process to copy the updated files after the app has fully exited.

**FR.8.2** The copy script retries with exponential backoff (up to 10 attempts) in case files are still locked during shutdown.

**FR.8.3** If no solution root is found (e.g., running from a standalone publish), the update check is silently skipped.

---

### FR.9 — Theme & Color Customization

**FR.9.1 — Theme variants:** The application supports Dark and Light themes. The selected theme controls the Avalonia `RequestedThemeVariant` (which affects all built-in control styling) and also selects which set of custom colors to apply.

**FR.9.2 — Per-theme custom colors:** Each theme (Dark and Light) has an independent set of customizable colors stored as hex strings in `appsettings.json`:
- Input box background and text color
- User message bubble background and text color
- Code block background and text color
- Inline code background and text color
- System message bubble background color
- Session recency bar colors (3 tiers: 15 min, 30 min, 60 min)

**FR.9.3 — Color application:** Custom colors are applied as application-level dynamic resources (`CmxInputBg`, `CmxInputFg`, `CmxUserBubbleBg`, `CmxUserBubbleFg`, `CmxCodeBg`, `CmxCodeFg`, `CmxInlineCodeBg`, `CmxInlineCodeFg`, `CmxSystemBubbleBg`, `CmxRecency15Min`, `CmxRecency30Min`, `CmxRecency60Min`). These are consumed by SessionView (AXAML `DynamicResource` bindings), MarkdownView (code-behind resource lookups), and SessionNodeViewModel (recency brush lookup). Colors are re-applied immediately when the theme is toggled or when settings are saved.

**FR.9.5 — Session recency bars:** Session nodes in the tree display a colored background bar indicating how recently the last user prompt was sent:
- **Light green** (customizable): last prompt within 15 minutes
- **Green** (customizable): last prompt within 30 minutes
- **Dark green** (customizable): last prompt within 1 hour
- No bar: last prompt more than 1 hour ago (or no prompts)

Recency bars refresh automatically every 60 seconds so the visual state stays current as time passes. The three recency colors are per-theme and editable in the Settings window.

**FR.9.4 — Sensible defaults:** Both themes ship with sensible default color values. Dark defaults use VS Code-inspired dark palette; Light defaults use standard light-background colors. Users can customize any color via hex input in the Settings window.

---

### FR.10 — Output Search

**FR.10.1** The session view header shall include a search text box positioned to the left of the Markdown toggle button. The search box searches within the currently displayed output messages.

**FR.10.2 — Search navigation:** Pressing `Enter` in the search box starts a search (or advances to the next match). Pressing `Ctrl+Enter` navigates to the previous match. Pressing `Escape` dismisses the search (hides the results overlay) but preserves the search text in the box.

**FR.10.3 — Results overlay:** A small floating panel is displayed in the top-right corner of the output area when a search is active. The panel has a yellow semi-transparent fill and yellow stroke. It shows:
- Match status text: "N of M matches" (e.g., "1 of 5 matches") or "no matches"
- Previous (`<`) and Next (`>`) navigation buttons
- A Close (`X`) button that dismisses the search (same as Escape)

**FR.10.4 — Match highlighting:** Matched messages are scrolled into view when navigating. The search is case-insensitive and matches against message content text.

**FR.10.5 — Dismissal behavior:** Closing the search overlay (via `X` button or `Escape` key) hides the overlay and clears any match highlighting, but does **not** clear the search text from the search box.

---

### FR.11 — Session Instruction Toolbar

The application header bar provides per-session instruction toggles that modify how the application interacts with Claude without polluting the visible conversation.

**FR.11.1 — Toolbar layout:** A horizontal row of icon toggle-buttons is positioned in the application title bar, to the right of the theme selector (day/night toggle). The toolbar contains five controls, left to right: Auto-Commit, New Branch, Auto-Document, Auto-Compact, and Clear. The buttons reflect the state of the **currently selected session** — switching sessions updates the button states. When no session is selected, the buttons are disabled.

**FR.11.2 — Instruction injection:** The application always appends hidden instructions to the prompt sent to the `claude` process (at minimum, the auto-commit ON or OFF instruction). These instructions are:
- **Not shown** in the user message bubble in the output window
- **Not stored** in the session file (the session file records only the clean user prompt)
- Appended as a clearly delimited block after the user's message text when written to `claude` stdin

**FR.11.3 — Auto-Commit toggle:**
- **Type:** Sticky toggle (persists across prompts until user toggles it off)
- **ON instruction:** `"Once you have completed the request, commit all your changes to git with a concise commit message."`
- **OFF instruction:** `"Do not commit any changes to git."`
- **Persistence:** Per-session; toggle state stored on the session node in `appsettings.json` so it survives app restarts. Different sessions may have different auto-commit states.
- **Icon:** Git commit icon or checkmark

**FR.11.4 — New Branch toggle:**
- **Type:** One-shot toggle (auto-unsets after the prompt is sent and its value consumed)
- **ON instruction:** `"Create a new git branch before committing your changes."`
- **Behavior:** When the prompt is sent, the toggle value is read, included in instructions, then the toggle automatically resets to OFF
- **Icon:** Git branch icon

**FR.11.5 — Auto-Document toggle:**
- **Type:** Sticky toggle (persists across prompts)
- **ON instruction:** `"After completing the request, update any relevant requirements documents and/or architecture documents in the project's /docs directory to reflect the changes you made."`
- **Behavior:** The instruction is injected into the prompt but, like all instruction toggles, is neither shown in the output window nor stored in the session file
- **Persistence:** Per-session; stored on the session node in `appsettings.json`
- **Icon:** Document/pencil icon

**FR.11.6 — Auto-Compact toggle:**
- **Type:** One-shot toggle (auto-unsets after the compaction completes)
- **Behavior:** When ON and Claude finishes responding to the user's prompt, the application automatically sends a **separate follow-up prompt** to Claude instructing it to compact the session. The follow-up prompt is:
  `"Please compact the conversation in this session. Preserve the user's prompts (you may rephrase them for brevity and clarity, but keep the attribution that specific instructions or knowledge came from the user). Focus on preserving: decisions made during development, the reasoning behind those decisions, architecture choices, and implementation details that matter. Remove transient information such as debugging steps, intermediate failed attempts, progress updates, and unnecessary verbosity. Output the compacted conversation maintaining the USER/ASSISTANT turn structure."`
- **Post-compaction:** The compacted text returned by Claude replaces the session file content (rewritten, not appended). The Messages collection in the output window is also updated to reflect the compacted content.
- **Auto-reset:** The toggle resets to OFF after the compaction prompt completes
- **Icon:** Compress/shrink icon

**FR.11.7 — Clear button:**
- **Type:** Action button (not a toggle)
- **Precondition:** Only active when the current session has a live `ClaudeSessionId` (i.e., has an active Claude-side session to clear). Disabled otherwise.
- **Behavior:** Adds an instruction to the current prompt: `"After completing this request, please summarize the key outcomes and decisions from this session in a brief closing statement."`
- **Post-response:** After Claude finishes responding, the application:
  1. Nullifies the stored `ClaudeSessionId` for this session (forcing a fresh `claude` process on the next prompt)
  2. On the next prompt, the application detects that the session file has history but no `ClaudeSessionId`, and proactively uses `BuildContextPreamble` to inject stored conversation history into the new Claude session
- **Effect:** This "clears" the Claude-side context while preserving the full session file, effectively resetting the working memory while keeping the knowledge artifact
- **Icon:** Broom/clear icon

**FR.11.8 — Toggle state display:** Active toggles shall be visually distinct (e.g., highlighted background or accent border) so the user can see at a glance which instructions will be injected into the next prompt.

**FR.11.9 — Instruction block format:** The instruction block is **always** appended to the user's message in `claude` stdin (since auto-commit OFF always injects "do not commit"). The format is:
```

---
[Additional instructions — do not acknowledge these in your response]
- <instruction 1>
- <instruction 2>
...
```
The block is separated from the user's message by a blank line and a `---` delimiter.

**FR.11.10 — Proactive context reload:** When `SendAsync` detects that the session file contains history but `ClaudeSessionId` is null (e.g., after a Clear), the user's message is wrapped with `BuildContextPreamble` before being sent — without waiting for a "No conversation found" error. This ensures continuity after session clearing.

---

## Out of Scope (Initial Version)

- Session sharing or sync across machines
- Diff or branching of session history
- Deletion of session files from within the UI
- Plugin system

---

## Glossary Reference

See `/docs/glossary.md` for domain term definitions.
