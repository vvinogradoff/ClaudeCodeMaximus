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

**FR.2.3.4 — User bubble profile label:** The user message bubble header displays "You" on the left and the active profile display name (e.g. the account email, or "Default") on the right of the same row. The profile name is persisted in the session file and is restored when the session is loaded.

**FR.2.3.5 — Assistant model/effort label:** The first assistant message rendered after each user prompt displays a small italic label above the response content in the format `[<modelId>, <effort>]` (e.g. `[claude-sonnet-4-6, max]` or `[default, default]`). The label is persisted in the session file and restored on load. Subsequent assistant messages within the same turn (e.g. after a tool call) do not repeat the label. For scheduled/orchestrated turns (FR.14, FR.15), the model is recorded from the session's effective model and effort is written as `"default"` since effort is a user-facing command-bar option only.

**FR.3.3 — Session file format (extended):** The standard header line optionally carries space-delimited `key="value"` metadata after the role keyword:
- USER entries: `profile="<displayName>"`
- ASSISTANT entries: `model="<modelId>" effort="<effortLevel>"`

Old session files without metadata are parsed correctly (backward-compatible). The role keyword is always the first word after the timestamp; metadata keys never conflict with role names.

**FR.2.3.2 — Text selectability:** All rendered text in the output panel must be selectable (copy-able) by the user, including user prompts, assistant responses (both plain text and markdown mode), headings, list items, code blocks, and system messages. Text blocks display an I-beam cursor on hover and show a visible blue selection highlight when text is selected. Cross-block selection is supported: when a pointer drag crosses from one text block into another, the selection extends across all intervening blocks. The first and last blocks receive partial selection; intermediate blocks are fully selected. Ctrl+C copies the combined cross-block text. Auto-scrolling is triggered when the pointer nears the viewport edges during a drag. This works uniformly in both plain-text mode (across messages) and markdown mode (across paragraphs, headings, code blocks within and across messages). Implementation: `CrossBlockSelectionHandler` attaches tunnel-level pointer handlers to the `MessageScroller` ScrollViewer and manages `SelectionStart`/`SelectionEnd` on each `SelectableTextBlock` in range.

**FR.2.3.3 — Markdown table rendering:** Tables in markdown responses shall render with:
- Theme-aware border and header colors that adapt correctly to both dark and light themes (using Avalonia system brushes `SystemControlForegroundBaseLowBrush` / `SystemControlBackgroundChromeMediumBrush`)
- Column alignment (left, center, right) as specified in the markdown source via `:---`, `:---:`, `---:` syntax
- Alternating row shading on data rows for readability
- Header row visually distinguished with a background fill

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

**FR.4.5** Application window state (size, position, left-panel splitter position, and maximized state) is also persisted in `appsettings.json` and restored on next launch. When the window is closed while maximized, the normal (restored) bounds are preserved so un-maximizing restores the previous size and position. The saved position is validated against connected screens on startup; if the saved center point is off-screen, the window is centered on the primary display.

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

**FR.8.1 — Source codes location:** The application stores a `SourceCodesLocation` setting (the solution root directory) in `appsettings.json`. On startup, if the setting is empty, the app auto-detects it by walking up the directory tree from its base directory looking for a `*.sln` file. If found, the path is persisted. The setting is also editable in the Settings window.

**FR.8.2 — Build output detection:** On startup, the app checks whether it is running from the project's build output directory (`bin/Debug/net9.0/` under the source location). If so, an internal `IsRunningFromBuildOutput` flag is set and the self-update is disabled for that session. A warning icon (⚠) is displayed in the title bar with a tooltip explaining that the app will not be updated on restart.

**FR.8.3 — Update on exit:** On application exit, if `SourceCodesLocation` is set and the app is NOT running from build output, the app checks `bin/Debug/net9.0/` for a newer `ClaudeMaximus.dll` (by comparing file timestamps against the running copy). If a newer build is found, a PowerShell script is spawned as a visible console process to copy the updated files into the running directory after the app exits.

**FR.8.4** The copy script retries with progressive backoff delays of 1, 2, 4, 8, 16, 32, and 64 seconds (7 attempts total). If all attempts fail, the script exits silently.

**FR.8.5** If `SourceCodesLocation` is empty and auto-detection fails, the update check is silently skipped.

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

**FR.10.4 — Match highlighting:** Matched messages are scrolled into view when navigating. The search is case-insensitive and matches against message content text. While a search is active, all occurrences of the search term within message content are highlighted with a yellow background (semi-transparent `#B4FFFF00`). The currently selected match message uses an orange background (`#DCFFA500`) to distinguish it from other matches. Highlighting applies to:
- User message text (plain text)
- Assistant message text (both plain text and markdown rendering modes, including within code blocks and inline code)
- System message text

**FR.10.5 — Precise scroll positioning:** When navigating to a match, the output area scrolls to position the matched text at approximately 25% from the top of the viewport, rather than merely scrolling the message bubble into view. This ensures the matched text is visible with context above and more content below.

**FR.10.6 — Re-search on text change:** When the user modifies the search text while a search is active and presses Enter, a new search is performed with the updated text instead of navigating within the old search results.

Highlighting is implemented via `HighlightTextBlock` (extends `SelectableTextBlock`) which splits text at match boundaries and renders matches as `Run` elements with yellow background. `MarkdownView` accepts a `HighlightTerm` styled property and applies the same highlighting within its inline rendering. Highlighting is cleared when the search is dismissed.

