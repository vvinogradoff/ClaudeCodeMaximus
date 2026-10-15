# TessynDesktop — Requirements

## Project Overview

**TessynDesktop** is a cross-platform Avalonia desktop application that wraps the Claude Code CLI tool, providing a structured session management UI. It does not replace Claude Code — it hosts it, stores its history, and presents it in an organized, navigable interface.

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
| **Directory** | Real filesystem directory | No — name is derived from the path (FR.1.3) | Top-level entry; defines the working directory for all sessions beneath it |
| **Group** | `appsettings.json` only | Yes | Intermediate organisational node; no session, no working directory of its own |
| **Session** | Session file on disk; name stored in `appsettings.json` | Yes | Terminal node; represents one Claude Code conversation |

**FR.1.3 — Directory node display name:** Derived from the working directory path by a pure string rule (`IDirectoryLabelService.GetLabel`); the filesystem is not accessed and git (`.git` roots) is **not** consulted:
1. Split the path on both `\` and `/`, dropping empty segments (so trailing separators, UNC `\\` prefixes and forward-slash paths are handled). If there are no segments, the input is returned unchanged.
2. Take the last segment as the candidate. While the candidate is a **well-known project folder** name (`Constants` list: `Code`, `Docs`, `Data`; compared case-insensitively; not user-configurable), a previous segment exists, and that previous segment is not a drive designator (`X:`), move to the previous segment. Stripping repeats (`HardStop\Code\Data` → `HardStop`).
3. If the chosen segment is a drive designator (the path is a drive root), the label is that segment with a trailing `\` (`C:\` → `C:\`).
4. The label keeps the segment's original casing.
5. The label is computed at display time and is never persisted; directory node display names are never overridden by the user.
6. The directory node in the session tree shows the full working-directory path as a tooltip, since the label no longer reveals it.

| Path | Label |
|---|---|
| `C:\Projects\oDesk\HardStop` | `HardStop` |
| `C:\Projects\oDesk\HardStop\Code\` | `HardStop` |
| `C:\Projects\oDesk\HardStop\Code\Docs` | `HardStop` |
| `C:\Projects\EyeLabX\Code\EyeCura\` | `EyeCura` (only the *last* segment is replaced) |
| `C:\Code` | `Code` (parent is a drive designator) |
| `C:\` | `C:\` |
| `\\server\share\Proj` | `Proj` |
| `/home/u/proj/Code` | `proj` |

Different directories may therefore share a label (e.g. `HardStop` and `HardStop\Code` both show `HardStop`). Duplicates are prevented by path (FR.1.5), never by label. The same label is used wherever a directory is named: tree, recent-sessions dropdown, import pickers (including the "switch to original directory" target), `list_sessions` `dirLabel` (FR.15.1) and Telegram topic names (FR.21.2).

**FR.1.4 — Working directory inheritance:** All Session nodes nested under a Directory node (at any depth, through any number of Group nodes) inherit the Directory node's path as the `claude` process working directory.

**FR.1.5 — Adding nodes:**
- **Add Directory:** user selects a folder via a directory picker; a new top-level Directory node is created. If a Directory node for that path already exists, no duplicate is created (duplicates are detected by path, not by display label — FR.1.3).
- **Add Group:** user right-clicks a Directory or Group node and selects "Add Group"; a new named Group node is created as a child. Name is entered inline.
- **Add Session:** user right-clicks a Directory or Group node and selects "Add Session"; a new Session node is created as a child, a session file is created on disk (see FR.3), and a name is entered inline.
- **New Session shortcut (no tree selection):** if the user triggers "New Session" without a tree context, they are first prompted to select a working directory. If a matching Directory node exists it is used; otherwise a new Directory node is created.

**FR.1.6 — Renaming:**
- Group nodes and Session nodes may be renamed via inline editing (double-click label, or context menu → Rename).
- Directory nodes may not be renamed; their display name is always derived from the filesystem path per FR.1.3.
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

**FR.4.1** All application configuration is stored in a single `appsettings.json` file in the platform application data directory (e.g., `%APPDATA%\TessynDesktop\appsettings.json` on Windows).

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

**NR.1 — Cross-platform:** Runs on Windows, macOS, and Linux. Platform-specific code (path resolution, process launching, app data directory) is isolated behind interfaces.

**NR.2 — Performance:** Session tree is responsive for up to 500 sessions. Search via full file scan is acceptable for v1; may be slow on large sets.

**NR.3 — Durability:** Session file appends are flushed immediately. `appsettings.json` is written atomically (temp-write + rename) AND all reads/writes are serialized under a single process-wide lock (`IAppSettingsService.SyncRoot`), so concurrent Saves from multiple threads (orchestration workers, Streaming Session Mode, UI toggles) can no longer race and silently lose an update (see ISSUE.008).

**NR.4 — Architecture:** MVVM with Avalonia + ReactiveUI. One ViewModel per significant view. Models and services have no UI dependencies.

**NR.5 — Testability:** Session file I/O, tree persistence, and process management are behind interfaces; unit tests require no real `claude` process or real filesystem.

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
- For symbols: the fully qualified name including namespace is inserted (e.g., `TessynDesktop.ViewModels.SessionViewModel`)

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

**FR.8.3 — Update on exit:** On application exit, if `SourceCodesLocation` is set and the app is NOT running from build output, the app checks `bin/Debug/net9.0/` for a newer `TessynDesktop.dll` (by comparing file timestamps against the running copy). If a newer build is found, a PowerShell script is spawned as a visible console process to copy the updated files into the running directory after the app exits.

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

**FR.9.6 — Recent-dropdown hide:** The top-bar recent-sessions dropdown lists sessions active within the last 24 hours. Each row has a hide button that sets `RecentIgnoredAt` (`SessionNodeModel`) to the current time, removing the session from the dropdown until its next real activity (prompt, response, or draft edit) — tracked via `IsHiddenFromRecent` on `SessionNodeViewModel`, which auto-clears once newer activity is recorded.

**FR.9.7 — Favorite sessions:** A session can be marked as a favorite, independent of the FR.9.6 hide/recency mechanism.
- **Persistence:** `IsFavorite` (bool) on `SessionNodeModel`, persisted in `appsettings.json`.
- **Command-bar toggle:** A star toggle button in the session command bar (FR.12.2), immediately left of the Model selector. Unchecked: grey-stroked, transparent fill. Checked: black-stroked, gold/yellow fill. Clicking toggles favorite state for the active session.
- **Recent dropdown inclusion:** Favorited sessions always remain in the top-bar recent-sessions dropdown regardless of the 24-hour recency window (FR.9.6) and regardless of hidden state. Sort order is unchanged — most-recently-active first — so a favorite with no recent activity sinks to the bottom of the list rather than being dropped.
- **Recent-dropdown row affordance:** Each dropdown row shows exactly one of two icons on its right side: a gold/black star if the session is favorited (clicking un-favorites it, after which the hide icon from FR.9.6 takes its place), or the hide icon (⦸) if not favorited (clicking hides it per FR.9.6).
- **Tree and Recent-tab display:** The session tree and the Recent-tab flat list each show a display-only gold/black star at the far right of the session row's top line, above the last-activity date/time, when the session is favorited. The existing draft indicator (✏, FR — "has unsent text") remains immediately right of the session name and is unaffected.
- **Hide icon replacement:** The recent-dropdown hide icon (FR.9.6) is changed from the eye pictogram (👁) to a monochrome bold-slashed-circle glyph (⦸, U+29B8), for visual consistency with the rest of the flat, non-emoji iconography used elsewhere in the command bar.

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

**FR.11.2 — Instruction injection:** The application appends a session-preferences block to the prompt sent to the `claude` process whenever there is at least one active instruction to convey (Auto-Commit in the Commit or Do-not-commit state, New Branch, Auto-Document, or the FR.14.11 redirect). When Auto-Commit is in its "No preference" state and no other toggle is active, **no block is appended at all**. These instructions are:
- **Not shown** in the user message bubble in the output window
- **Not stored** in the session file (the session file records only the clean user prompt)
- Appended as a clearly delimited block after the user's message text when written to `claude` stdin, worded as a genuine preamble (not as a directive to conceal itself from the model)
- When `AgentToolsEnabled` is true **and** the session is in One-shot Mode, the block additionally includes the native-scheduling redirect defined in **FR.14.11**. As of **FR.19.10**, the redirect is **not** emitted when the session is in Streaming Session Mode (FR.19) — its rationale (the CLI process dies between turns) does not hold for a persistent process. The `NoWorkflowTool` redirect (session-orchestration tools) is unconditional on mode and continues to be emitted in both.

**FR.11.3 — Auto-Commit toggle (tri-state):**
- **Type:** Sticky three-state toggle (persists across prompts until the user changes it). Clicking cycles **Do-not-commit → Commit → No preference → Do-not-commit**.
- **Commit instruction:** `"Once you have completed the request, commit all your changes to git with a concise commit message."`
- **Do-not-commit instruction:** `"Do not commit any changes to git."`
- **No preference:** No commit-related instruction is emitted at all — the block is silent on committing (this state removes what was previously the only always-injected instruction).
- **Persistence:** Per-session; toggle state (`true` = Commit, `false` = Do-not-commit, absent/`null` = No preference) stored on the session node in `appsettings.json` so it survives app restarts. Different sessions may have different auto-commit states. New sessions default to Do-not-commit (`false`).
- **Icon:** Three distinct glyph/color states for Commit, Do-not-commit, and No preference (see FR.11.9).

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
- **Empty-prompt shortcut:** Auto-Compact can be triggered with no input text — submitting with the toggle ON and an empty message skips the normal turn entirely and goes straight to the compaction prompt, resetting the toggle afterward.
- **Behavior:** When ON and Claude finishes responding to the user's prompt, the application automatically sends a **separate follow-up prompt** to Claude instructing it to compact the session. The follow-up prompt is:
  The compaction prompt instructs Claude to: preserve decisions and reasoning, architecture choices, user attribution, **all URLs (full or partial)**, **all secrets/API keys/tokens/credentials/connection strings** (always preserved verbatim, no exceptions), and **file names/paths when the surrounding context is kept** (only drop a file name if the entire segment referencing it is removed); remove transient debugging steps, meta-instructions, and redundant corrections; restructure user inputs by semantic grouping (merge related follow-ups, only split on topic change); **normalize terminology per the project glossary** (`docs/glossary.md` — attached to the prompt when available); flag new terms with `[NEW TERM]` and update the glossary; and output in session file format with `[timestamp] ROLE` headers.
- **Transcript is embedded directly, not resumed:** The compaction prompt includes the full transcript to compact inline (`SESSION TRANSCRIPT TO COMPACT:` section) — USER/ASSISTANT entries from the text Session File since the last `[COMPACTION]` marker (or the whole file if none exists yet), each formatted as `[timestamp] ROLE` plus content. The compaction call itself is **stateless** (`sessionId: null`, no `--resume`) — Claude gets all conversation content from the embedded transcript, never from resuming a prior Claude session. This is deliberate: because a session's Claude Code JSONL history can be split or incomplete across Cross-Profile Session Adoption (FR.11.13) hops or mixed Anthropic/local-Ollama turns (Model Provider), the plain-text Session File — always complete regardless of profile or model-provider switches — is the only reliable source for what to compact. `--resume` is not used for compaction and no cross-profile lookup is needed here (unlike interactive sends and headless turns, which still need it — see FR.11.13).
- **Nothing-to-compact guard:** If the built transcript is empty (e.g. the empty-prompt shortcut fires before any turn exists yet), no CLI call is made — a `[Nothing to compact]` system message is shown instead.
- **Post-compaction:** The compacted text returned by Claude replaces the session file content (rewritten, not appended). The Messages collection in the output window is also updated to reflect the compacted content. **After compaction completes, the JSONL session is automatically detached (FR.11.8).** The next prompt will use the compacted text session as context via `BuildContextPreamble` instead of `--resume`.
- **Failure safety:** The compaction turn's stream events are monitored for any error (e.g. an invalid-credentials/reauth failure, a non-zero `claude` exit code, or any other error event) as well as for a wholly empty result. If either condition occurs, the session file and JSONL are **left completely untouched** — no rewrite, no detach — even if some assistant text happened to be captured before the error. A system message (`[Compaction failed — session left unchanged: <error>]` or `[Compaction returned no content — session left unchanged]`) is shown in the Output Panel so the failure is visible instead of silent. If the error is an invalid-credentials failure, `HasAuthError` is set so the Reauthenticate button appears, matching the behavior of a normal turn's auth failure (FR.12.16).
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
- The next prompt after detachment proactively uses `BuildContextPreamble` to feed the stored text session history to Claude as context (FR.11.10), unless Cross-Profile Session Adoption (FR.11.13) locates the JSONL under a different profile first — that path re-attaches the session and resumes it natively instead.

**FR.11.9 — Toggle state display:** Active toggles shall be visually distinct (e.g., highlighted background or accent border) so the user can see at a glance which instructions will be injected into the next prompt. Auto-Commit, being three-state, shows three distinct visuals — one each for Commit, Do-not-commit, and No preference — so the current state is unambiguous at a glance.

**FR.11.10 — Instruction block format:** The instruction block is appended to the user's message in `claude` stdin whenever at least one instruction is active (see FR.11.2 — it is omitted entirely when Auto-Commit is "No preference" and no other toggle is active). The format is:
```