**FR.10.5 — Dismissal behavior:** Closing the search overlay (via `X` button or `Escape` key) hides the overlay and clears any match highlighting, but does **not** clear the search text from the search box.

---

### FR.11 — Session Instruction Toolbar

The application header bar provides per-session instruction toggles that modify how the application interacts with Claude without polluting the visible conversation.

**FR.11.1 — Toolbar layout:** A horizontal row of icon toggle-buttons is positioned in the application title bar, to the right of the theme selector (day/night toggle). The toolbar contains five controls, left to right: Auto-Commit, New Branch, Auto-Document, Auto-Compact, and Clear. The buttons reflect the state of the **currently selected session** — switching sessions updates the button states. When no session is selected, the buttons are disabled.

**FR.11.2 — Instruction injection:** The application always appends hidden instructions to the prompt sent to the `claude` process (at minimum, the auto-commit ON or OFF instruction). These instructions are:
- **Not shown** in the user message bubble in the output window
- **Not stored** in the session file (the session file records only the clean user prompt)
- Appended as a clearly delimited block after the user's message text when written to `claude` stdin
- When `AgentToolsEnabled` is true, the block additionally includes the native-scheduling redirect defined in **FR.14.11** (unconditional, not tied to any toolbar toggle).

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
- **ON instruction:** `"After completing the request, update any relevant requirements documents and/or architecture documents in the project's /docs directory to reflect the changes you made. If any new domain terms were introduced or existing terms changed, also update /docs/glossary.md."`
- **Behavior:** The instruction is injected into the prompt but, like all instruction toggles, is neither shown in the output window nor stored in the session file
- **Persistence:** Per-session; stored on the session node in `appsettings.json`
- **Icon:** Document/pencil icon

**FR.11.6 — Auto-Compact toggle:**
- **Type:** One-shot toggle (auto-unsets after the compaction completes)
- **Behavior:** When ON and Claude finishes responding to the user's prompt, the application automatically sends a **separate follow-up prompt** to Claude instructing it to compact the session. The follow-up prompt is:
  The compaction prompt instructs Claude to: preserve decisions and reasoning, architecture choices, user attribution, **all URLs (full or partial)**, **all secrets/API keys/tokens/credentials/connection strings** (always preserved verbatim, no exceptions), and **file names/paths when the surrounding context is kept** (only drop a file name if the entire segment referencing it is removed); remove transient debugging steps, meta-instructions, and redundant corrections; restructure user inputs by semantic grouping (merge related follow-ups, only split on topic change); **normalize terminology per the project glossary** (`docs/glossary.md` — attached to the prompt when available); flag new terms with `[NEW TERM]` and update the glossary; and output in session file format with `[timestamp] ROLE` headers.
- **Post-compaction:** The compacted text returned by Claude replaces the session file content (rewritten, not appended). The Messages collection in the output window is also updated to reflect the compacted content. **After compaction completes, the JSONL session is automatically detached (FR.11.8).** The next prompt will use the compacted text session as context via `BuildContextPreamble` instead of `--resume`.
- **Auto-reset:** The toggle resets to OFF after the compaction prompt completes
- **Icon:** Compress/shrink icon

**FR.11.7 — Terminate Session button:**
- **Type:** Action button (not a toggle); previously called "Clear"
- **Precondition:** Only active when the current session has a live `ClaudeSessionId`. Disabled otherwise.
- **Behavior:** When clicked, shows a **confirmation dialog** asking the user to confirm the intention to terminate the session.
- **On confirm:** The JSONL session is **immediately detached** (FR.11.8) — no prompt is sent to Claude. A system message `[Session detached — next prompt will use text session as context]` appears in the Output Panel.
- **On cancel:** No action taken.
- **Effect:** The `ClaudeSessionId` is moved to `PriorClaudeSessionIds` so the JSONL remains visible in JSONL view mode but is no longer used for `--resume`. The next prompt will use the text session as context via `BuildContextPreamble`.
- **Icon:** Broom/clear icon (&#x2672;)

**FR.11.8 — JSONL Session Detachment:**
- The JSONL session is detached by: moving `ClaudeSessionId` to `PriorClaudeSessionIds` and setting `ClaudeSessionId = null`.
- Detachment occurs in two situations: (1) after Auto-Compact completes, (2) when the user confirms the Terminate Session button.
- After detachment, the JSONL file still exists and remains accessible via the JSONL view toggle, but `--resume` is no longer used.
- The next prompt after detachment proactively uses `BuildContextPreamble` to feed the stored text session history to Claude as context (FR.11.10).

**FR.11.9 — Toggle state display:** Active toggles shall be visually distinct (e.g., highlighted background or accent border) so the user can see at a glance which instructions will be injected into the next prompt.

**FR.11.10 — Instruction block format:** The instruction block is **always** appended to the user's message in `claude` stdin (since auto-commit OFF always injects "do not commit"). The format is:
```

---
[Additional instructions — do not acknowledge these in your response]
- <instruction 1>
- <instruction 2>
...
```
The block is separated from the user's message by a blank line and a `---` delimiter.

**FR.11.11 — Proactive context reload:** When `SendAsync` detects that the session file contains history but `ClaudeSessionId` is null (e.g., after detachment), the user's message is wrapped with `BuildContextPreamble` before being sent — without waiting for a "No conversation found" error. This ensures continuity after session detachment or compaction.

**FR.11.12 — Mid-run toggle corrections:** When the user toggles Auto-Commit, New Branch, Auto-Document, or Auto-Compact while Claude is actively processing a prompt (`IsBusy` is true):
- A system message is shown in the output panel: `[{ToggleName} was {enabled|disabled} for this run]`
- For Auto-Commit, New Branch, and Auto-Document: a follow-up prompt is sent to the active Claude session with the new instruction (enable) or a correction telling Claude to ignore the previous instruction (disable). These correction prompts are fire-and-forget and do not appear in the session file or UI beyond the system status message.
- For Auto-Compact: no prompt is sent (compaction happens post-response). The mid-run state is tracked and used when deciding whether to compact after the response completes. If the user enables then disables auto-compact during a single run, the final state at response completion determines behavior.

### FR.12 — Input Command Bar & Model Selection

The input area includes a collapsible command bar for runtime configuration of the Claude process parameters.

**FR.12.1 — Settings toggle button:** The right side of the input area is split vertically into two equal-height buttons: the existing **Send** button on top and a **Settings toggle** (gear icon ⚙) on the bottom. The settings toggle controls visibility of the command bar.

**FR.12.2 — Command bar layout:** The command bar appears directly beneath the text input area (within the same input border). When visible, it reduces the available height for the text input. The command bar has a subtle background matching the chrome medium brush for visual separation.

**FR.12.3 — Model selection:** The command bar contains a model selector (ComboBox). The first entry is always "Default" (no `--model` flag — uses Claude Code's own default). Remaining entries are loaded dynamically at startup from two sources:
1. **Anthropic models** — fetched from the Claude CLI using a print-mode query; cached to disk for 24 hours. A built-in fallback list (Opus, Sonnet, Haiku) is used if the fetch fails.
2. **Ollama models** — fetched from the locally running Ollama instance (`GET {OllamaBaseUrl}/api/tags`) on each startup. If Ollama is unreachable the discovery fails silently and no Ollama models appear.

Anthropic models appear first, Ollama models after. Each entry displays the true model ID (e.g. `claude-opus-4-7`, `gemma4:26b`).

**FR.12.4 — Model persistence:** The selected model is persisted as a **model ID string** (`SelectedModelId` on `DirectoryNodeModel` and `AppSettingsModel`). When the user switches sessions under a different directory, the model selector reflects that directory's saved choice. If the saved ID is not present in the current model list the selection reverts to "Default". New directories default to "Default" (empty `SelectedModelId`).

**FR.12.5 — Model flag injection:** When a non-default model is selected, the `--model <id>` flag is appended to the `claude` CLI arguments for all process spawns (user messages, context retries, compaction, and mid-run corrections).

**FR.12.6 — Profile selection:** The command bar contains a profile selector (ComboBox) to the right of the model selector. Each profile uses a separate `CLAUDE_CONFIG_DIR` directory, giving it an isolated authentication context with the Claude CLI. The dropdown displays the account email as the display text. A "Default" entry (index 0, no env var override) uses the CLI's default authentication. A "New..." entry at the bottom of the list triggers profile creation (FR.12.7).

**FR.12.7 — Profile creation flow:** When "New..." is selected in the profile dropdown:
1. A unique profile ID is generated (`profile_1`, `profile_2`, etc.) and a corresponding config directory created under `%APPDATA%\ClaudeMaximus\profiles\<profileId>\`
2. A visible console window is spawned running `claude auth login` with `CLAUDE_CONFIG_DIR` set to the profile's config directory. On Windows, a temporary `.bat` file is written to the config directory that sets the env var via `set`, uses `call` to invoke the `.cmd` wrapper (ensuring cmd.exe waits for the full auth flow including the OAuth browser callback), and ends with a `pause` so the user sees the result. The `.bat` file is cleaned up after the process exits.
3. After the console process exits, `claude auth status` is queried (with `CLAUDE_CONFIG_DIR` set) to verify authentication succeeded and retrieve the account email. If the email query fails (auth was not completed), the profile is not added and a failure message is shown.
4. The profile is added to the persisted list with the email as display name
5. The new profile is automatically selected

The dropdown selection reverts to the previous value while auth is in progress, preventing the "New..." item from being persisted as the selected index.

**FR.12.8 — Profile persistence:** Profiles are stored in `appsettings.json` as a list of `ClaudeProfileModel` objects with `ProfileId` (string, used as the config subdirectory name) and `DisplayName` (string, typically the account email). The selected profile index is persisted per working directory (stored as `SelectedProfileIndex` on each `DirectoryNodeModel`). Index 0 = Default (no env var), indices 1..N map to stored profiles. New directories default to index 0. Each profile's auth state lives in `%APPDATA%\ClaudeMaximus\profiles\<ProfileId>\`.

**FR.12.9 — Profile config dir injection:** When a non-default profile is selected, the `CLAUDE_CONFIG_DIR` environment variable is set to the profile's config directory on all spawned `claude` CLI processes (user messages, context retries, compaction, and mid-run corrections). This isolates session IDs, auth tokens, and settings per profile.

**FR.12.10 — Default profile email resolution:** On first session load, the application queries `claude auth status` (no config dir override) to retrieve the default account email. If successful, the "Default" entry in the profile dropdown is updated to show the email address instead of the generic "Default" label.

**FR.12.12 — Effort level selection:** The command bar contains an effort selector (ComboBox) with the following options:
| Index | Label | CLI Flag |
|---|---|---|
| 0 | Default | (no `--effort` flag — uses Claude Code's own default) |
| 1 | Max | `--effort max` |
| 2 | High | `--effort high` |
| 3 | Medium | `--effort medium` |
| 4 | Low | `--effort low` |

The selected effort index is persisted per working directory (`SelectedEffortIndex` on `DirectoryNodeModel`). When a non-default effort is selected, the `--effort` flag is appended to the `claude` CLI arguments for all process spawns.

**FR.12.11 — Command bar visibility persistence:** The show/hide state of the command bar (toggled via the settings gear button) is persisted per working directory in `appsettings.json` (stored as `IsCommandBarVisible` on each `DirectoryNodeModel`). When the user switches to a session under a different directory, the command bar visibility reflects that directory's saved state. New directories default to hidden.

**FR.12.13 — Local model discovery (Ollama):** On each startup the application queries the local Ollama instance at `GET {OllamaBaseUrl}/api/tags` for installed models. The base URL is configurable as `OllamaBaseUrl` in `appsettings.json` (default: `http://localhost:11434`). Discovered Ollama models are appended to the model selector after Anthropic models, using their true Ollama IDs (e.g. `gemma4:26b`). If Ollama is unreachable or returns an error the discovery fails silently — no Ollama models appear and no error is shown to the user.

**FR.12.14 — Local model routing:** When a session uses an Ollama model (identified by `ModelProvider.Ollama`), the claude CLI subprocess is launched with:
- `ANTHROPIC_BASE_URL = {OllamaBaseUrl}/v1` — routes the Claude SDK to the local Ollama endpoint
- `ANTHROPIC_AUTH_TOKEN = ollama` — dummy token accepted by Ollama
- `ANTHROPIC_API_KEY = ""` — clears any real key

Profile authentication (`CLAUDE_CONFIG_DIR`) and HTTPS proxy settings are **not** injected for Ollama-routed sessions. Ollama models are also excluded from the Claude Assist fallback chain (FR.13.14) — assist calls (title generation, semantic search) always use Anthropic models only.

---

### FR.13 — Session Import

The application supports importing Claude Code sessions that were created outside of ClaudeMaximus (e.g., from the terminal, VS Code, JetBrains, or other tools). Import converts a Claude Code JSONL session file into the ClaudeMaximus session format and adds it to the session tree, preserving the Claude session ID for immediate resumability.

**FR.13.1 — Entry point:** The context menu on Directory and Group nodes shall include an "Import Claude Session" option. This scopes the import to the working directory of the selected node.

**FR.13.2 — Session discovery:** When the import picker opens, the application derives the project slug from the node's working directory (using the same algorithm as `ClaudeSessionStatusService`) and scans `~/.claude/projects/<slug>/` for `.jsonl` files. Each file represents a discoverable session.

**FR.13.3 — Discovery data extraction:** For each discovered JSONL file, the following metadata is extracted via local file parsing (no Claude CLI calls):

| Field | Source |
|---|---|
| Session ID | File name (UUID portion before `.jsonl`) |
| Created | Timestamp of the first event in the file |
| Last used | Timestamp of the last event in the file |
| Message count | Count of `user` + `assistant` type events |
| First user prompt | Content of the first `user` event's `message.content`, truncated to 500 characters |

**FR.13.4 — Import picker dialog:** A dialog displays all discovered sessions as a scrollable list, sorted by last-used date (most recent first). Each row shows:
- Session title (initially the truncated first user prompt; replaced by a Claude-generated title when available — see FR.13.8)
- Date range (created → last used)
- Message count
- An "already imported" indicator if the session's ID matches any existing `ClaudeSessionId` in the tree (see FR.13.10)

**FR.13.5 — Multi-select:** The picker supports multi-selection via checkboxes. An "Import Selected" button triggers import for all checked sessions. Already-imported sessions cannot be checked.

**FR.13.6 — Search box:** The picker includes a search text box. Pressing Enter sends the query along with session summaries to the Claude CLI for semantic matching (see FR.13.9). Results are reordered by relevance. When the Claude CLI is unavailable, search falls back to case-insensitive substring matching against first user prompts.

**FR.13.7 — JSONL parsing:** The import service parses a Claude Code JSONL file into ClaudeMaximus session entries. Parsing rules:

| JSONL event type | Action |
|---|---|
| `user` | Extract `message.content` (string) → `USER` entry with original timestamp |
| `assistant` | Extract `message.content[]` blocks where `type` is `"text"` → concatenate text → `ASSISTANT` entry with original timestamp |
| `assistant` (tool_use blocks) | Extract `name` field from each `tool_use` block → `SYSTEM` entry formatted as `[Tool: <name>] <input summary>` with original timestamp |
| `system`, `progress`, `file-history-snapshot`, `queue-operation`, `pr-link` | Skipped — no conversation content |

Parsing is line-by-line with per-line error handling: malformed or unrecognised lines are skipped and logged. Files are opened with `FileShare.ReadWrite` to handle concurrent access from an active Claude process. The `thinking` block type within assistant events is skipped (internal reasoning, not conversation content).

**FR.13.8 — Claude-powered title generation:** When the import picker opens, the application asynchronously generates titles for all discovered sessions by calling the Claude CLI in print mode:
```
claude -p --tools "" --no-session-persistence --model <model> --output-format json
```
The model is selected using a fallback order: Haiku (preferred for speed/cost), then the user's selected model from FR.12, then no `--model` flag (CLI default). Sessions are batched (up to 20 per call) with each session identified by its ID. The prompt requests a JSON response mapping session IDs to concise 3–6 word titles. As titles arrive, they progressively replace the first-prompt preview in the picker UI. If title generation fails, the truncated first prompt remains as the display text and the default session name.

**FR.13.9 — Claude-powered search:** When the user enters a search query and presses Enter, the application sends session summaries (session IDs + truncated first prompts + message counts) along with the query to the Claude CLI (same print-mode invocation as FR.13.8). The prompt requests a JSON array of matching session IDs ranked by relevance. The picker reorders to show matches first. A spinner indicates search is in progress.

**FR.13.10 — Duplicate detection:** Before displaying the picker, the application collects all `ClaudeSessionId` values from existing session nodes across the entire tree. Discovered sessions whose ID matches an existing node are marked "already imported" in the picker and cannot be selected for import.

**FR.13.11 — Import execution:** For each selected session, the import process:
1. Parses the JSONL file into session entries (per FR.13.7)
2. Creates a new ClaudeMaximus session file (standard naming: `YYYY-MM-dd-HHmm-{6random}.txt`)
3. Writes all parsed entries to the file using the standard session file format (`[timestamp] ROLE` headers)
4. Creates a `SessionNodeModel` with: the generated title (or truncated first prompt) as `Name`, the new file as `FileName`, the working directory from the parent node, and the original Claude session ID as `ClaudeSessionId`
5. Adds the session node to the tree under the selected parent (Directory or Group)
6. Saves `appsettings.json`

**FR.13.12 — Resumability after import:** Because the imported session retains the original `ClaudeSessionId`, the session is immediately resumable via `--resume` if the JSONL file still exists in `~/.claude/projects/<slug>/`. The existing resumability check (60-second poll) applies.

**FR.13.13 — Fallback behaviour:**
- Claude CLI unavailable for title generation → truncated first prompt used as session name
- Claude CLI unavailable for search → case-insensitive substring match against first prompts
- JSONL parse errors on individual lines → skip and log, import remaining entries
- Empty JSONL file (no user/assistant events) → shown in picker but greyed out, not selectable
- No JSONL files found for the slug → picker shows empty state with explanatory message

**FR.13.14 — CLI process management for import:** All Claude CLI calls for title generation and search use timeouts, stderr capture, and structured error propagation. The process management reuses the existing `ClaudeProcessManager` infrastructure (shared `TryStartProcess` pattern). The `--tools ""` flag prevents the CLI from executing any tools. The `--no-session-persistence` flag prevents the CLI from creating session files for these utility calls. The model for assist calls follows a fallback order: Haiku (preferred), then the user's FR.12 model selection, then no `--model` flag (CLI default). This ensures the feature works regardless of the user's plan entitlements.

---

### FR.14 — Scheduled Turns

The application allows any session to schedule a future turn against itself or another session, enabling "get back to me in 30 minutes" and "check this job every hour" workflows.

**FR.14.1 — In-process MCP server:** The application hosts an HTTP MCP server bound to `127.0.0.1` (loopback only). The port is persisted in `appsettings.json` (`AgentMcpPort`); 0 means a free port is chosen at startup. The server implements the MCP JSON-RPC 2.0 protocol and is started when the application launches (if `AgentToolsEnabled` is true in `appsettings.json`).

**FR.14.2 — Per-node identity:** Each Session node has a stable `NodeId` (GUID, never changes even after session detach/compact) and a `AgentToken` (random secret, regenerated only on explicit user action). Both are persisted in `appsettings.json` and backfilled for existing sessions on first load. `NodeId` is the durable handle used by all scheduling and orchestration tools.

**FR.14.3 — Agent tool injection:** When `AgentToolsEnabled` is true, every `claude` process spawn (user turns, scheduled turns, orchestrated turns) passes `--mcp-config <per-node-path>`. The config file points to the in-process MCP server with the node's `AgentToken` in the `X-CMX-Token` request header. The MCP server maps the token to the calling node, enabling self-referential scheduling.

**FR.14.4 — Scheduling tools:**
| Tool | Required args | Optional args | Effect |
|---|---|---|---|
| `schedule_wake` | `when` (object: `inSeconds`, `at`, or `cron`) | `prompt`, `note`, `target` (nodeId) | Create a schedule. When `target` is omitted, targets the caller's own node (self-wake). |
| `list_schedules` | — | `all` (bool) | Returns the caller's schedules (or all app schedules when `all=true`). |
| `cancel_schedule` | `scheduleId` | — | Removes a schedule. |

**FR.14.5 — Self-wake identity guarantee:** When `schedule_wake` is called without a `target`, the scheduled turn is always fired against the **same** session (same `NodeId`, resumed via its current `ClaudeSessionId`). A new session node is never created.

**FR.14.6 — Schedule kinds:**
- **Delay**: fire once after N seconds from now (`when.inSeconds`)
- **At**: fire once at a specific UTC datetime (`when.at`)
- **Cron**: fire repeatedly on a cron expression (`when.cron`)

**FR.14.7 — Missed fire policy:** If the application is closed when a schedule is due, on next launch the application fires the schedule once (default policy: `FireOnce`). The per-schedule policy is configurable.

**FR.14.8 — Schedule persistence:** Schedules are stored as a `List<ScheduleModel>` in `appsettings.json`. The scheduler re-arms timers from persisted schedules on startup.

**FR.14.9 — Per-node turn serialization:** At most one turn may run concurrently per `NodeId`. If a live user-initiated turn and a scheduled turn would overlap, the scheduled turn waits for the lock to be released.

**FR.14.10 — Scheduled turn visibility:** A scheduled turn's prompt is appended to the session file as a `USER` entry, and the response as an `ASSISTANT` entry. A `SYSTEM` entry `[Scheduled: <note>]` is prepended to the prompt so the trigger is visible in the session view. The session file watcher (`FR.3.1`, `SessionViewModel`) picks up the new entries and updates the output panel for any session that is currently open.

**FR.14.11 — Native scheduling redirect:** When `AgentToolsEnabled` is true, the hidden instruction block (FR.11.2, FR.11.10) unconditionally includes a directive redirecting Claude away from the CLI's built-in scheduling tools (`ScheduleWakeup`, `CronCreate`, `CronList`, `CronDelete`) toward the ClaudeMaximus MCP scheduling tools (`mcp__cmx__schedule_wake`, `mcp__cmx__list_schedules`, `mcp__cmx__cancel_schedule`). **Rationale:** the `claude` process is terminated after each turn, so schedules registered via the CLI's built-in tools live only within that process's lifetime and are silently lost. The MCP-served scheduling tools delegate to the host-side `SchedulerService`, which persists schedules to `appsettings.json` and re-arms them on app startup (FR.14.7, FR.14.8). When `AgentToolsEnabled` is false, the redirect line is omitted (the MCP tools are unavailable, so no substitute exists).

---

### FR.15 — Agent Orchestration (Persistent Worker Sessions)

A supervisor session can spawn and control worker sessions. All workers are **persistent, resumable** session nodes in the tree — never ephemeral subagents.

**FR.15.1 — Orchestration tools:**
| Tool | Required args | Optional args | Effect |
|---|---|---|---|
| `list_sessions` | — | — | Returns summary of all sessions in the tree: nodeId, name, dirLabel, isRunning, isResumable, lastPrompt. |
| `spawn_session` | `name` | `workingDir`, `parentNodeId`, `prompt`, `model`, `group`, `schema` | Creates a **new** session node, runs the first turn, returns `{nodeId, resultText}`. `model` is tier-enforced (FR.16). |
| `send_to_session` | `nodeId`, `prompt` | `mode` (wait/async), `schema` | **Resumes** an existing session node, runs a turn. In `wait` mode returns the result synchronously. In `async` mode posts the result back to the supervisor when done. `schema` enforces structured output (FR.15.7). |
| `read_session` | `nodeId` | `lastN` | Returns the last N session file entries for the given node. |
| `stop_session` | `nodeId` | — | Cancels any running turn for the given node. |
| `set_session_model` | `nodeId`, `model` | — | Changes the model for an existing session (tier-enforced, FR.16.2). Effective from next turn. |
| `orchestrate_parallel` | `tasks` | `model`, `group`, `schema`, `budgetTokens` | Barrier fan-out: spawns one session per task, waits for all (FR.15.8). |
| `orchestrate_pipeline` | `items`, `stages` | `model`, `group`, `schema`, `budgetTokens` | Non-blocking item pipeline: one session per item through sequential stages (FR.15.8). |
| `workflow_phase` | `title` | — | Writes `[Phase: {title}]` to the supervisor session file (FR.15.9). |
| `workflow_log` | `message` | — | Writes `[Log: {message}]` to the supervisor session file (FR.15.9). |
| `get_budget` | — | — | Returns token usage and remaining budget for the caller's active orchestrations (FR.15.6). |

**FR.15.2 — New-vs-resume choice:** The supervisor explicitly chooses between `spawn_session` (creates a new tree node) and `send_to_session` (resumes an existing node). `list_sessions` provides the data needed to make that choice.

**FR.15.3 — Async mailbox:** When `send_to_session` is called with `mode=async`, on worker turn completion a follow-up turn is automatically triggered on the supervisor node with the message `[worker <name> finished] <resultText>`. This is implemented as a delay-0 schedule targeting the supervisor, reusing the scheduler engine from FR.14.

**FR.15.4 — UI thread marshaling:** All tree mutations triggered by orchestration tools (node creation, model changes) are executed on the Avalonia UI thread via `Dispatcher.UIThread.InvokeAsync`.

**FR.15.5 — Guardrails (enforced):** To prevent runaway loops, the following limits are checked at runtime — they are not advisory:
- `Constants.Agent.MaxOrchestrationDepth` (default: 5) — maximum supervisor-worker nesting depth. Checked on `spawn_session`, `orchestrate_parallel`, and `orchestrate_pipeline`; a `McpToolException` is thrown when exceeded.
- `Constants.Agent.MaxConcurrentWorkers` (default: 10) — maximum simultaneous worker turns across the entire app. `orchestrate_parallel` and `orchestrate_pipeline` acquire slots from a global `SemaphoreSlim`; if all slots are taken the spawn waits until one frees.
- `Constants.Agent.MaxTurnsPerLoop` (default: 100) — maximum turns per cron schedule before auto-cancellation.

Each `SessionNodeModel` persists `OrchestrationDepth` (int, 0 = top-level user session) and `SupervisorNodeId` (string?, null for user sessions). These are set when a session is created by an orchestration tool and used to enforce the depth limit on further spawning from that session.

A global kill-switch (`TerminateAllSessions`) cancels all active turn locks and calls `ClaudeProcessManager.TerminateAll()`.

**FR.15.6 — Orchestration Budget:** Fan-out tools (`orchestrate_parallel`, `orchestrate_pipeline`) accept an optional `budgetTokens` (int) argument. When set, an `OrchestrationBudget` is registered in memory for the duration of the orchestration, keyed by `(supervisorNodeId, orchestrationId)`. Each worker turn accrues `input_tokens + output_tokens` against the budget. Spawning a new worker when the budget is already exhausted returns a `McpToolException`. The `get_budget` tool returns current usage and remaining tokens for the caller's active orchestrations.

**FR.15.7 — Schema-Enforced Output:** `spawn_session`, `send_to_session`, `orchestrate_parallel`, and `orchestrate_pipeline` accept an optional `schema` argument (a JSON Schema object). When present, the host:
1. Appends a schema-instruction to the worker's prompt telling it to return only valid JSON matching the schema.
2. After the turn completes, validates the response using `JsonSchema.Net`.
3. On failure, sends a correction prompt up to `Constants.Agent.MaxSchemaRetries` (default: 3) times.
4. After `MaxSchemaRetries` exhausted, returns the last raw text prefixed with `[schema-invalid]`.
5. On success, returns the validated JSON string so the supervisor can parse it without ambiguity.

**FR.15.8 — Fan-Out Primitives:**

`orchestrate_parallel` — barrier fan-out:
| Arg | Required | Description |
|---|---|---|
| `tasks` | yes | JSON array of `{name, prompt}` objects |
| `model` | no | Override model for all workers (tier-enforced, FR.16) |
| `group` | no | Group name for worker sessions in the tree |
| `schema` | no | JSON Schema for structured output (FR.15.7) |
| `budgetTokens` | no | Token ceiling for this orchestration (FR.15.6) |

Creates one worker session per task. All sessions run concurrently, bounded by `MaxConcurrentWorkers`. Returns a JSON array of `{name, result, inputTokens, outputTokens}` objects when ALL workers complete. Depth enforcement applies (FR.15.5).

`orchestrate_pipeline` — non-blocking item fan-out through sequential stages:
| Arg | Required | Description |
|---|---|---|
| `items` | yes | JSON array of string items |
| `stages` | yes | JSON array of `{prompt}` objects. Stages run sequentially within one session per item. Use `{{item}}` in the first stage prompt to reference the item. |
| `model` | no | Override model for all worker sessions (tier-enforced, FR.16) |
| `group` | no | Group name for worker sessions |
| `schema` | no | Applied to the LAST stage output only |
| `budgetTokens` | no | Token ceiling |

Creates one session per item. Within each session the stages are sent as sequential turns (the session accumulates full context). All item-sessions run concurrently. Returns a JSON array of `{item, result, inputTokens, outputTokens}` when all are complete.

**FR.15.9 — Progress Tracking:**
- `workflow_phase(title)` — writes a `SYSTEM` entry `[Phase: {title}]` to the calling supervisor's session file. Visible in the session view. Returns immediately.
- `workflow_log(message)` — writes a `SYSTEM` entry `[Log: {message}]` to the calling supervisor's session file. Returns immediately.

**FR.15.10 — Per-Session Model Control:**
- `set_session_model(nodeId, model)` — changes the persisted `ModelId` on an existing session node (tier-enforced per FR.16.2; the caller's tier must be ≥ the requested model's tier). Takes effect on the next turn for that session. The supervisor can use this to switch models on running worker sessions mid-orchestration.

---

### FR.16 — Model Tier Governance

A session may only create or configure worker sessions using models at or below its own capability tier.

**FR.16.1 — Tier table:**
| Tier | Matches (substring in model ID) |
|---|---|
| 4 — Fable | `fable` |
| 3 — Opus | `opus` |
| 2 — Sonnet | `sonnet` |
| 1 — Haiku | `haiku` |
| 0 — Local | anything else (Ollama / unknown) |

Matching is case-insensitive substring search on the model ID string. If multiple substrings match, the highest-tier match wins.

**FR.16.2 — Enforcement:** The `ModelTierService.GetTier(string? modelId)` method computes the tier. Enforcement is applied in `AgentMcpServer` before processing `spawn_session`, `set_session_model`, `orchestrate_parallel`, and `orchestrate_pipeline`. A clear error is returned if `callerTier < requestedTier`. `send_to_session` does not take a model arg and therefore requires no tier check; the target session's tier is already fixed at spawn time.

**FR.16.3 — Per-session model persistence:** `SessionNodeModel` persists a `ModelId` (string, nullable). This is the model used for all turns (user-initiated, scheduled, and orchestrated) on that session. When `ModelId` is null or empty, the session inherits the effective model from the per-directory `SelectedModelId`, then the global app `SelectedModelId`.

**FR.16.4 — Caller tier resolution:** The caller node's tier is derived from its effective model: `callerNode.ModelId` → directory `SelectedModelId` → app `SelectedModelId`. An unconfigured session (all null/empty) falls back to `Constants.Agent.DefaultModelTier` (default: 2, Sonnet). This prevents unset sessions from being unable to spawn any workers.

**FR.16.5 — Turn model resolution (bug fix):** `SessionTurnService.RunTurnAsync` previously always passed `model: null` to `SendMessageAsync`, causing all headless turns (scheduled and orchestrated) to run on the CLI's default model regardless of the session's configured model. After this fix, `RunTurnAsync` resolves the effective model from `SessionNodeModel.ModelId` (falling back to directory/app selection), determines `ollamaBaseUrl` and `disableTools` from model provider, and passes these to `SendMessageAsync`.

---

### FR.17 — Token Capture in Non-Daemon Path

**FR.17.1 — Stream-JSON usage extraction:** The `result` event in the claude `stream-json` format contains a `usage` object with `input_tokens` (int), `output_tokens` (int), and optionally `total_cost_usd` (double). `ClaudeProcessManager.ParseResultEvent` extracts these into new fields on `ClaudeStreamEvent`: `InputTokens`, `OutputTokens`, `CostUsd`.

**FR.17.2 — TurnResultModel extension:** `TurnResultModel` gains `InputTokens` (int), `OutputTokens` (int), and `CostUsd` (double) fields, populated by `SessionTurnService.HandleEvent` from the result event.

**FR.17.3 — Orchestration budget accrual:** When a worker turn completes inside `orchestrate_parallel` or `orchestrate_pipeline`, its `InputTokens + OutputTokens` are accrued against the active `OrchestrationBudget` (if one was registered with `budgetTokens`).

---

### FR.18 — Status Bar (Plan Usage & Model Pricing)

A narrow status bar at the very bottom of the main window provides at-a-glance information about the selected model's pricing and the active profile's usage against plan limits.

**FR.18.1 — Placement:** A narrow border (~22 px) is docked to the very bottom of the main window, full-width, above the OS taskbar. It is always visible, independent of which session (if any) is selected.

**FR.18.2 — Left section — model pricing:** Displays the currently selected session's model ID and its Anthropic pricing in the format:
```
<modelId>  ·  in $X.XX / out $Y.YY per 1M
```
For Ollama (local) models, the pricing is replaced with `· local`. When no session is selected or "Default" model is chosen the label is empty.

**FR.18.3 — Right section — usage bars:** Two thin progress bars, stacked vertically and pixel-adjacent (no gap between them):
- **Top bar** — 5-hour rolling window utilization (`five_hour.utilization`). Resets on the schedule supplied by the API.
- **Bottom bar** — 7-day rolling window utilization (`seven_day.utilization`). Resets on the weekly schedule supplied by the API.

Each bar has a text label overlaid in the center showing utilization % and reset time: `5h: 42%  resets 10:49` / `7d: 4%  resets Thu`. The fill color is dark grey; the track is the app background.

**FR.18.4 — Usage data source:** Usage is fetched from the Anthropic OAuth usage endpoint:
```
GET https://api.anthropic.com/api/oauth/usage
Authorization: Bearer <access_token>
anthropic-beta: oauth-2025-04-20
```
The access token is read from the active profile's `.credentials.json` file (`claudeAiOauth.accessToken`). For the default profile the file is at `~/.claude/.credentials.json`; for named profiles it is at `%APPDATA%\ClaudeMaximus\profiles\<profileId>\.credentials.json`.

**FR.18.5 — Failure behaviour (token rejection):** If the endpoint returns HTTP 401 (token expired or invalid), the usage fetch stops and is not retried for the remainder of the app session. Any previously cached usage data remains visible. If no cached data is available the bars are hidden entirely (`HasUsageData = false`).

**FR.18.6 — Polling:** Usage is refreshed once immediately when the active profile changes (session switch) and then every 5 minutes while the app is running. Each poll reads the access token fresh from disk (the token may have been refreshed on disk by Claude Code in the meantime).

**FR.18.7 — Model catalog (curated static list):** The model dropdown (FR.12.3) now uses a built-in curated catalog instead of a dynamic CLI query. The catalog ships with correct model IDs, display names, and per-1M pricing:

| Model ID | Display Name | In ($/1M) | Out ($/1M) |
|---|---|---|---|
| claude-fable-5 | Fable 5 | 10.00 | 50.00 |
| claude-opus-4-8 | Opus 4.8 | 5.00 | 25.00 |
| claude-opus-4-7 | Opus 4.7 | 5.00 | 25.00 |
| claude-opus-4-6 | Opus 4.6 | 5.00 | 25.00 |
| claude-sonnet-5 | Sonnet 5 | 3.00 | 15.00 |
| claude-sonnet-4-6 | Sonnet 4.6 | 3.00 | 15.00 |
| claude-haiku-4-5-20251001 | Haiku 4.5 | 1.00 | 5.00 |

Ollama models are still discovered live (FR.12.13) and appended after the Anthropic entries. They carry no price info (`InputPricePerMillion = 0`, `OutputPricePerMillion = 0`).

---

## Out of Scope (Initial Version)

- Session sharing or sync across machines
- Diff or branching of session history
- Deletion of session files from within the UI
- Plugin system

---

## Glossary Reference

See `/docs/glossary.md` for domain term definitions.