---
Session preferences — apply these if relevant to our work here:
- <instruction 1>
- <instruction 2>
...
```
The block is separated from the user's message by a blank line and a `---` delimiter. The preamble reads as ordinary guidance to the model rather than an instruction to conceal itself from the user.

**FR.11.11 — Proactive context reload:** When `SendAsync` detects that the session file contains history but `ClaudeSessionId` is null (e.g., after detachment), the user's message is wrapped with `BuildContextPreamble` before being sent — without waiting for a "No conversation found" error. This ensures continuity after session detachment or compaction.

**FR.11.12 — Mid-run toggle corrections:** When the user toggles Auto-Commit, New Branch, Auto-Document, or Auto-Compact while Claude is actively processing a prompt (`IsBusy` is true):
- A system message is shown in the output panel: `[{ToggleName} was {enabled|disabled|set to no preference} for this run]`
- For New Branch and Auto-Document: a follow-up prompt is sent to the active Claude session with the new instruction (enable) or a correction telling Claude to ignore the previous instruction (disable). These correction prompts are fire-and-forget and do not appear in the session file or UI beyond the system status message.
- For Auto-Commit (tri-state): a follow-up correction prompt is sent reflecting whichever of the three states was just selected — Commit, Do-not-commit, or a correction telling Claude to disregard any earlier commit-related instruction (No preference).
- For Auto-Compact: no prompt is sent (compaction happens post-response). The mid-run state is tracked and used when deciding whether to compact after the response completes. If the user enables then disables auto-compact during a single run, the final state at response completion determines behavior.

**FR.11.13 — Cross-profile session adoption:** Each profile stores its Claude Code session JSONL files under its own isolated `CLAUDE_CONFIG_DIR` (`%AppData%\TessynDesktop\profiles\profile_N\projects\<slug>\`, or `~\.claude\projects\<slug>\` for the Default profile). A session created under one profile is therefore invisible to `--resume` when a different profile is active, even though its JSONL still exists — Claude Code reports "No conversation found" and, for Auto-Compact specifically, produces nothing to compact.
- **When it runs:** Before any `--resume` attempt that carries a `ClaudeSessionId` — interactive sends (FR.11.11's proactive path) and headless/orchestrated turns (FR.14.9). Auto-Compact does **not** use this — its compaction call is stateless and embeds the transcript directly instead of resuming anything (FR.11.6).
- **Lookup:** The application derives the project slug from the working directory (same algorithm as FR.13.2) and first checks whether `<slug>/<ClaudeSessionId>.jsonl` already exists under the currently active profile's session store. If not, it searches every other known profile — the Default profile's `~/.claude` plus every directory under the profiles root, including profiles marked removed (`_rem` suffix, see the Removed Profile glossary entry) — for a matching file.
- **Adoption is a move, not a copy:** When found under a different profile, the JSONL file is **moved** into the active profile's session store (creating the destination `projects/<slug>/` directory if needed). The session becomes owned by whichever profile most recently used it; no stale duplicate is left behind under the original profile, since a copy would immediately go stale the moment the active profile continues the conversation.
- **Fallback:** Only when the session cannot be found under any profile does the application fall through to the existing `BuildContextPreamble` text-reconstruction path (FR.11.11 for interactive turns).
- **Scope:** `ClaudeSessionId` itself is unchanged by adoption — only its backing JSONL file is relocated. `PriorClaudeSessionIds`, session titles, and all other node state are unaffected.

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
1. A unique profile ID is generated (`profile_1`, `profile_2`, etc.) and a corresponding config directory created under `%APPDATA%\TessynDesktop\profiles\<profileId>\`
2. A visible console window is spawned running `claude auth login` with `CLAUDE_CONFIG_DIR` set to the profile's config directory. On Windows, a temporary `.bat` file is written to the config directory that sets the env var via `set`, uses `call` to invoke the `.cmd` wrapper (ensuring cmd.exe waits for the full auth flow including the OAuth browser callback), and ends with a `pause` so the user sees the result. The `.bat` file is cleaned up after the process exits.
3. After the console process exits, `claude auth status` is queried (with `CLAUDE_CONFIG_DIR` set) to verify authentication succeeded and retrieve the account email. If the email query fails (auth was not completed), the profile is not added and a failure message is shown.
4. The profile is added to the persisted list with the email as display name
5. The new profile is automatically selected

The dropdown selection reverts to the previous value while auth is in progress, preventing the "New..." item from being persisted as the selected index.

**FR.12.8 — Profile persistence:** Profiles are stored in `appsettings.json` as a list of `ClaudeProfileModel` objects with `ProfileId` (string, used as the config subdirectory name) and `DisplayName` (string, typically the account email). The selected profile index is persisted per working directory (stored as `SelectedProfileIndex` on each `DirectoryNodeModel`). Index 0 = Default (no env var), indices 1..N map to stored profiles. New directories default to index 0. Each profile's auth state lives in `%APPDATA%\TessynDesktop\profiles\<ProfileId>\`.

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
- `ANTHROPIC_BASE_URL = {OllamaBaseUrl}` — the **bare host, no `/v1` suffix** — routes the Claude SDK to the local Ollama endpoint. The Anthropic SDK appends `/v1/messages` to this value itself; including `/v1` in `OllamaBaseUrl` here would double the path segment and break routing. (`ClaudeProcessLauncher.TryStartProcess` sets this via `ollamaBaseUrl.TrimEnd('/')`.)
- `ANTHROPIC_AUTH_TOKEN = ollama` — dummy token accepted by Ollama
- `ANTHROPIC_API_KEY = ""` — clears any real key

Profile authentication (`CLAUDE_CONFIG_DIR`) and HTTPS proxy settings are **not** injected for Ollama-routed sessions. Ollama models are also excluded from the Claude Assist fallback chain (FR.13.14) — assist calls (title generation, semantic search) always use Anthropic models only. Because the claude CLI is still the process being spawned, an Ollama-routed turn hands the model the CLI's full native tool surface via the Anthropic `/v1/messages` protocol (Ollama honors the `tools` parameter) — see FR.12.21 for the profile that curates this surface down for weaker local models.

**FR.12.15 — Mid-flight model change (not supported — design decision):** Changing the model or effort dropdown while a Claude response is in progress does **not** affect the currently-running invocation. The model and effort are captured at send time and passed to the CLI process; there is no mechanism in the Claude CLI protocol to hot-swap the model mid-stream. The new selection takes effect on the next user message automatically. Real mid-flight switching would require killing the running process and re-sending the same prompt, discarding the partial response — this is not implemented. Users who need to abort and restart with a different model should use the **Stop** button, then change the model and resend.

**FR.12.16 — Auth failure detection:** When a `result` or `system` error event's content contains the substring `"Invalid authentication credentials"` (the Claude CLI's 401 error text), the session sets `HasAuthError = true` in addition to normal error handling (FR.12.16 does not change how the error text itself is displayed — it only raises a flag). The flag is cleared when: a subsequent request succeeds, the user switches the profile selection, or a Re-authenticate/Remove action (FR.12.17/FR.12.18) completes.

**Profile actions flyout:** Re-authenticate, Remove, Rename, and Delete (FR.12.17–12.19) live in a flyout `Popup`, not directly in the command bar — they were crowding it. The **"Profile:"** label is a toggle (`Profile: ▾`); clicking it opens a small popup below the label containing the (up to four) applicable icon buttons, sized compactly (`Padding="4,2"`). The popup closes when the label is clicked again (no click-outside light-dismiss, to avoid the toggle's checked state desyncing from the popup's open state — mirrors the existing `AutocompletePopup` pattern elsewhere in `SessionView.axaml`).

**FR.12.17 — Re-authenticate button:** When `HasAuthError` is true, a **key icon (🔑)** button appears in the profile actions flyout. Clicking it re-runs the same interactive auth login flow used for profile creation (FR.12.7), targeting the **currently selected profile's** config directory (or no `CLAUDE_CONFIG_DIR` override, if Default is selected). On success (`claude auth status` returns an email), `HasAuthError` is cleared and a confirmation message is appended to the session. On failure/cancellation, `HasAuthError` remains set and a failure message is appended. (Currently shown unconditionally rather than gated on `HasAuthError`, per an explicit interim request — see ISSUE.004.)

**FR.12.18 — Remove profile button:** When `HasAuthError` is true **and** a non-Default profile is selected, a **✕** button appears in the flyout alongside Re-authenticate. Clicking it asks for confirmation (destination directory is preserved, not deleted — see below), then:
1. Removes the profile's entry from `AppSettingsModel.Profiles` (the app no longer lists or selects it).
2. Renames the profile's config directory on disk from `<ProfilesRoot>\<ProfileId>\` to `<ProfilesRoot>\<ProfileId>_rem\` (e.g. `profile_4` → `profile_4_rem`). The directory itself, its Claude sessions, and its projects are **not deleted** — only marked removed and taken out of the active profile list, since they may hold sessions/projects still worth recovering later. If a directory with the `_rem` suffix already exists (e.g. from a previous removal that was never cleaned up manually), a numeric suffix is appended (`_rem2`, `_rem3`, ...) to avoid collisions.
3. Resets the profile selector to "Default" (index 0) and clears `HasAuthError`.

The Default profile cannot be removed (the Remove button never appears while Default is selected, even if `HasAuthError` is true).

**FR.12.19 — Profile Rename/Delete buttons:** Two icon buttons, **✏ Rename** and **🗑 Delete**, appear in the flyout whenever `CanManageSelectedProfile` is true (a stored, non-Default profile is currently selected — independent of `HasAuthError`, unlike FR.12.18's Remove button). They act on whichever profile is currently selected in the Profile dropdown. All four flyout buttons carry a `ToolTip.Tip` label for discoverability, since they're icon-only.
1. **Rename** prompts for a new display name (pre-filled with the current one) via the same input dialog used elsewhere in the app. Confirming updates the profile's stored `DisplayName` only — the `ProfileId` and its config directory are untouched.
2. **Delete** prompts for confirmation, then performs the same removal as FR.12.18 (removes the profile from `AppSettingsModel.Profiles`, renames its config directory with a `_rem` suffix, collision-safe). If the deleted profile was the one currently selected, selection falls back to Default; if a different (unselected) profile was deleted, the current selection is preserved (adjusting its index for the shift). Remove (FR.12.18) and Delete share the same confirm-then-delete code path (`SessionView.axaml.cs`'s `ConfirmAndDeleteSelectedProfileAsync`) — both act on the same underlying operation, just gated by different visibility conditions.
3. **Default entry styling:** the "Default" entry is prefixed `"[Sys] "` (e.g. `[Sys] user@example.com`) and rendered in dark grey (`ProfileListItemModel.IsSystem`), while stored profiles render in the normal (black) text color — visually distinguishing the base, non-isolated claude auth identity from actual stored profiles, since both can otherwise show the same account email.

**Design note:** FR.12.19 originally implemented Rename/Delete as a right-click context menu on individual Profile dropdown entries. This was replaced with plain command-bar buttons acting on the current selection, because a `ContextMenu` opened from inside a `ComboBox`'s own dropdown `Popup` is a documented fragile pattern in Avalonia (nested popups) — buttons avoid that risk entirely and reuse the same proven pattern as the Re-authenticate/Remove buttons.

**FR.12.20 — Background ("small-fast") model routing:** The `claude` CLI uses a secondary, smaller model internally for background/utility work, distinct from the primary conversational model, controlled by the `ANTHROPIC_SMALL_FAST_MODEL` environment variable. Tessyn sets this only for Anthropic-routed sessions:
- **Anthropic-routed sessions:** `ANTHROPIC_SMALL_FAST_MODEL` is set to the curated catalog's Haiku entry (FR.18.7 — currently `claude-haiku-4-5-20251001`), taking background-task load off the session's primary selected model (Sonnet/Opus/Fable).
- **Ollama-routed sessions:** `ANTHROPIC_SMALL_FAST_MODEL` is deliberately **not** set. Setting it to a distinct, smaller local model would require Ollama to keep a second model resident in memory alongside the primary session model, increasing local resource pressure for hardware that already runs a single large local model (e.g. `gemma4:26b`/`gemma4:31b`) at full size for free — the opposite of the variable's intended effect. Ollama-routed sessions therefore continue to use only the single model selected in the command bar (FR.12.5) for all CLI-internal work.

**FR.12.21 — Local Tool Profile:** A per-directory selection of exactly which native tools and MCP servers the claude CLI subprocess is given, applied **only** when the directory's currently selected model is a local (`ModelProvider.Ollama`) model. For Anthropic-routed sessions, launch behavior is unchanged (full native tool set + full `--mcp-config` merge per FR.20) and this profile has no effect.

**Rationale:** Local Ollama models already run through the claude CLI (FR.12.13/FR.12.14) and are handed the CLI's full ~30-tool native surface plus the full merged MCP server set. Empirically, a weak, quantized, or abliterated local model gets confused by that large a tool surface and mis-calls tools (e.g. inventing a `read_file` tool instead of calling `Read`), even though the underlying Ollama-served Anthropic `/v1/messages` protocol correctly returns valid `tool_use` blocks. A smaller, curated tool surface measurably improves local-model tool-call reliability without touching Anthropic-routed sessions at all.

- **Data model:** `LocalToolProfileModel` — `List<string> NativeTools`, `bool IncludeCmxMcp`, `List<string> IncludedExternalMcpServerIds`. Persisted as a nullable `LocalToolProfile` property on `DirectoryNodeModel` in `appsettings.json`. `null` means "use the default lean profile" — existing directories with no saved profile are unaffected and behave exactly as before this feature (backward-compatible).
- **Default lean profile** (used whenever `LocalToolProfile` is `null`): `NativeTools = [Read, Write, Edit, Bash, Glob, Grep, LS]`; `IncludeCmxMcp = false`; no external MCP servers. Git operations are still available through `Bash`.
- **Selectable native tool catalog:** `Read`, `Write`, `Edit`, `Bash`, `Glob`, `Grep`, `LS` (the core set, included in the default lean profile) plus optional `WebFetch`, `WebSearch`, `NotebookEdit`, `TodoWrite`, `Task` (available to add, not included by default).
- **CLI mapping — native tools:** the selected native tool names are joined space-separated and passed as `--tools "<names>"`. An empty selection passes `--tools ""` (no tools); the claude CLI treats `"default"` as "all tools" but the Local Tool Profile never emits that literal — it always enumerates the selected names explicitly.
- **CLI mapping — MCP:** the `--mcp-config` file built for a local turn includes only the `cmx` in-process server (if `IncludeCmxMcp` is true) plus whichever `ExternalMcpServerModel` entries match `IncludedExternalMcpServerIds` (FR.20). `--strict-mcp-config` is additionally passed for local turns so no ambient/user-level MCP configuration leaks in beside the curated set. If neither `IncludeCmxMcp` nor any external server is selected, `--mcp-config` is omitted entirely for that turn.
- **Tool-incapable local models unaffected:** an Ollama model with `SupportsTools = false` (FR.12.13) still receives `--tools ""` and no `--mcp-config` regardless of any configured Local Tool Profile — the profile only governs *which* tools a tool-capable local model receives, not whether tool-incapable models are given tools at all.

**FR.12.22 — Command-bar selector compaction & left-truncated model label:** The Model, Profile, and Effort selector boxes in the command bar are each fixed to their own compact width via a local `Width` override on the shared `.cmxCompact` style: **Model ~120px, Profile ~100px, Effort ~60px** (Model needs the most room to keep a useful suffix legible after left-truncation below; Effort's option labels are short enough that 60px rarely truncates at all). Each selector's own dropdown flyout is unaffected by this and still opens at full/natural content width — only the closed box is narrowed.

- **Left-truncated model label:** unlike Profile and Effort (whose values truncate normally, trailing-edge, via Avalonia's built-in `TextTrimming`), the Model box's selected value is right-aligned and truncated from the **left** — e.g. `claude-opus-4-8` displays as `…pus-4-8` — so the distinctive trailing suffix of a model ID (the part that actually distinguishes it from sibling models) stays visible instead of the shared `claude-` prefix. Implemented via `TextTrimming="LeadingCharacterEllipsis"`, sized to the box's actual available width rather than a fixed character count.
- **`:latest` tag stripped from the displayed model text only:** Ollama tags commonly carry a trailing `:latest` (e.g. `qwen3.6:35b-a3b:latest`). Both the Model box's closed-box label and its open dropdown list strip this trailing `:latest` from the text shown to the user — so the example above displays as `qwen3.6:35b-a3b` — via display converters. This is purely cosmetic: the underlying model ID string is completely unchanged everywhere it is actually used — the `--model` CLI flag, `SelectedModelId` persistence, and `LocalToolProfileResolver`'s model matching all continue to see the full ID including `:latest`.
- **Local Tool Profile control relocated (amended by FR.12.23):** the per-directory Local Tool Profile (FR.12.21) is no longer a selector in the Model/Profile/Effort row. It is now an icon-style toggle button — sized to match the existing 26×26 icon buttons (Chrome, Streaming Session Mode, Session Completion Sound) — positioned immediately to the right of the Session Completion Sound button (FR.22). It remains visible only when the directory's currently selected model is local (`ModelProvider.Ollama`), and remains disabled (with the existing tooltip explaining why) when the selected local model does not support tool calling. Its click/press-and-hold interaction and popup content are specified in FR.12.23.
- **No drop-down arrow chrome:** the three `.cmxCompact` boxes also suppress FluentTheme's built-in drop-down arrow glyph and let the closed-box content span into the reclaimed space — clicking anywhere on the closed box still opens the dropdown, so the arrow was pure decoration and, on the 60px Effort box, ate about half the available width.

**FR.12.23 — Local Tools master enable/disable:** The relocated Tools icon button (FR.12.22) is not just a way to open the tool/MCP checklist — it is itself a master on/off switch for whether the Local Tool Profile applies at all.

- **FR.12.23.1 — `Enabled` flag & resolver bypass:** `LocalToolProfileModel` gains an `Enabled` (bool) field, defaulting to `true`. Profiles persisted before this field existed deserialize with `Enabled = true` (backward-compatible — no behavior change for existing saved profiles). When `Enabled` is `false` for a directory, `LocalToolProfileResolver.Resolve` returns `null` for that directory's local-model turns **regardless of the saved `NativeTools`/`IncludeCmxMcp`/`IncludedExternalMcpServerIds` selection** — the local model is then given the same unrestricted native tool set and full FR.20-merged `--mcp-config` an Anthropic-routed session gets. The saved selection itself is not cleared or modified by disabling — it is preserved untouched and automatically reapplied the next time the master switch is turned back on.
- **FR.12.23.2 — Interaction model:** the relocated Tools button (FR.12.22) has two distinct gestures:
  - **Short click** toggles the master switch (`Enabled`) and persists the new value immediately — no separate Save step, matching every other command-bar toggle in this app.
  - **Press-and-hold (~450ms)** opens a popup checklist instead of toggling the master switch — the same native-tools + `cmx` + registered-external-MCP-server checkbox list described in FR.12.21/FR.12.22's prior selector design.
  - While the popup is open: clicking away from it, or clicking the Tools button again, closes the popup **without** changing the master switch.
  - Clicking a tool's checkbox, or its label text, toggles that tool's inclusion in the saved selection and persists immediately; the popup stays open afterward so further edits can be made in the same interaction.
- **FR.12.23.3 — Visual state:** the button is shown visibly dimmed (reduced opacity) when the master switch is off, and at full opacity when on — consistent with the existing convention (FR.11.9) that active vs. inactive instruction toggles must be visually distinguishable at a glance.

**FR.12.24 — OAuth refresh-race mitigation (fixes a confirmed intermittent auth failure):** Two or more `claude` CLI processes sharing one `CLAUDE_CONFIG_DIR` (most commonly the Default profile, which uses no override and so shares the literal `~/.claude` credential store machine-wide, and orchestrated children that inherit their supervisor's profile — see `docs/issues/ISSUE.002.orchestrated-child-profile-inheritance.md`) can race a near-expiry OAuth token's refresh: whichever process's refresh request lands second presents an already-rotated refresh token and the CLI reports `"OAuth session expired and could not be refreshed"` — a transient failure, not an actually-revoked account. Two mitigations:

- **FR.12.24.1 — Per-config-dir spawn serialization:** `ClaudeProcessLauncher.Start` — the single chokepoint for One-shot Mode, Streaming Session Mode, and `RunPrintModeAsync` spawns — serializes the `Process.Start` call itself with a lock keyed by `profileConfigDir` (Default/null spawns share one key). The lock is held only around the launch call, not the process's subsequent lifetime, so turns sharing a profile still run fully in parallel once started; it narrows the window in which two launches' startup-time token checks can interleave.
- **FR.12.24.2 — Single silent auto-retry:** `TurnController` (interactive one-shot and Streaming Session Mode) and `SessionTurnService` (headless one-shot and the FR.19.13 streaming-redirect path) detect `Constants.Auth.OAuthRefreshRaceMarker` on a `result`/`system` error event, suppress the file write and `HasAuthError`/FR.12.16 flag for that event, wait `Constants.Auth.OAuthRefreshRaceRetryDelayMs` (1.5s), and retry the identical turn exactly once. A second failure with the same marker is not retried again — it is not distinguished from any other auth failure and falls through to normal FR.12.16 handling. Full incident writeup in `docs/issues/ISSUE.010.oauth-refresh-token-race.md`.

**FR.12.25 — Local model Claude Code compatibility check:** Ollama's `tools` capability flag (FR.12.13 `SupportsTools`) is not enough to know whether a local model can actually run under the claude CLI. The CLI places a `role: "system"` message *inside* `messages[]` (the environment block, after the first user turn). Ollama models that render through their own built-in Go **renderer** (Modelfile `RENDERER <name>`, e.g. all `ollama.com/library` Qwen 3.5/3.6 and Gemma 4 builds) accept this. Models pulled from `hf.co/...` (and many community re-uploads) instead render through the GGUF-embedded Jinja chat template, and the stock Qwen 3.x template raises `System message must be at the beginning`. Ollama then returns HTTP 500 on **every** turn, even with tools disabled. Background and evidence: `docs/business-analysis/Local-Models_Agentic-Coding-Findings.md`.

- **FR.12.25.1 — Static classification (always, free):** `OllamaModelService` already calls `POST /api/show` per model during discovery. From the same response it also derives `ClaudeCodeCompatibility`:
  - `Compatible` — the `modelfile` declares a `RENDERER`, **or** its `TEMPLATE` is a real Go template (anything other than the passthrough `{{ .Prompt }}`).
  - `Incompatible` — passthrough `TEMPLATE {{ .Prompt }}`, no `RENDERER`, and the Jinja `template` contains a `raise_exception(` guard on a system-message position (matched on the `System message must be at the beginning` text).
  - `Unknown` — anything else. Treated as compatible: no warning, nothing blocked.
  No model load and no inference are involved.
- **FR.12.25.2 — UI signal:** in the Model dropdown, `Incompatible` local models show a ⚠ marker. Its tooltip says the model's chat template rejects Claude Code's request layout and points to FR.12.26 repair when available. The model stays selectable; the indicator only warns.
- **FR.12.25.3 — On-demand agent probe:** a "Test with Claude Code" action on a local model sends one Claude-Code-shaped request directly to `{OllamaBaseUrl}/v1/messages`: a top-level system prompt, a mid-`messages` system message, a single `Read` tool, and a prompt that needs that tool. The result is one of:
  - `Fails` — a 5xx or template error.
  - `NoToolCall` — 200, but the model answered in prose.
  - `CallsTools` — the response contains a well-formed `tool_use` for `Read`.
  The probe loads the model, so it runs only when the user asks, never automatically. The result is cached in memory per model digest and shown in the same tooltip.

**FR.12.26 — One-click renderer repair for local models:** For an `Incompatible` (FR.12.25.1) model whose `/api/show` `model_info["general.architecture"]` appears in a curated map of Ollama built-in renderers (`Constants.Ollama.RendererByArchitecture`, initially `qwen35 → qwen3.5`, `gemma4 → gemma4`; extended as verified), the ⚠ tooltip/context offers **"Create Claude Code–compatible copy"**.
- Tessyn calls `POST {OllamaBaseUrl}/api/create` with `{ "model": "<alias>", "from": "<original id>", "renderer": "<r>", "parser": "<r>" }`. This is verified to work on Ollama 0.32.x, and the copy reuses the original's weight blobs, so it takes no extra disk space.
- The original model is never modified or deleted.
- The alias is `<repo-or-base-name>-cc:<original tag>` (e.g. `hf.co/HauhauCS/Qwen3.5-9B-Uncensored-HauhauCS-Aggressive:Q8_0` → `qwen3.5-9b-uncensored-hauhaucs-aggressive-cc:q8_0`), lower-cased and collision-checked.
- On success the model list is refreshed and the new alias is selected for the current directory.
- Architectures not in the map get no repair offer, only the FR.12.25.2 warning.

---

### FR.13 — Session Import

The application supports importing Claude Code sessions that were created outside of TessynDesktop (e.g., from the terminal, VS Code, JetBrains, or other tools). Import converts a Claude Code JSONL session file into the TessynDesktop session format and adds it to the session tree, preserving the Claude session ID for immediate resumability.

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

**FR.13.7 — JSONL parsing:** The import service parses a Claude Code JSONL file into TessynDesktop session entries. Parsing rules:

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
The model is selected using a fallback order: Haiku (preferred for speed/cost), then the user's selected model from FR.12, then no `--model` flag (CLI default). Sessions are batched (up to 20 per call) with each session identified by its ID. The prompt requests a JSON response mapping session IDs to concise 3–6 word titles. As titles arrive, they progressively replace the first-prompt preview in the picker UI. If title generation fails, the truncated first prompt remains as the display text and the default session name. **Titles are persisted** (Stage 6, `IClaudeSessionIndexService`) keyed by the Claude CLI session ID, so a session already titled in a previous import-picker session is never regenerated — reopening the picker (or a future app run) reuses the stored title at zero additional CLI cost.

**FR.13.9 — Claude-powered search (current project):** When the user enters a search query and presses Enter with global search off, the application sends session summaries (session IDs + truncated first prompts + message counts) along with the query to the Claude CLI (same print-mode invocation as FR.13.8), scoped to the sessions already discovered under the selected source directory. The prompt requests a JSON array of matching session IDs ranked by relevance. The picker reorders to show matches first. A spinner indicates search is in progress.

**FR.13.10 — Duplicate detection:** Before displaying the picker, the application collects all `ClaudeSessionId` values from existing session nodes across the entire tree. Discovered sessions whose ID matches an existing node are marked "already imported" in the picker and cannot be selected for import.

**FR.13.11 — Import execution:** For each selected session, the import process:
1. Parses the JSONL file into session entries (per FR.13.7)
2. Creates a new TessynDesktop session file (standard naming: `YYYY-MM-dd-HHmm-{6random}.txt`)
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

**FR.13.15 — Cross-project search index (Stage 6, `IClaudeSessionIndexService`):** Toggling "Global" search (`IsGlobalSearch`, always available — no external dependency) searches message content across every Claude CLI session on the machine, not just the currently selected project. Backed by a SQLite FTS5 index (`%AppData%/TessynDesktop/session-index.db`) built and incrementally refreshed (by JSONL file mtime) from every `*/projects/*/*.jsonl` under **both** `~/.claude/` and every registered Claude account profile's own config directory (`%AppData%/TessynDesktop/profiles/<profileId>/`) — a profile's sessions run with `CLAUDE_CONFIG_DIR` set to its own folder, so they never appear under `~/.claude`. (Fixed 2026-07-27 — the index originally scanned only `~/.claude/projects`, silently missing every session run under a named profile and making tree/global search return empty results for most real usage.) Reindexed once in the background at app startup and exposed through `IDaemonClient.SearchSessionsAsync`/`ReindexSessionsAsync`.

Indexed message content includes USER, ASSISTANT, and tool-use SYSTEM entries (as parsed for display, FR.13.7) **plus** a TOOL_RESULT entry per `tool_result` block's text content — the output of Read/Grep/Bash/WebFetch/Task-subagent calls, which typically carries most of a session's substantive content but is never shown in the session view or exported .txt files (kept out of those to avoid flooding the conversation display with raw tool output). An `IndexContentVersion` marker (SQLite `PRAGMA user_version`) forces a one-time full rebuild of an existing index whenever the extracted content scope changes, since the per-file mtime check alone can't detect "the same file now yields richer content." (Fixed 2026-09-11 — TOOL_RESULT indexing added; see [[ISSUE.007]].)

`SearchAsync` results are ranked by FTS5 `bm25` relevance, preferring an exact-phrase match (adjacent tokens) and falling back to a looser AND-of-terms match only when the phrase finds nothing — plain AND-of-terms ranking otherwise scores incidental term co-occurrence the same as a genuine phrase hit. Results carry the session's stable external ID directly (no separate ID-resolution round trip) and are capped (default 50) for top-N display use, e.g. the import picker's global search.

`SessionTreeViewModel`'s tree search bar filters the tree by content match, including sessions from other projects, via the separate `SearchMatchingSessionIdsAsync` — an unranked, uncapped distinct-ID lookup. This is deliberately not `SearchAsync`: filtering already-known tree nodes is a membership test, not a ranked results list, and a hard result cap there would silently hide genuine matches in the current project whenever unrelated projects elsewhere on the machine score higher on the same terms. (Fixed 2026-09-11, see [[ISSUE.007]].)

Sessions found via global search that aren't yet tracked in the current project's tree are synthesized into importable picker entries (original project path best-effort recovered from the Claude CLI project slug, matching FR.13.11's cross-project import path).

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

**FR.14.11 — Native scheduling redirect:** When `AgentToolsEnabled` is true **and** the session is in One-shot Mode (FR.19), the hidden instruction block (FR.11.2, FR.11.10) includes a directive redirecting Claude away from the CLI's built-in scheduling tools (`ScheduleWakeup`, `CronCreate`, `CronList`, `CronDelete`) toward the TessynDesktop MCP scheduling tools (`mcp__cmx__schedule_wake`, `mcp__cmx__list_schedules`, `mcp__cmx__cancel_schedule`). **Rationale:** in One-shot Mode the `claude` process is terminated after each turn, so schedules registered via the CLI's built-in tools live only within that process's lifetime and are silently lost. The MCP-served scheduling tools delegate to the host-side `SchedulerService`, which persists schedules to `appsettings.json` and re-arms them on app startup (FR.14.7, FR.14.8). When `AgentToolsEnabled` is false, or when the session is in Streaming Session Mode (FR.19.10), the redirect line is omitted. As of **FR.19.11**, One-shot Mode spawns also pass `--disallowed-tools` for the same four native tools when `AgentToolsEnabled` is true, making the redirect an enforced block rather than an advisory instruction.

**FR.14.12 — UI-initiated delayed send:** Right-clicking the Send button (SessionView) opens a "Schedule Send" popup prompting for a delay amount and unit (Minutes / Hours / Days) instead of sending the composed message immediately. Confirming registers a **Delay**-kind self-wake schedule (FR.14.5, FR.14.6) targeting the session's own `NodeId`, using the identical scheduling engine exposed to agents as the `schedule_wake` MCP tool (`ISchedulerService`/`IDaemonClient.AddSchedule`) — the eventual turn is fired by the same `SchedulerService.FireScheduleAsync` → `SessionTurnService.RunTurnAsync` path used for any other scheduled turn (FR.14.9, FR.14.10), so instruction-block building, context preamble, and cross-profile session adoption all apply unchanged with no additional code. The input box is cleared immediately (mirroring an ordinary send) and a one-off system message in the output panel confirms the scheduled fire time; this message is UI-only and is not written to the session file. The existing per-session schedule indicator (clock icon, shown next to Send/Stop whenever the session has an active schedule, with a tooltip and a cancel action) applies unchanged, since a UI-created schedule is indistinguishable from one an agent creates. Left-click Send behavior is unaffected. Right-clicking with an empty input box is a no-op — there is nothing to schedule.

---

### FR.15 — Agent Orchestration (Persistent Worker Sessions)

A supervisor session can spawn and control worker sessions. All workers are **persistent, resumable** session nodes in the tree — never ephemeral subagents.

**FR.15.1 — Orchestration tools:**
| Tool | Required args | Optional args | Effect |
|---|---|---|---|
| `list_sessions` | — | — | Returns summary of all sessions in the tree: nodeId, name, dirLabel (directory label per FR.1.3), isRunning, isResumable, lastPrompt. |
| `spawn_session` | `name` | `workingDir`, `parentNodeId`, `prompt`, `model`, `group`, `outputFormat` | Creates a **new** session node, runs the first turn, returns `{nodeId, resultText}`. `model` is tier-enforced (FR.16). |
| `send_to_session` | `nodeId`, `prompt` | `mode` (wait/async), `outputFormat` | **Resumes** an existing session node, runs a turn. In `wait` mode returns the result synchronously. In `async` mode posts the result back to the supervisor when done. `outputFormat` is advisory markdown shaping guidance (FR.15.7). |
| `read_session` | `nodeId` | `lastN` | Returns the last N session file entries for the given node. |
| `stop_session` | `nodeId` | — | Cancels any running turn for the given node. |
| `set_session_model` | `nodeId`, `model` | — | Changes the model for an existing session (tier-enforced, FR.16.2). Effective from next turn. |
| `orchestrate_parallel` | `tasks` | `model`, `group`, `outputFormat`, `budgetTokens` | Barrier fan-out: spawns one session per task, waits for all, returns one markdown document (FR.15.8). |
| `orchestrate_pipeline` | `items`, `stages` | `model`, `group`, `outputFormat`, `budgetTokens` | Non-blocking item pipeline: one session per item through sequential stages, returns one markdown document (FR.15.8). |
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

**FR.15.7 — Output Format Guidance (markdown-centric, not schema-validated):** `spawn_session`, `send_to_session`, `orchestrate_parallel`, and `orchestrate_pipeline` accept an optional `outputFormat` argument — a free-text/markdown description of the desired response shape (e.g. a section outline). The host appends it to the relevant prompt (the first prompt for `spawn_session`; the prompt for `send_to_session`; every worker's prompt for `orchestrate_parallel`; the **last stage's** prompt only for `orchestrate_pipeline`) as advisory guidance. It is never validated or retried — a worker's result is consumed by another session, which reads markdown far better than JSON, so there is no host-side parsing step to enforce. Convention: when a worker's output is genuinely large or structured (data files, generated code, a big table), it should write the artifact to a file in its working directory and return the path instead of inlining the content (see glossary "Worker Artifact File").

**FR.15.8 — Fan-Out Primitives:**

`orchestrate_parallel` — barrier fan-out:
| Arg | Required | Description |
|---|---|---|
| `tasks` | yes | JSON array of `{name, prompt}` objects |
| `model` | no | Override model for all workers (tier-enforced, FR.16) |
| `group` | no | Group name for worker sessions in the tree |
| `outputFormat` | no | Advisory markdown shape guidance, applied to every worker's prompt (FR.15.7) |
| `budgetTokens` | no | Token ceiling for this orchestration (FR.15.6) |

Creates one worker session per task. All sessions run concurrently, bounded by `MaxConcurrentWorkers`. Returns **one markdown document** — a `## {name}` section per worker with its result and a trailing `_tokens: in=… out=…_` line — when ALL workers complete. Depth enforcement applies (FR.15.5).

`orchestrate_pipeline` — non-blocking item fan-out through sequential stages:
| Arg | Required | Description |
|---|---|---|
| `items` | yes | JSON array of string items |
| `stages` | yes | JSON array of `{prompt}` objects. Stages run sequentially within one session per item. Use `{{item}}` in the first stage prompt to reference the item. |
| `model` | no | Override model for all worker sessions (tier-enforced, FR.16) |
| `group` | no | Group name for worker sessions |
| `outputFormat` | no | Advisory markdown shape guidance, applied to the LAST stage's prompt only |
| `budgetTokens` | no | Token ceiling |

Creates one session per item. Within each session the stages are sent as sequential turns (the session accumulates full context). All item-sessions run concurrently. Returns **one markdown document** — a `## {item}` section per item with its result and a trailing `_tokens: in=… out=…_` line — when all are complete.

**FR.15.9 — Progress Tracking:**
- `workflow_phase(title)` — writes a `SYSTEM` entry `[Phase: {title}]` to the calling supervisor's session file. Visible in the session view. Returns immediately.
- `workflow_log(message)` — writes a `SYSTEM` entry `[Log: {message}]` to the calling supervisor's session file. Returns immediately.

**FR.15.10 — Per-Session Model Control:**
- `set_session_model(nodeId, model)` — changes the persisted `ModelId` on an existing session node (tier-enforced per FR.16.2; the caller's tier must be ≥ the requested model's tier). Takes effect on the next turn for that session. The supervisor can use this to switch models on running worker sessions mid-orchestration.

**FR.15.11 — CMX Orchestration Skill:** A skill named `cmx-orchestration` documents how to act as a supervisor (choosing between the orchestration tools, `outputFormat` shaping, the file-artifact convention) and how to behave when spawned as a worker (return markdown, honor `outputFormat`, don't over-nest, never use CLI-native scheduling). `SkillProvisioningService.ProvisionAll()` writes its `SKILL.md` (content: `Constants.Agent.OrchestrationSkillMarkdown`) into every registered profile's `skills/cmx-orchestration/` folder at startup — idempotent, rewriting only when content changes, mirroring `AgentMcpServer.EnsureConfigFileAsync`'s write pattern. Gated on `AgentToolsEnabled`; the Default profile (no dedicated config dir) is skipped. Only the skill's one-line frontmatter `description` is always visible to a session; the body loads on demand. A one-line pointer to the skill is folded into `Constants.Instructions.NoWorkflowTool`, so it is surfaced at session start under the same `IsAgentToolsEnabled && IsNewSession` gate as the rest of that instruction (FR.14.11) — not repeated on every turn.

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
The access token is read from the active profile's `.credentials.json` file (`claudeAiOauth.accessToken`). For the default profile the file is at `~/.claude/.credentials.json`; for named profiles it is at `%APPDATA%\TessynDesktop\profiles\<profileId>\.credentials.json`.

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

**FR.18.8 — Cache staleness on profile selection:** Each time the active profile changes (the same trigger points as FR.18.6 — selecting a different session, or changing the Profile selector within a session), the cached usage snapshot for the newly active profile, if any, is evaluated against the reset timestamps already supplied with it (`five_hour.resets_at`, `seven_day.resets_at`) before being shown. If the current time is at or past `FiveHourResetsAt`, that rolling window has necessarily turned over since the snapshot was captured, so the 5-hour utilization is shown as 0% (severity reset to `"normal"`) instead of the last-known percentage. The 7-day window is evaluated independently against `SevenDayResetsAt` the same way — a stale window on one side never forces the other to zero. The status bar omits the "resets …" time suffix for whichever window was just zeroed this way, until a fresh fetch supplies a current reset schedule. This complements FR.18.5: previously cached data "remained visible" indefinitely after a 401 with no further correction; now reselecting that profile later self-heals a stale non-zero reading back to 0% instead of continuing to show a number already known to be wrong. Implemented by `ClaudeUsageStalenessService` (`IsFiveHourStale`/`IsSevenDayStale`/`ZeroStaleWindows`), invoked from `ClaudeUsageService.SetActiveProfile` and reused by `MainWindowViewModel.UpdateStatusBarUsage` for the reset-time-suffix suppression.

---

### FR.19 — Streaming Session Mode

By default every turn spawns a fresh, one-shot `claude -p` process that reads one message from stdin, closes stdin, and exits — this is **One-shot Mode**, the mode described everywhere else in this document. **Streaming Session Mode** is a per-session, opt-in alternative: instead of one process per turn, one `claude` process is kept alive for the session's turns, spawned with `--input-format stream-json` and an stdin pipe that is **never closed**. Because the process persists between turns, native Claude Code capabilities that depend on the process staying alive (background tasks, native scheduling) function as designed, instead of being silently discarded when the one-shot process exits. This does not extend across app restarts or an explicit Stop — see FR.19.6.

**FR.19.1 — Toggle placement:** A per-session `ToggleButton` sits in the command bar beside the existing `--chrome` browser-control toggle (FR.12.14's UI), switching the session between One-shot Mode (default, unchecked) and Streaming Session Mode (checked).

**FR.19.2 — Persistent process spawn:** While a session is in Streaming Session Mode, sending a message spawns the `claude` process (if none is currently alive for this session) with `--input-format stream-json` in place of the one-shot behavior of writing one message and closing stdin. The process's stdin remains open across turns; each subsequent send while the process is alive writes a new turn to the same open stdin instead of spawning a new process. The exact stdin message schema for `--input-format stream-json` is confirmed empirically against the installed CLI before implementation (see Stage 1 spike) rather than assumed from documentation.

**FR.19.3 — One process per session:** At most one streaming process is alive per session (`NodeId`) at a time. If the process exits on its own (crash, CLI-side termination) while the toggle is still ON, the next send transparently spawns a new one.

**FR.19.4 — Stop button visible for the lifetime of the process:** The Stop button (FR.19 extends `CanStop`, previously FR.15's orchestration-only definition) is shown whenever a Streaming Session Mode process is alive for the session — not only while output is actively streaming. Because the application cannot know in advance whether the process has more output pending (a background task, a native schedule, additional turns), the process is treated as active for as long as it is running, independent of visible activity.

**FR.19.5 — Stop terminates the process:** Clicking Stop while a streaming process is alive kills the entire process tree (matching the existing kill semantics used for cancellation and shutdown elsewhere in the app) rather than attempting to interrupt only the current turn. The process is not automatically restarted; the next send (if the toggle is still ON) spawns a fresh one.

**FR.19.6 — Toggle persistence:** Streaming Session Mode is a sticky per-session setting, persisted on the session node in `appsettings.json` (mirrors Auto-Document, FR.11.5) — unlike the in-memory-only `--chrome` toggle (FR.12.14), it survives app restarts. On app restart or process exit, no streaming process is automatically respawned; the setting only affects the *next* send.

**FR.19.7 — Confirmation when turning Streaming off with a live process:** If the user un-checks the Streaming Session Mode toggle while a streaming process is currently alive for that session, a confirmation dialog warns that doing so will terminate the running process (and any in-progress background work). On confirm, the process is killed (FR.19.5) and the toggle is set OFF. On cancel, the toggle remains ON and the process keeps running.

**FR.19.8 — Locked configuration while a streaming process is alive:** The Model, Profile, and Effort selectors (FR.12.3, FR.12.6, FR.12.12) are disabled for a session while its streaming process is alive, since these are fixed as spawn-time arguments and cannot change on an already-running process. They become editable again once the process has stopped (via FR.19.5, FR.19.7, or the process exiting on its own).

**FR.19.9 — Mode-gated native-scheduling steering:** The native-scheduling redirect (FR.14.11) is emitted only for sessions in One-shot Mode; it is never emitted for a session in Streaming Session Mode, since the redirect's rationale (the process dies between turns, so native schedules are lost) does not apply while the process persists. This is a deliberate choice, not a guarantee: a Streaming Session Mode process is still not guaranteed to survive a Stop (FR.19.5) or an app restart, so schedules registered via Claude's native tools in Streaming Session Mode remain at the mercy of the process's actual lifetime. The `NoWorkflowTool` redirect (session-orchestration tools, FR.15) is unconditional on mode and is emitted in both One-shot and Streaming Session Mode, since the concern it addresses (subagents invisible to the session tree) applies regardless of process lifetime.

**FR.19.10 — Hard-blocking native scheduling tools in One-shot Mode:** In addition to the advisory redirect (FR.14.11), One-shot Mode process spawns pass `--disallowed-tools ScheduleWakeup CronCreate CronList CronDelete` when `AgentToolsEnabled` is true, making the four native scheduling tools unreachable rather than merely discouraged. This flag is **not** passed for Streaming Session Mode spawns, where the redirect itself is already omitted (FR.19.9) and the tools may legitimately function for the process's lifetime.

**FR.19.11 — Headless turns default to One-shot Mode:** Scheduled turns (FR.14), orchestrated turns (FR.15), and Telegram-routed turns (FR.21.4) run via `SessionTurnService`'s existing One-shot Mode path (`--resume`) whenever the target session has **no** currently-alive Streaming Session Mode process — this remains the common case and is unaffected by FR.19.13 below. They still acquire the same per-node turn lock (FR.14.9) as the streaming session's own sends, so a headless turn and an interactive send on the same session always serialize regardless of mode.

**FR.19.12 — Compaction and mid-run corrections unaffected:** Auto-Compact (FR.11.6) and mid-run toggle corrections (FR.11.12) continue to use independent, stateless one-shot `claude` calls (`sessionId: null`) regardless of the session's Streaming Session Mode setting — they are not routed through the persistent streaming process.

**FR.19.13 — Headless turns redirect into an already-alive streaming process (fixes a confirmed context-divergence bug):** Before falling back to FR.19.11's one-shot path, `SessionTurnService.RunTurnAsync` checks `IClaudeStreamingSessionManager.IsAlive(nodeId)`; if a Streaming Session Mode process is already alive for the target session, the turn is written to that process's open stdin (`SendTurnAsync`) instead of spawning a competing one-shot `--resume` process against the same `ClaudeSessionId`. This closes the gap FR.19.11 originally left open as an "accepted v1 limitation": a one-shot `--resume` process reads and extends the session's on-disk JSONL, but an already-running persistent process has no mechanism to notice that external change — its own in-memory conversation state silently diverges, so its next answer reads as if it "forgot" the headless-sourced exchange, even though the exchange is correctly present in both the on-disk JSONL and the Tessyn session file. Confirmed via live reproduction through the Telegram bridge; full incident writeup and evidence trail in `docs/issues/ISSUE.006.streaming-headless-context-divergence.md`.
- **Result plumbing without touching file/UI writes:** `IClaudeStreamingSessionManager.SendTurnAsync` gains an optional, additive `onTurnEvent` observer parameter — delivered every event for just this turn, alongside (not instead of) the process's existing primary `onEvent` callback (bound by whichever `TurnController` started and keeps the process alive). `SessionTurnService` uses this purely to build the `TurnResultModel` (assistant text, tokens, error state) its own caller needs; the primary callback remains solely responsible for appending assistant/system content to the session file and updating the UI, exactly as if the message had been typed on the desktop — this is what avoids a double-write.
- **Scope:** since the check is on `SessionTurnService.RunTurnAsync` itself, this applies uniformly to Scheduled, Orchestrated, and Telegram-routed turns — the bug was never Telegram-specific, only surfaced there first because Telegram is the headless source most likely to interleave with an open desktop tab in Streaming Session Mode.
- **Not covered:** the streaming process's spawn-time arguments (model, effort, MCP config) cannot be changed by a redirected headless turn — it always runs with whatever the process was started with. The assistant/system file entries for a redirected turn also do not carry `model=`/`effort=` metadata, since that stamping is owned by `TurnController`'s own per-turn pending-fields plumbing (UI-layer state), which a headless caller in Core has no access to and does not attempt to replicate — a minor, accepted cosmetic gap, not a functional one.

---

### FR.20 — External MCP Server Registry

A user-managed registry of MCP servers, independent from the in-process `cmx` orchestration server (FR.14.1) and **not** a bridge to claude.ai's own connector registrations. Registry entries are merged into every session's `--mcp-config` (One-shot and Streaming Session Mode alike) alongside `cmx`, so any Claude Code session — running against Anthropic or against a local Ollama model — can reach the same set of tools. An entry registered here is also the shared source of truth for a future in-process MCP client (a claude-CLI-free execution path, tracked in the Backlog) — registered once, reachable from either path.

**FR.20.1 — Registry data model:** `AppSettingsModel.ExternalMcpServers`: `List<ExternalMcpServerModel>`. Each entry:

| Field | Type | Notes |
|---|---|---|
| `Id` | string | stable identifier; used as the token-store filename and the `mcpServers` key |
| `DisplayName` | string | shown in Settings |
| `Enabled` | bool | disabled entries are omitted from `--mcp-config` entirely |
| `Transport` | enum: `Stdio`, `Http` | `Stdio` for a local process (e.g. a future VS/Roslyn server); `Http` for a remote server |
| `Command` / `Arguments` / `WorkingDirectory` | string(s) | `Stdio` only |
| `Url` | string | `Http` only |
| `AuthType` | enum: `None`, `StaticHeader`, `OAuth2` | `Http` only |
| `StaticHeaderName` / `StaticHeaderValue` | string | `AuthType = StaticHeader` only (e.g. a fixed bearer token) |
| `OAuth` | `McpOAuthConfig?` | `AuthType = OAuth2` only — see FR.20.3 |

**FR.20.2 — Merge into `--mcp-config`:** The method that currently builds the CLI's `--mcp-config` (today `AgentMcpServer.BuildMcpConfigJson`, `cmx`-only) is extended to append one `mcpServers` entry per **enabled** registry entry, alongside the always-present `cmx` entry. `Stdio` entries emit `{type:"stdio", command, args}`; `Http` entries emit `{type:"http", url, headers}`, with `headers` populated from `StaticHeaderValue` or a freshly-resolved OAuth bearer token (FR.20.6). This reuses the existing tool-capability gate (Ollama tool-incapable models, FR.16-adjacent existing behavior) — when tools are disabled for a turn, `--mcp-config` is omitted entirely; `cmx` and all registry entries are included together, never selectively.

**FR.20.3 — OAuth 2.1 client role:** For `AuthType = OAuth2` entries, Tessyn acts as its own OAuth client — no dependency on claude.ai's connector authentication — per the MCP Authorization specification:
- **Discovery:** probe `{Url}/.well-known/oauth-protected-resource` and/or `/.well-known/oauth-authorization-server` for `authorization_endpoint`, `token_endpoint`, and (if present) `registration_endpoint`. If discovery fails or is unsupported, fall back to endpoints entered manually in the registry entry (`McpOAuthConfig.AuthorizationEndpoint` / `TokenEndpoint`).
- **Client registration:** attempt Dynamic Client Registration (RFC 7591) against `registration_endpoint` if present; otherwise the registry entry carries a manually-provisioned `ClientId` (and `ClientSecret`, if the server requires one).
- **Scopes / redirect:** `McpOAuthConfig.Scopes` (space-delimited string); the redirect URI is always a loopback address on an ephemeral port chosen at authorization time — never a fixed port, to avoid collisions with FR.14.1's agent MCP port or other loopback services.

**FR.20.4 — Interactive authorization flow:** An "Authorize" action (Settings UI, FR.20.7) triggers: generate a PKCE `code_verifier`/`code_challenge` (S256); start a temporary loopback `HttpListener` on an ephemeral port; open the system's default browser at the authorization endpoint with the `code_challenge`, `redirect_uri`, and `scope`; on receiving the redirect with an authorization `code`, exchange it at the token endpoint for an access token and (if issued) a refresh token; stop the listener. Failure at any step (user cancels, server error, timeout) leaves the entry in a "Not connected" state — no partial or invalid token is stored.

**FR.20.5 — Token storage:** Tokens are never written to `appsettings.json`. Each OAuth-authorized entry's access/refresh tokens (plus expiry) are serialized and encrypted via Windows DPAPI (`ProtectedData.Protect`, `DataProtectionScope.CurrentUser`) to `%AppData%\TessynDesktop\mcp-tokens\<Id>.token`.

**FR.20.6 — Transparent refresh:** Before any `--mcp-config` build (FR.20.2) needs a bearer token for an `OAuth2` entry, the stored access token's expiry is checked; if expired or near-expiry, a refresh-token exchange is attempted first. If refresh also fails (revoked/expired refresh token), the entry is marked "Needs re-authorization" (surfaced in Settings, FR.20.7) and is **omitted** from that turn's `--mcp-config` rather than sending a stale/invalid token — a session simply proceeds without that server's tools rather than failing the whole turn.

**FR.20.7 — Settings UI:** A new registry management panel (Settings window) lists all entries with: enable/disable toggle, transport/auth-type summary, connection status (`Not connected` / `Connected as <identity, if known>` / `Needs re-authorization`), and per-entry Add/Edit/Remove/Authorize actions. Only `Http` + `OAuth2` entries show the "Authorize" button (FR.20.4).

**FR.20.8 — Scope boundary:** This registry is entirely independent of claude.ai's own MCP connector configuration. Servers that exist **only** as claude.ai connectors (e.g. a shared/third-party server not owned by the user) are left untouched — Claude Code sessions running against Anthropic continue to benefit from them exactly as today. Tessyn never reads, writes, or proxies claude.ai's connector settings. A server the user wants reachable by **any** Tessyn session — including ones on local Ollama models — must be registered here explicitly, even if it is already usable via claude.ai for Anthropic-routed sessions.

**FR.20.9 — Shared foundation:** Registry entries (and their live/refreshed OAuth tokens) are the single source of truth for every MCP-tool execution path Tessyn offers. The CLI `--mcp-config` merge (FR.20.2) is the first consumer; a planned in-process MCP client (a claude-CLI-free execution path, tracked in the Backlog) is expected to consume the same registry and token store rather than re-implementing OAuth.

**Implementation notes (Settings UI, FR.20.7):**
- Registry Add/Edit/Remove and the per-row Enable toggle persist immediately (`appsettings.json` write on every action) — unlike the rest of the Settings window's fields, which are held in copied ViewModel state and only committed on the window's main Save button. This is deliberate: the Authorize action (FR.20.4) already performs an immediate side effect (a network round-trip and a token-store write) regardless of the main Save button, so gating the rest of the registry's edits behind a separate, easily-forgotten Save click would create an inconsistent, partially-applied state.
- Editing an already-authorized `OAuth2` entry preserves its live discovery/connection state (`DiscoveryCompleted`, `NeedsReauthorization`, `ConnectedIdentity`) — changing the display name or an endpoint field does not silently invalidate an existing authorization.
- Discovery (FR.20.3) probes `.well-known/oauth-protected-resource` first; if it lists `authorization_servers`, the first entry's base URL is what's then probed for `.well-known/oauth-authorization-server` metadata, since some MCP servers separate the protected resource from its authorization server (RFC 9728). Falls back to probing the resource's own base URL directly when the protected-resource document is absent or has no such list.
- **Discovery-on-blur auto-fill:** the Add/Edit dialog's URL field triggers `IMcpOAuthService.DiscoverAsync` on `LostFocus` (not just when Authorize is clicked) for any `Http` transport, regardless of the currently-selected `AuthType` — so the Authorization/Token/Registration Endpoint fields in the `OAuth2` panel are already populated by the time the user switches Auth Type to `OAuth2`, whichever order they fill the form in. Never overwrites an endpoint the user already typed. `IMcpOAuthService.DiscoverAsync` is the same discovery logic `AuthorizeInteractiveAsync` uses internally, exposed as a standalone public method for this purpose. Silent on failure (network log only) — this is a convenience, not a requirement, and the user can still fill endpoints manually or retry via Authorize.
- **Bounded discovery + busy-state UI:** `DiscoverAsync` runs under its own short timeout (`Constants.ExternalMcp.DiscoveryTimeoutMs`, 8s), independent of the shared `HttpClient`'s 100s default — without this, a server that doesn't 404 unknown paths (e.g. one that routes every GET, including the `.well-known/...` probes, into its streamable-HTTP MCP handler) could hold the connection open and let `GetStringAsync` buffer an unbounded response body in memory for the full 100s. The Add/Edit dialog also cancels any prior in-flight discovery before starting a new one on the next `LostFocus` (and on dialog close), and disables the URL and OAuth endpoint fields it actually overwrites (`McpServerEditViewModel.IsDiscovering`) while a probe is running — so repeatedly tabbing in and out of the URL field can no longer stack up multiple concurrent unbounded probes, which previously manifested as the Settings window becoming unresponsive with climbing memory usage. `IsDiscovering`'s `IsEnabled` bindings deliberately stop at the fields discovery actually writes (URL, Authorization/Token/Registration Endpoint) rather than every OAuth2 field — disabling the Authentication `ComboBox` too (an always-visible, tab-adjacent control never written by discovery) was found to trigger Avalonia's focus manager reentrantly, since `OnUrlLostFocus` set `IsDiscovering` synchronously while that same handler's `LostFocus` dispatch was still on the call stack; `OnUrlLostFocus` now `await Task.Yield()`s first so the disabling happens on the next UI-thread turn instead.
- The registry-to-`--mcp-config` merge logic (FR.20.2) lives in `IMcpConfigMerger`/`McpConfigMerger`, extracted from `AgentMcpServer` specifically so it depends on nothing but `IMcpOAuthService` and is unit-testable without the rest of `AgentMcpServer`'s dependency graph (scheduler, session registry, TDCP server, etc.).

---

### FR.21 — Telegram Notification Bridge

A bidirectional bridge between Tessyn sessions and a Telegram bot, letting a session reach the user away from the desktop and letting the user reply to, browse, message, and operate sessions from Telegram. Every session's conversation — input and output, from both the desktop and Telegram — mirrors live into that session's own topic (FR.21.9), so a topic is a full window onto the session, not just a notification channel. Independent of the desktop toast channel (FR.16) — both fire from the same completion events, gated by separate settings.

**FR.21.1 — Data model:** `AppSettingsModel` gains:

| Field | Type | Notes |
|---|---|---|
| `TelegramEnabled` | bool | Master toggle. When false, the bridge does not start and all bot calls are no-ops. |
| `TelegramChatId` | long | The forum-topic supergroup's chat ID. Every inbound update from any other chat is dropped. |
| `TelegramAllowedUserIds` | `List<long>` | Telegram numeric user IDs authorized to message the bot. Everyone else's messages are logged and dropped, even inside the correct chat. |
| `TelegramTopicThreadIdsByNodeId` | `Dictionary<string, int>` | Persisted NodeId → Telegram `message_thread_id` map. The reverse lookup (thread → NodeId) is rebuilt in memory from this on `Start()`. |
| `TelegramTopicNamesByNodeId` | `Dictionary<string, string>` | Persisted NodeId → last-applied topic name (untruncated), recorded at topic creation (FR.21.2) and by topic name reconciliation (FR.21.18). Defaults to empty. No code path currently removes thread-id mappings; any future removal must also remove the corresponding entry here (FR.21.18). |
| `TelegramUpdateOffset` | long | Last-processed Telegram `update_id` + 1, persisted so a restart does not replay already-handled updates. |
| `TelegramMentionHandle` | string | @handle (e.g. `@harmlessevil`) prefixed onto notification pings (FR.21.10). Empty = no prefix. Never prefixed onto mirrored conversation text (FR.21.9) — only pings, so routine back-and-forth doesn't page the user on every line. |

The bot token itself is **not** in `AppSettingsModel` — see FR.21.6.

**FR.21.2 — Topic-per-session, created lazily on first activity:** The first time a session runs a turn — interactive (`TurnController.SendAsyncCore`) or headless (`SessionTurnService.RunTurnAsync`, which also covers scheduled, orchestrated, and Telegram-triggered turns) — while `TelegramEnabled` is true, `ITelegramBridgeService.EnsureTopicForSessionAsync` fire-and-forget-creates a Telegram forum topic named `"{directory label} / {session name}"` via `createForumTopic` and persists the NodeId → thread mapping. The directory label follows FR.1.3, derived from the session's working directory (e.g. `HardStop / Fix login`); the name actually applied is recorded as the last-applied topic name (`TelegramTopicNamesByNodeId`, FR.21.1). Names are truncated to 128 characters (`Constants.Telegram.MaxTopicNameLength`) when sent. Sessions never touched while the bridge is enabled never get a topic — there is no bulk backfill over session history. Errors (offline, bot misconfigured) are logged and swallowed; a topic-creation failure never fails or delays the turn it was piggybacked on.

**FR.21.3 — Outbound: notifications and questions:** A new `mcp__cmx__notify_user` tool (`AgentMcpServer`) lets a running session send an arbitrary message to the user's Telegram, ensuring its topic exists first. There is deliberately **no blocking "ask and wait" tool** — a `TaskCompletionSource`-style block risks getting stuck indefinitely on Telegram connectivity issues or the user simply being away from their phone, with no clean way to time out a mid-turn CLI process. Instead, `Constants.Instructions.TelegramNotifyGuidance` (emitted once per fresh session, alongside `NoWorkflowTool`, when `AgentToolsEnabled && TelegramEnabled`) instructs Claude to call `notify_user` with the question in plain text and then end its turn normally — the user's reply becomes the next ordinary prompt via FR.21.4, resuming the same session. The message is posted with the configured `TelegramMentionHandle` prefix (FR.21.10) so it stands out among the mirrored conversation text (FR.21.9) sharing the same topic.

**FR.21.4 — Inbound routing:** `ITelegramBridgeService` long-polls `getUpdates` (30s timeout, offset persisted per FR.21.1) while started. For each accepted update (chat ID + user ID both allow-listed):
- A message whose `message_thread_id` matches an entry in the topic map runs a turn on that NodeId via `IDaemonClient.RunTurnAsync(node, text, TurnSource.Telegram)`. The routed message itself is not re-echoed back into its own topic (FR.21.9's echo suppression), and the turn's output reaches the topic via the same universal mirror every other turn source uses — there is no separate "post the reply" step.
- A message with attachments (FR.21.13–FR.21.16) is routed the same way; its turn text is the message text plus attachment lines (FR.21.15).
- `/stop`, sent inside a session's own topic, cancels that session's in-flight turn via `IDaemonClient.CancelTurn` (FR.21.11) and confirms whether a turn was actually cancelled.
- `/sessions` replies with sessions where `IsFavorite` is true or the session already has a topic (mirrors the recent-dropdown favorite rule, FR.9.7, without requiring the 24-hour recency computation the bridge has no cheap access to), each line showing running state.
- `/open <NodeId prefix or name>` replies with that session's last 20 entries (reusing the `read_session` tool's logic) without requiring the session to already have a topic.
- Anything else (unmapped thread, unrecognized command) gets a short "unrecognized" reply.

**FR.21.5 — Composite notifications:** `CompositeNotificationService` (app project) implements `INotificationService`, wrapping the existing `WindowsNotificationService` (desktop toast, gated on `NotificationsEnabled`, still shown with the full assistant text) and `ITelegramBridgeService.NotifyAsync` (gated independently on `TelegramEnabled`) side by side — `SchedulerService`'s existing `ShowResult` call (scheduled/orchestrated turn completion) now reaches both channels without any change to `SchedulerService` itself. The Telegram side posts a short completion ping (e.g. `✅ finished`, plus the note if any) rather than the assistant text: FR.21.9's mirror already streamed that text into the topic as the turn produced it, so repeating it here would duplicate it.

**FR.21.6 — Bot token storage:** The bot token is never written to `appsettings.json`. It is DPAPI-protected (`CurrentUser` scope) at `%AppData%\TessynDesktop\telegram-token.dat` via a dedicated `ITelegramTokenStore`, mirroring the MCP Token Store (FR.20.5) but as a single file, since there is exactly one bot. Settings UI writes it immediately on a dedicated Save action, independent of the main Settings Save button, for the same reason FR.20.7's registry actions persist immediately.

**FR.21.7 — Settings UI:** A Telegram panel (Settings window) with: enable toggle, bot token entry (masked) + its own Save action, chat ID field, a comma-separated allowed-user-IDs field, and a mention-handle field (FR.21.10). The latter three are held in the normal copied ViewModel state and commit on the window's main Save button, like most other Settings fields.

**FR.21.8 — Session summary extensions:** `SessionSummaryModel` (used by `list_sessions` and now `/sessions`) gains `IsFavorite` and `LastActivityUtc` (the timestamp of the session file's last entry, any role), populated by `SessionRegistry.BuildSummary` from data it already reads for `LastPrompt`.

**FR.21.9 — Universal I/O mirror:** Every session's conversation mirrors into its Telegram topic as it happens — **except Worker Sessions**, excluded outright (FR.21.19) — regardless of which of the two turn-execution paths produced it — the interactive desktop path (`TurnController` driving `IClaudeProcessManager` directly, for live streaming) and the headless path (`SessionTurnService.RunTurnAsync`, used by the scheduler, orchestration, and Telegram-triggered turns themselves) both write every message through the single `ISessionFileService.AppendMessage`/`AppendCompactionSeparator` sink. `EntryAppended` — raised by that sink and relayed through `IDaemonClient.EntryAppended` per FR.21.10 — is the one tap point the bridge subscribes to; no per-turn-path integration is needed. Per appended entry:
- `ROLE=USER` content is posted with a `USER:` prefix — the Telegram Bot API cannot post a message as anyone but the bot itself, so this is the closest available approximation of "as written by me" for desktop-originated input.
- `ROLE=ASSISTANT` content posts with no prefix.
- `ROLE=SYSTEM` content (errors, phase/log markers, mid-run toggle notes) posts with a `⚙️` prefix.
- `ROLE=COMPACTION` separator entries (empty content) are not mirrored.
- Messages over Telegram's ~4096-character limit are chunked, preferring to split on a newline boundary.
- **Echo suppression:** the one `ROLE=USER` entry `SessionTurnService.RunTurnAsync` appends for a `TurnSource.Telegram` turn — the same text the user just typed into the topic — is suppressed rather than mirrored back, matched by NodeId + exact content against a short-lived flag set immediately before the routed turn starts.
- Raw tool-call/thinking events (visible only in the Claude JSONL, not the text Session File) are not mirrored — out of scope for this pass; deferred to a future richer/expandable view.

**FR.21.10 — Bridge as a daemon client:** `TelegramBridgeService` depends on `IDaemonClient` (`FindSessionByNodeId`, `FindSessionByFileName`, `RunTurnAsync`, `CancelTurn`, `ReadEntries`, `BuildAgentSessionSummaries`, `EntryAppended`) instead of reaching into `ISessionRegistry`/`ISessionTurnService`/`ISessionFileService` directly — it is a peer of the Avalonia UI on the daemon boundary (`docs/refactoring/tdcp-spec.md`), not a registry-coupled service. `IDaemonClient.EntryAppended` and `FindSessionByFileName` (added to `ISessionRegistry`/`IDaemonClient` for this) are the two additions this required; `InProcDaemonClient` relays the former from `ISessionFileService` exactly as it already does for `TreeChanged`/`ScheduleChanged`. `IDirectoryLabelService` (topic naming) and `IAppSettingsService`/`ITelegramClient` (settings/transport) remain direct dependencies — stateless utilities outside the daemon-owned turn/session domain, matching the carve-outs `tdcp-spec.md` §5 already makes for the UI. Outbound notifications (`notify_user`, scheduled/orchestrated completion pings) are prefixed with `TelegramMentionHandle` so they read as a ping directed at the user rather than more mirrored conversation text.

**FR.21.11 — Telegram-driven turn cancellation:** `/stop`, sent inside a session's own topic, calls `IDaemonClient.CancelTurn(nodeId)` for that topic's session and replies confirming whether a turn was actually cancelled — the first Telegram command that controls a session rather than just reading or messaging it.

**FR.21.12 — Update acknowledgement:** `getUpdates` offset advancement covers **every** update Telegram returned, not only the ones the bridge acts on: non-message updates, edited messages, unsupported message types, messages without a `from`, and messages from filtered (wrong chat / unauthorized user) senders are all acknowledged. `ITelegramClient.GetUpdatesAsync` therefore returns one `TelegramUpdate` per raw update, flagging non-actionable ones (`IsActionable == false`); the poll loop advances `TelegramUpdateOffset` to `max(update_id) + 1` over the whole batch and persists it, then dispatches only the actionable ones. Without this a trailing unsupported update is re-delivered immediately on every poll (tight loop, no long-poll wait) — see ISSUE.011.

**FR.21.13 — Inbound attachments:** A message carrying `photo` (largest `PhotoSize` is taken), `document`, `video`, `audio`, `voice`, `video_note` or `animation` is an actionable message. Its text is `text` ?? `caption` ?? `""`; a message with only attachments is valid. Chat/user authorization, topic routing and slash-command handling (`/sessions`, `/open`, `/stop`, recognized only in the plain text of a message with no attachments) are unchanged.

**FR.21.14 — Attachment download and inbox storage:** Each attachment is downloaded with the Bot API (`getFile` → `https://api.telegram.org/file/bot<token>/<file_path>`, reusing `ITelegramTokenStore` and `Constants.Telegram.BotApiBaseUrl`; the token is never logged). Files are stored under `%AppData%\TessynDesktop\telegram-inbox\yyyy-MM-dd\` (outside any repo) as `{updateId}-{index}-{sanitized original name or file_unique_id}{ext}`, where the extension is taken from the original file name, else the MIME type, else the `file_path`; photos are always `.jpg`. File names are sanitized (invalid path chars and path separators replaced, length-capped) and made collision-free. On `Start()` the bridge deletes inbox files older than 30 days (and empties date folders). The cloud Bot API cannot download files larger than 20 MB: if `file_size` exceeds 20 MB (or `getFile` fails with "file is too big") the download is skipped. A failed download never fails the poll loop nor loses the message text.

**FR.21.15 — Turn text format:** The text routed to the session (and compared by FR.21.9's echo suppression) is the message text, followed by one line per attachment, in order:
- `[attachment: <absolute path>]`
- `[attachment too large for Telegram bot download (<size> MB > 20 MB): <name> — send it from the desktop instead]`
- `[attachment download failed: <name> — <short reason>]`

The absolute path is exact (the downstream consumer is an LLM that uploads the file by path). Lines are joined with `\n`; with empty message text the turn text starts directly with the first attachment line.

**FR.21.16 — Albums (media groups):** Telegram delivers a multi-photo/video album as separate messages sharing `media_group_id`. Actionable messages with the same `media_group_id` (same chat and thread) are coalesced into ONE turn: the first non-empty caption wins; attachments are kept in message (update) order. Rule, deterministic: after a `getUpdates` batch, if the batch's **last** actionable update has a `media_group_id`, the bridge holds the trailing group and performs one extra short poll (`Constants.Telegram.AlbumFollowUpPollSeconds`, 1 s) from the already-advanced offset, merging any further messages of the held group before dispatching. Only a single follow-up poll is made per batch (the group is dispatched after it regardless). Groups that are complete within a batch (followed by an update of another kind) are dispatched immediately.

**FR.21.17 — Download robustness and logging:** Download/`getFile` errors are caught per attachment, logged as warnings (message only, no URL — the URL contains the bot token), and rendered as an `[attachment download failed: ...]` line. The poll loop is never interrupted.

**FR.21.18 — Topic name reconciliation:** Topic names are not readable back from Telegram, so the bridge reconciles them against the persisted last-applied names (`TelegramTopicNamesByNodeId`, FR.21.1).
- **Trigger:** `Start()` launches a background run that never blocks `Start()` or the poll loop and stops (cancellation) when the bridge stops. It works over a snapshot of `TelegramTopicThreadIdsByNodeId`. The run happens only when `TelegramChatId` is set (non-zero) — the same guard as topic creation (FR.21.2); otherwise it does nothing. Runs never overlap: a run started by `Start()` after a `Stop()` waits until the previous run, including its final save, has finished.
- **Per topic:** look up the session by NodeId; an orphan (no such session) is skipped and its mapping left untouched. Compute the current FR.21.2 name for the session (`{directory label} / {session name}`). If it equals the stored last-applied name, skip with no API call. Otherwise call `ITelegramClient.EditForumTopicAsync` (Bot API `editForumTopic`; returns `true` when renamed, `false` when Telegram answers `TOPIC_NOT_MODIFIED`, throws `TelegramApiException` carrying `ErrorCode`, `Description` and `RetryAfterSeconds` for any other failure; the name is truncated as in FR.21.2).
- **Outcomes:** renamed **or** "not modified" counts as success — the current name is recorded as the last-applied name. HTTP 429: wait `retry_after` seconds (cancellable), clamped to 1–300 s (1 s when `retry_after` is missing), and retry the same topic once; if it fails again the run stops. Run-stopping API errors: a rights error (401/403, or a description containing "not enough rights") or a chat-level error (a description containing "chat not found") — each logs a single warning and stops the run. Any other API error (e.g. deleted topic, `TOPIC_ID_INVALID`): log a warning, skip the topic, wait `TopicRenameNoOpIntervalMs` (1000 ms) and continue with the next topic. Non-API exception (network), including an HTTP timeout: log and stop the run (the next start retries). Cancellation is quiet only when the bridge itself is stopping; a cancellation caused by an HTTP timeout is a network failure and is handled as such.
- **Throttle:** after a real rename wait `Constants.Telegram.TopicRenameIntervalMs` (6000 ms); after a "not modified" result wait `TopicRenameNoOpIntervalMs` (1000 ms) — every real rename posts a service message into the group, whose flood limit is about 20 messages/minute, and this keeps half of that budget for normal bridge traffic. No wait follows a skip that made no API call.
- **Persistence:** recorded names are saved to settings after every 20 recorded names and once more at the end of the run (including stop/cancel), under the settings synchronization used elsewhere by the bridge.
- **Mappings:** no code path currently removes `TelegramTopicThreadIdsByNodeId` entries; any future removal must also remove the corresponding `TelegramTopicNamesByNodeId` entry.
- **Effect:** topics created before FR.1.3's short labels are migrated gradually, once, in the background (a large installation may take tens of minutes); afterwards each start renames only topics whose session was renamed since the name was last applied.

**FR.21.19 — Orchestrated children excluded from the bridge:** A Worker Session (`SessionNodeModel.OrchestrationDepth > 0` — spawned by `spawn_session`, `orchestrate_parallel`, or `orchestrate_pipeline`) never reaches Telegram, only the top-level session the user actually started does — whether that session is a plain session or a Supervisor Session fanning out dozens of workers.
- **No topic creation:** `EnsureTopicForSessionAsync` no-ops for a Worker Session (checked before the existing enabled/chat-id/already-has-a-topic guards).
- **No mirroring:** `OnEntryAppended` (FR.21.9) no-ops for a Worker Session's entries — its turns (prompt and response alike) never post to any topic.
- **No outbound pings:** `NotifyAsync` no-ops for a Worker Session — a worker calling `notify_user` produces no Telegram message and no topic, consistent with "never see them at all."
- **`/sessions` exclusion:** a Worker Session is excluded from the `/sessions` reply regardless of favorite or existing-topic state. `SessionSummaryModel` gains `OrchestrationDepth` (populated by `SessionRegistry.BuildSummary` from the same field) so the bridge can filter on it without an extra lookup.
- **Cleanup of pre-existing worker topics:** `Start()` runs a cleanup pass — chained before topic name reconciliation (FR.21.18) in the same background task — over the persisted `TelegramTopicThreadIdsByNodeId` map: for each NodeId whose session is found and has `OrchestrationDepth > 0`, the bridge calls `ITelegramClient.DeleteForumTopicAsync` and, on success, removes the NodeId from both `TelegramTopicThreadIdsByNodeId` and `TelegramTopicNamesByNodeId` and from the in-memory reverse lookup. An orphan (node no longer found) is left untouched, matching FR.21.18's existing orphan policy — there's no way to tell whether a since-deleted node was a worker. A deletion failure other than the topic already being gone leaves the mapping in place for a retry on the next start; a found-but-already-gone topic (Bot API `TOPIC_ID_INVALID`) still counts as success so the mapping gets cleaned up.
- **New client method:** `ITelegramClient.DeleteForumTopicAsync(chatId, messageThreadId, ct)` (Bot API `deleteForumTopic`) returns `true` on success or when Telegram reports `TOPIC_ID_INVALID` (already gone — functionally the desired state); any other failure throws `TelegramApiException`, mirroring `EditForumTopicAsync`'s error handling (FR.21.18).

**FR.21.20 — Discouraging notify_user misuse by workers and subagents:** FR.21.19 already blocks a Worker Session's `notify_user` call server-side (dropped outright, no topic, no message) — but that only covers Tessyn's own orchestration children (`spawn_session`/`orchestrate_parallel`/`orchestrate_pipeline`), which get a distinct `NodeId`/MCP token the server can check. A Task-tool subagent spawned by Claude Code's own built-in `Task` tool shares its parent session's `NodeId`/token entirely — Tessyn has no server-side signal to distinguish "the main agent thread" from "a Task subagent running inside it," so that case can only be addressed by instructing the model, not by blocking a call:
- The `notify_user` tool's own MCP description (seen by every caller, including a Task subagent sharing the session's token) now states it is for the top-level session only, must never be called from a worker or subagent, and should be used sparingly — one message per need the user must personally act on, never progress updates.
- `Constants.Instructions.TelegramNotifyGuidance` (FR.21.3) carries the same restraint wording.
- The `cmx-orchestration` skill's "As a worker" section explicitly tells a Worker Session not to call `notify_user` and to report results to its supervisor instead — closing the gap where the skill previously said nothing about it.
- `InstructionOptionsModel` gains `IsWorkerSession` (true when `OrchestrationDepth > 0`); `InstructionBlockBuilder.Build` now suppresses `TelegramNotifyGuidance` entirely for a Worker Session, on top of FR.21.19's runtime no-op — since the call is already guaranteed to do nothing there, there is no reason to tell it the tool exists at all. Set at both `SessionTurnService.RunTurnAsync` call sites (one-shot and the FR.19.13 streaming redirect) and in `SessionPromptComposer.BuildInstructionBlock` (interactive path), so a Worker Session never sees the guidance line regardless of how its turn is dispatched.
- **Residual limitation (accepted):** a Task-tool subagent still technically *can* call `notify_user` if it ignores the tool description and skill guidance — Tessyn cannot enforce this one server-side the way it does for genuine Worker Sessions. This is a prompt-engineering mitigation, not a hard guarantee.

**Setup (manual, one-time, outside the app):**
1. Message `@BotFather` on Telegram → `/newbot` → record the bot token.
2. Create a regular Telegram group (there is no separate "create supergroup" action — any regular group silently upgrades to a supergroup the moment Topics, below, is turned on):
   - Compose/pencil icon → **New Group** → add at least one member (the bot itself, if its username is already known, or any placeholder contact) → name it (e.g. "Tessyn Sessions") → create.
   - Open the group's info panel → **Edit** (pencil) → toggle **Topics** on. This both enables forum topics and upgrades the group to a supergroup — regular groups and channels cannot host forum topics.
3. Add the bot to the group as a member (Group Info → Add Members → search its username), then promote it: Group Info → Administrators → Add Admin → select the bot → ensure **Manage Topics** is enabled (required for `createForumTopic` and `editForumTopic`) → confirm/save the admin rights (some clients require an explicit checkmark tap, not just toggling switches). A bot that is only a plain member operates in Telegram's **Privacy Mode** — it only sees messages that are commands or that mention/reply to it, not ordinary text — and the group's own UI may surface this as "this bot has no access to messages" while it's still unpromoted. Making it admin (as above) bypasses Privacy Mode automatically; no separate BotFather setting is needed once that's done.
4. Get the group's chat ID (negative number, e.g. `-100xxxxxxxxxx`) — e.g. temporarily add `@RawDataBot` (or `@getidsbot`) to the group, read the chat ID it posts, then remove it; or send any message in the group and inspect the `getUpdates` response (`https://api.telegram.org/bot<TOKEN>/getUpdates` in a browser) for `"chat":{"id":-100xxxxxxxxxx,...}`.
5. Get your own numeric Telegram user ID (message `@userinfobot` directly — it replies with your ID).
6. Enter the bot token, chat ID, and your user ID into Tessyn's Settings → Telegram panel.

---

### FR.22 — Session Completion Sound

A per-session, opt-in audible cue for the interactive command bar, independent of the desktop toast/Telegram channels (FR.16, FR.21), which only fire for scheduled/orchestrated (headless) turns.

**FR.22.1 — Trigger point:** The sound fires when `TurnController.SendAsyncCore` clears the "Claude is thinking…" state on natural completion of the last concurrent send (the same point that stops the thinking timer and sets `IsBusy`/`IsRunning` false) — for both One-shot Mode (the spawned process exits) and Streaming Session Mode (`SendViaStreamingAsync` returns on the result event). It does not fire when the turn ends via user-initiated Stop (`OperationCanceledException` path) or any other non-natural termination.

**FR.22.2 — Per-session toggle:** `SessionNodeModel.PlayCompletionSound` (bool, default `true`), persisted like `IsStreamingMode`. Exposed via a bell-glyph (🔔) `ToggleButton` in the command bar, to the right of the Streaming Session Mode toggle. To fit three toggle buttons (`--chrome`, Streaming, Completion Sound) in the available space, all three are rendered as fixed-size squares (26×26, no padding) with reduced inter-button spacing and explicit `HorizontalContentAlignment`/`VerticalContentAlignment="Center"` (needed once `Padding="0"` is set, since Avalonia's default `ToggleButton` content alignment otherwise renders the glyph in the top-left corner of a fixed-size square), replacing their previous wider `Padding="7,2"` rendering.

**FR.22.3 — Per-session throttle:** No more than one sound per session within a configurable window (`AppSettingsModel.SessionCompletionSoundThrottleSeconds`, default 180 = 3 minutes). Enforced by `ISessionSoundService` (app-lifetime singleton) tracking last-played timestamp per `NodeId` in memory — a session that finishes multiple turns in quick succession (e.g. rapid follow-up prompts) is not spammed with sounds; a session that has been quiet for longer than the window is not blocked from playing.

**FR.22.4 — Sound source and playback:** `AppSettingsModel.SessionCompletionSoundFile` (string, default `%WINDIR%\Media\Windows Notify.wav`) names a `.wav` file. `ISessionSoundService` plays it via the Win32 `winmm.dll` `PlaySound` API (`SND_ASYNC | SND_FILENAME`) — chosen over `System.Media.SoundPlayer` or a NuGet audio package to avoid a project-reference change. Playback failures (missing file, no audio device) are logged and swallowed; they never fail or delay a turn.

**FR.22.5 — Sound picker:** Settings → Notifications gains a "Session completion sound" control group: a dropdown populated by `ISessionSoundService.GetAvailableSounds()` (every `.wav` under `%WINDIR%\Media\`, i.e. the Windows default sound library), a **Test** button that plays the selected sound immediately (bypassing the throttle), and a throttle-seconds numeric field bound to `SessionCompletionSoundThrottleSeconds`.

---

## Out of Scope (Initial Version)

- Session sharing or sync across machines
- Diff or branching of session history
- Deletion of session files from within the UI
- Plugin system
- Bridging to or reading claude.ai's own MCP connector configuration (FR.20.8) — servers usable only through claude.ai (e.g. third-party connectors not owned by the user) are intentionally left out of the External MCP Server Registry
- A blocking "ask and wait" Telegram tool (FR.21.3) — ruled out as unreliable (connectivity, user availability); `notify_user` plus plain-text follow-up replies covers the same need without a stuck-turn risk
- Closing/archiving Telegram topics when a session is deleted or hidden, multiple bots/chats, and Telegram-side message editing/deletion sync — not needed for the initial bridge and may be revisited later
- A public webhook receiver / daemon↔web relay for Telegram (the S3 topology sketched in `docs/refactoring/tdcp-spec.md` §3) — long polling (FR.21.4) already meets the latency need with zero fleet infrastructure; explicitly parked as a later phase, not built alongside FR.21.9–FR.21.11

---

## Glossary Reference

See `/docs/glossary.md` for domain term definitions.
