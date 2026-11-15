# TessynDesktop — Requirements

## Project Overview

**TessynDesktop** is a Windows desktop application (WPF client over a UI-free daemon; see NR.1) that wraps the Claude Code CLI tool, providing a structured session management UI. It does not replace Claude Code — it hosts it, stores its history, and presents it in an organized, navigable interface.

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

**FR.1.9 — Search:** A search box positioned above the tree performs a full-text search using the Cross-Project Search Index (FR.13.15), with a 300ms debounce after each keystroke and the query run on a background thread. The tree filters to show only Session nodes whose indexed content matches; ancestor nodes (Group, Directory) of matching sessions stay visible, all others are hidden. A supervisor session stays visible when any of its sub-sessions match, and non-matching sub-sessions are hidden. Any typed text is a valid query — emails, file names, paths and punctuation are matched literally and never cause a query error (FR.13.15). Only if the index itself cannot be opened does search fall back to a substring scan of session files in the configured Session Files Root; the fallback matches message bodies only (never the entry header lines, which carry the account profile such as `profile="user@example.com"`), and skips files it cannot read (e.g. one a running session is writing) instead of failing the search. (Fixed 2026-11-05, see [[ISSUE.015]].)

**FR.1.10 — Project-scope toggle:** A toggle button next to the search box limits search results to the currently selected session's project (matched by Claude CLI project slug) when enabled. It defaults to enabled whenever a session is selected, and is forced off and disabled whenever no session is selected. Changing the toggle, or selecting a different session while it is enabled, re-runs the active search immediately.

**FR.1.11 — Searching indicator:** While a search query is running against the index, an animated spinner glyph is shown inside the search box to indicate that a search is in progress, since index lookups are not always instantaneous. The indicator is cleared when the search completes, fails, or is cleared — a failing search never leaves it spinning (see [[ISSUE.015]]).

**FR.1.12 — Relevance ordering:** While a search is active, the tree is reordered most relevant first. A session's relevance is the number of its messages containing the query as an exact phrase (strict match), then the number of its messages containing all query words anywhere (loose match); any strict match outranks any loose-only match, and ties go to the most recent prompt. A Group or Directory ranks by its best descendant session. Reordering is view-only — the persisted tree order is never changed — and clearing the search restores the original order (sessions added during the search appear at the end of their container). Moving sessions (drag-drop and the Move command) is unavailable while a search is active, since moves are positional and the displayed order is not the persisted one.

**FR.1.13 — Live indexing:** Sessions become searchable as they post output, without an app restart. After each completed turn (`TurnCompleted`), that session's Claude CLI JSONL log is re-indexed (debounced ~2s per session, so a burst of turns indexes once). If a search is active when a session is re-indexed, the search re-runs so the tree reflects the new content.

**FR.1.14 — Exact search toggle:** A toggle next to the search box limits results to sessions containing the query as an exact phrase (strict match, FR.1.12); loose-only matches are hidden. It switches on automatically when a search runs for a query that starts with `https://` or is shorter than 8 characters. Unchecking it for such a query holds until the search text changes; the automatic rule then applies to the new text. For other queries the toggle keeps whatever the user last chose. In a single-word query strict and loose match the same sessions, because the index matches whole words, so the toggle only changes results for multi-word queries and links.

**FR.1.15 — Left panel tabs:** The left panel has three tabs, in this order:
- **Tree**: the session tree (FR.1.1–FR.1.14).
- **Recent** (FR.1.16).
- **Tags** (FR.24.11).

The selected tab isn't persisted: the app starts on Tree. Picking a session in the top-bar recent-sessions dropdown switches to the Tree tab, as it does today.

**FR.1.16 — Recent tab:** Documents today's behaviour plus the new collapse rule:
- **Content.** Every top-level session; Worker Sessions stay nested in the tree and aren't listed.
- **Grouping.** Sessions are grouped under a header per Directory node: a 📂 icon and the Auto-derived label (FR.1.3), with directories in tree order.
- **Order within a group.** Sessions with unsent draft text come first, then the rest, each newest first by Last Activity (`SessionNodeViewModel.LastActivityTimestamp`, FR.9.8).
- **Rows.** Status dots, name, the draft ✏ indicator, the favorite star (FR.9.7), profile name, last prompt time, the recency background (FR.9.5) and the dotted border for recently active sessions.
- **Click.** Clicking a row selects that session in the tree too. This also works when the same row is clicked again after another session was selected elsewhere.
- **Rebuild.** The list is rebuilt each time the tab is selected.
- **NEW: collapsible groups.** Each directory header shows a chevron (▾ expanded, ▸ collapsed); clicking the header toggles its group.
- **NEW: collapse on switch.** Each time the user switches to the Recent tab, every group is collapsed except the one for the Directory node containing the selected session. With no session selected, every group is expanded. Expanding or collapsing a group by hand holds until the next switch to the tab.

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

**FR.2.3.2 — Text selectability:** All rendered text in the output panel must be selectable (copy-able) by the user, including user prompts, assistant responses (both plain text and markdown mode), headings, list items, code blocks, and system messages. Text blocks display an I-beam cursor on hover and show a visible blue selection highlight when text is selected. Cross-block selection is supported: when a pointer drag crosses from one text block into another, the selection extends across all intervening blocks. The first and last blocks receive partial selection; intermediate blocks are fully selected. Ctrl+C copies the combined cross-block text. Auto-scrolling is triggered when the pointer nears the viewport edges during a drag. This works uniformly in both plain-text mode (across messages) and markdown mode (across paragraphs, headings, code blocks within and across messages). Cross-message selection works within the rendered window of the transcript (about 1.5 screens either side of the viewport; Architect decision A4, 2026-11-15).

**FR.2.3.3 — Markdown table rendering:** Tables in markdown responses shall render with:
- Theme-aware border and header colors that adapt correctly to both dark and light themes (taken from the platform theme's low-emphasis foreground and chrome colours)
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

**NR.1 — Platform:** The desktop client is a Windows application (WPF). The daemon logic (sessions, turns, orchestration, remote API) stays free of UI types and becomes cross-platform when the daemon moves out of the desktop process (Phase 4 of the WPF programme, `docs/refactoring/wpf-programme-plan.md`). Platform-specific code (path resolution, process launching, app data directory) is isolated behind interfaces. New desktop UI is built in WPF only (decision A9, 2026-11-15); the Avalonia client receives no new UI features and is removed once WPF reaches parity.

**NR.2 — Performance:** The budgets below are measured with the Phase 0 benchmark (`docs/refactoring/wpf-programme-plan.md` section 6). The session tree holds about 2,700 sessions today.
- First paint after a session switch: cold at most 100 ms (small sessions), 400 ms (1,600 entries) and 600 ms (5,000 entries); warm at most 150 ms for every size.
- No degradation: the p95 of the last 50 switches is at most 1.2 times the p95 of the first 50, over a 200-switch round robin.
- Memory growth is at most 50 MB over 200 switches.
- UI-thread stall at most 50 ms in steady state and 250 ms during a switch; with 2,500 sessions idle, no stall over 16 ms.
- Built transcript visuals stay under 500 whatever the session length.
- Logs stay under 5 MB per hour at the default level.

**NR.3 — Durability:** Session file appends are flushed immediately. `appsettings.json` is written atomically (temp-write + rename) AND all reads/writes are serialized under a single process-wide lock (`IAppSettingsService.SyncRoot`), so concurrent Saves from multiple threads (orchestration workers, Streaming Session Mode, UI toggles) can no longer race and silently lose an update (see ISSUE.008).

**NR.4 — Architecture:** MVVM with WPF and CommunityToolkit.Mvvm. One ViewModel per significant view. Models and services have no UI dependencies. Clients (the WPF desktop client, Tessyn Web) reach the daemon only through the remote API (TDCP, `docs/refactoring/tdcp-spec.md`); they never call daemon services directly.

**NR.5 — Testability:** Session file I/O, tree persistence, and process management are behind interfaces; unit tests require no real `claude` process or real filesystem. One contract-test suite runs against every way a client reaches the daemon (in-process, loopback HTTP, relay) and the answers must match. UI end-to-end tests live in explicitly-run projects (`Tessyn.Web.E2E.Tests`, `TessynDesktop.WPF.E2E.Tests`) that start the app against a test daemon on a temporary data root.

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

**FR.9.1 — Theme variants:** The application supports Dark and Light themes. The selected theme controls the UI framework's theme variant (which affects all built-in control styling) and also selects which set of custom colors to apply.

**FR.9.2 — Per-theme custom colors:** Each theme (Dark and Light) has an independent set of customizable colors stored as hex strings in `appsettings.json`:
- Input box background and text color
- User message bubble background and text color
- Code block background and text color
- Inline code background and text color
- System message bubble background color
- Session recency bar colors (3 tiers: 15 min, 30 min, 60 min)

**FR.9.3 — Color application:** Custom colors are applied as application-level theme resources (named `Cmx*` colour keys: `CmxInputBg`, `CmxInputFg`, `CmxUserBubbleBg`, `CmxUserBubbleFg`, `CmxCodeBg`, `CmxCodeFg`, `CmxInlineCodeBg`, `CmxInlineCodeFg`, `CmxSystemBubbleBg`, `CmxRecency15Min`, `CmxRecency30Min`, `CmxRecency60Min`). These are consumed by the session view, the markdown/transcript renderer and the session-tree rows (recency colours). The editable colour palette is dropped at WPF parity (decision A8); the WPF client may revisit theming later. Colors are re-applied immediately when the theme is toggled or when settings are saved.

**FR.9.5 — Session recency bars:** Session nodes in the tree display a colored background bar indicating how recently the last user prompt was sent:
- **Light green** (customizable): last prompt within 15 minutes
- **Green** (customizable): last prompt within 30 minutes
- **Dark green** (customizable): last prompt within 1 hour
- No bar: last prompt more than 1 hour ago (or no prompts)

Recency bars refresh automatically every 60 seconds so the visual state stays current as time passes. The three recency colors are per-theme and editable in the Settings window.

**FR.9.4 — Sensible defaults:** Both themes ship with sensible default color values. Dark defaults use VS Code-inspired dark palette; Light defaults use standard light-background colors. Users can customize any color via hex input in the Settings window.

**FR.9.6 — Recent-dropdown hide:** The top-bar recent-sessions dropdown lists sessions active within the last 24 hours, plus Pinned Sessions (FR.9.8). Each row of a session that is not pinned has a hide button that sets `RecentIgnoredAt` (`SessionNodeModel`) to the current time, removing the session from the dropdown until its next real activity (prompt, response, or draft edit) — tracked via `IsHiddenFromRecent` on `SessionNodeViewModel`, which auto-clears once newer activity is recorded.

**FR.9.7 — Favorite sessions:** A session can be marked as a favorite, independent of the FR.9.6 hide/recency mechanism.
- **Persistence:** `IsFavorite` (bool) on `SessionNodeModel`, persisted in `appsettings.json`.
- **Command-bar toggle:** A star toggle button in the session command bar (FR.12.2), immediately left of the Model selector. Unchecked: grey-stroked, transparent fill. Checked: black-stroked, gold/yellow fill. Clicking toggles favorite state for the active session.
- **Recent dropdown inclusion:** A favorite is a Pinned Session (FR.9.8): it always remains in the top-bar recent-sessions dropdown regardless of the 24-hour recency window (FR.9.6) and regardless of hidden state. Sort order is unchanged — most-recently-active first — so a favorite with no recent activity sinks to the bottom of the list rather than being dropped.
- **Recent-dropdown row affordance:** Each dropdown row shows at most one icon on its right side: a gold/black star if the session is favorited (clicking un-favorites it, after which the hide icon from FR.9.6 takes its place unless the session is still pinned by FR.9.8), or the hide icon (⦸) if the session is not pinned (clicking hides it per FR.9.6). A non-favorite session pinned by open Focus items or schedules shows neither icon; the icon slot keeps its width so the row's columns stay aligned.
- **Tree and Recent-tab display:** The session tree and the Recent-tab flat list each show a display-only gold/black star at the far right of the session row's top line, above the last-activity date/time, when the session is favorited. The existing draft indicator (✏, FR — "has unsent text") remains immediately right of the session name and is unaffected.
- **Hide icon replacement:** The recent-dropdown hide icon (FR.9.6) is changed from the eye pictogram (👁) to a monochrome bold-slashed-circle glyph (⦸, U+29B8), for visual consistency with the rest of the flat, non-emoji iconography used elsewhere in the command bar.

**FR.9.8 — Recent-dropdown Pinned Sessions and order:**
- **Pinned:** a session is pinned in the top-bar recent-sessions dropdown when it is a favorite (FR.9.7), has at least one open Focus item of any kind, `Decided` included (FR.23.17), or has at least one schedule (FR.14).
- **Membership:** the dropdown lists every Pinned Session regardless of the 24-hour recency window and of hidden state (`RecentIgnoredAt`, FR.9.6), plus every session active within the last 24 hours that is not hidden. A pinned row offers no hide button, since hiding it would have no visible effect. Worker Sessions, shown nested under their supervisor in the tree, are not listed. The rule lives in `RecentSessionVisibilityService` (`ShouldShowInRecentDropdown`, `IsPinnedInRecentDropdown`).
- **Order:** most recently active first, by the latest of the last prompt sent, the last response received and the last non-empty draft edit (`SessionNodeViewModel.LastActivityTimestamp`). Sessions with no activity come last.
- **Refresh:** the list, and each session's schedules flag, are rebuilt at startup, every time the dropdown opens and after a hide. Row badges (FR.23.17) update live, also while the list is open.
- **Row badge:** each row shows the session-row badge (FR.23.17) between the status dots and the session name. The closed dropdown box shows the session name only.

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

Highlighting splits the text at match boundaries and renders matches with the yellow background, in plain-text and markdown modes alike. Highlighting is cleared when the search is dismissed.

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

- **Left-truncated model label:** unlike Profile and Effort (whose values truncate normally, trailing-edge, via the framework's standard trailing-ellipsis trimming), the Model box's selected value is right-aligned and truncated from the **left** — e.g. `claude-opus-4-8` displays as `…pus-4-8` — so the distinctive trailing suffix of a model ID (the part that actually distinguishes it from sibling models) stays visible instead of the shared `claude-` prefix. Implemented with leading-ellipsis trimming, sized to the box's actual available width rather than a fixed character count.
- **`:latest` tag stripped from the displayed model text only:** Ollama tags commonly carry a trailing `:latest` (e.g. `qwen3.6:35b-a3b:latest`). Both the Model box's closed-box label and its open dropdown list strip this trailing `:latest` from the text shown to the user — so the example above displays as `qwen3.6:35b-a3b` — via display converters. This is purely cosmetic: the underlying model ID string is completely unchanged everywhere it is actually used — the `--model` CLI flag, `SelectedModelId` persistence, and `LocalToolProfileResolver`'s model matching all continue to see the full ID including `:latest`.
- **Local Tool Profile control relocated (amended by FR.12.23):** the per-directory Local Tool Profile (FR.12.21) is no longer a selector in the Model/Profile/Effort row. It is now an icon-style toggle button — sized to match the existing 26×26 icon buttons (Chrome, Streaming Session Mode, Session Completion Sound) — positioned immediately to the right of the Session Completion Sound button (FR.22). It remains visible only when the directory's currently selected model is local (`ModelProvider.Ollama`), and remains disabled (with the existing tooltip explaining why) when the selected local model does not support tool calling. Its click/press-and-hold interaction and popup content are specified in FR.12.23.
- **No drop-down arrow chrome:** the three `.cmxCompact` boxes also suppress the theme's built-in drop-down arrow glyph and let the closed-box content span into the reclaimed space — clicking anywhere on the closed box still opens the dropdown, so the arrow was pure decoration and, on the 60px Effort box, ate about half the available width.

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

Free-typed queries are never passed to FTS5 as raw syntax. The query is split on whitespace and each word is quoted as an FTS5 string, so characters FTS5 treats as syntax (`@ . : \ - "` etc.) are matched literally — e.g. `vitaly@example.com` becomes the phrase of tokens `vitaly example com`. Strict match = the whole query as one phrase; loose match = every quoted word present (implicit AND). `SearchSessionScoresAsync(query, projectSlug)` returns, per matching external ID, the count of messages with a strict match and the count with a loose match — the input to the tree's relevance ordering (FR.1.12). `IndexSessionAsync(externalId, workingDirectory)` re-indexes a single session's JSONL (located under each Claude projects directory by the working directory's project slug) when its mtime changed, for live indexing (FR.1.13); index writes from it and from the startup `ReindexAsync` are serialized so they never contend for the SQLite write lock. (Fixed 2026-11-05 — before this, any query containing FTS5 syntax characters failed with `fts5: syntax error` and silently dropped to the slow file-scan fallback; see [[ISSUE.015]].)

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

**FR.14.9 — Per-node turn serialization:** At most one turn may run concurrently per `NodeId`. If a live user-initiated turn and a scheduled turn would overlap, the scheduled turn waits for the lock to be released. Exception: a Mailbox Delivery stays pending while its target session is busy instead of waiting on the lock, so results that keep arriving can be merged into it (FR.15.12).

**FR.14.10 — Scheduled turn visibility:** A scheduled turn's prompt is appended to the session file as a `USER` entry, and the response as an `ASSISTANT` entry. A `SYSTEM` entry `[Scheduled: <note>]` is prepended to the prompt so the trigger is visible in the session view. The session file watcher (`FR.3.1`, `SessionViewModel`) picks up the new entries and updates the output panel for any session that is currently open.

**FR.14.11 — Native scheduling redirect:** When `AgentToolsEnabled` is true **and** the session is in One-shot Mode (FR.19), the hidden instruction block (FR.11.2, FR.11.10) includes a directive redirecting Claude away from the CLI's built-in scheduling tools (`ScheduleWakeup`, `CronCreate`, `CronList`, `CronDelete`) toward the TessynDesktop MCP scheduling tools (`mcp__cmx__schedule_wake`, `mcp__cmx__list_schedules`, `mcp__cmx__cancel_schedule`). **Rationale:** in One-shot Mode the `claude` process is terminated after each turn, so schedules registered via the CLI's built-in tools live only within that process's lifetime and are silently lost. The MCP-served scheduling tools delegate to the host-side `SchedulerService`, which persists schedules to `appsettings.json` and re-arms them on app startup (FR.14.7, FR.14.8). When `AgentToolsEnabled` is false, or when the session is in Streaming Session Mode (FR.19.10), the redirect line is omitted. As of **FR.19.11**, One-shot Mode spawns also pass `--disallowed-tools` for the same four native tools when `AgentToolsEnabled` is true, making the redirect an enforced block rather than an advisory instruction.

**FR.14.12 — UI-initiated delayed send:** Right-clicking the Send button (SessionView) opens a "Schedule Send" popup prompting for a delay amount and unit (Minutes / Hours / Days) instead of sending the composed message immediately. Confirming registers a **Delay**-kind self-wake schedule (FR.14.5, FR.14.6) targeting the session's own `NodeId`, using the identical scheduling engine exposed to agents as the `schedule_wake` MCP tool (`ISchedulerService`/`IDaemonClient.AddSchedule`) — the eventual turn is fired by the same `SchedulerService.FireScheduleAsync` → `SessionTurnService.RunTurnAsync` path used for any other scheduled turn (FR.14.9, FR.14.10), so instruction-block building, context preamble, and cross-profile session adoption all apply unchanged with no additional code. The input box is cleared immediately (mirroring an ordinary send) and a one-off system message in the output panel confirms the scheduled fire time; this message is UI-only and is not written to the session file. The existing per-session schedule indicator (clock icon, shown next to Send/Stop whenever the session has an active schedule, with a tooltip and a cancel action) applies unchanged, since a UI-created schedule is indistinguishable from one an agent creates. Left-click Send behavior is unaffected. Right-clicking with an empty input box is a no-op — there is nothing to schedule.

---

### FR.15 — Agent Orchestration (Persistent Worker Sessions)

A supervisor session can spawn and control worker sessions. All workers are **persistent, resumable** session nodes in the tree — never ephemeral subagents.

**FR.15.1 — Orchestration tools:**
| Tool | Required args | Optional args | Effect |
|---|---|---|---|
| `list_sessions` | — | `scope`, `nodeId`, `query`, `running`, `activeWithinHours`, `includeWorkers`, `tags`, `limit`, `offset` | Returns one page of sessions, newest first — by default the caller's project, with the caller's own workers nested under its row (FR.15.13). Each row: Short Node Id, name, age of its Last Activity, and flags (`running`, `fav`, `no-resume`, worker counts). |
| `spawn_session` | `name` | `workingDir`, `parentNodeId`, `prompt`, `model`, `group`, `outputFormat` | Creates a **new** session node, runs the first turn, returns `{nodeId, resultText}`. `model` is tier-enforced (FR.16). |
| `send_to_session` | `nodeId`, `prompt` | `mode` (wait/async), `outputFormat` | **Resumes** an existing session node, runs a turn. In `wait` mode returns the result synchronously. In `async` mode posts the result back to the supervisor when done, as a Mailbox Delivery (FR.15.3, FR.15.12). `outputFormat` is advisory markdown shaping guidance (FR.15.7). |
| `read_session` | `nodeId` | `lastN` | Returns the last N session file entries for the given node. |
| `stop_session` | `nodeId` | — | Cancels any running turn for the given node. |
| `set_session_model` | `nodeId`, `model` | — | Changes the model for an existing session (tier-enforced, FR.16.2). Effective from next turn. |
| `orchestrate_parallel` | `tasks` | `model`, `group`, `outputFormat`, `budgetTokens` | Barrier fan-out: spawns one session per task, waits for all, returns one markdown document (FR.15.8). |
| `orchestrate_pipeline` | `items`, `stages` | `model`, `group`, `outputFormat`, `budgetTokens` | Non-blocking item pipeline: one session per item through sequential stages, returns one markdown document (FR.15.8). |
| `workflow_phase` | `title` | — | Writes `[Phase: {title}]` to the supervisor session file (FR.15.9). |
| `workflow_log` | `message` | — | Writes `[Log: {message}]` to the supervisor session file (FR.15.9). |
| `get_budget` | — | — | Returns token usage and remaining budget for the caller's active orchestrations (FR.15.6). |

Every argument that takes a session id (`nodeId`, `target`, `parentNodeId`) accepts a full NodeId or a unique prefix of it (Short Node Id, FR.15.13).

**FR.15.2 — New-vs-resume choice:** The supervisor explicitly chooses between `spawn_session` (creates a new tree node) and `send_to_session` (resumes an existing node). `list_sessions` provides the data needed to make that choice: called with no arguments it lists the supervisor's own workers under its row (FR.15.13).

**FR.15.3 — Async mailbox:** When `send_to_session` is called with `mode=async`, on worker turn completion a follow-up turn is automatically triggered on the supervisor node — a **Mailbox Delivery**. It is implemented as a delay-0 schedule targeting the supervisor (`ScheduleModel.IsMailboxDelivery = true`), reusing the scheduler engine from FR.14; its prompt is `[Worker finished] <name>` followed by the worker's result text on the next line, and its note is `Worker '<name>' result`. Results that arrive while a delivery for the same supervisor is still pending are merged into it (FR.15.12).

**FR.15.4 — UI thread marshaling:** All tree mutations triggered by orchestration tools (node creation, model changes) are executed on the UI thread through the UI dispatcher abstraction (`IUiDispatcher`), never directly from the worker thread that handled the tool call.

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

**FR.15.11 — CMX Orchestration Skill:** A skill named `cmx-orchestration` documents how to act as a supervisor (choosing between the orchestration tools, `outputFormat` shaping, the file-artifact convention) and how to behave when spawned as a worker (return markdown, honor `outputFormat`, don't over-nest, never use CLI-native scheduling). `SkillProvisioningService.ProvisionAll()` writes its `SKILL.md` (content: `Constants.Agent.OrchestrationSkillMarkdown`) into every registered profile's `skills/cmx-orchestration/` folder at startup — idempotent, rewriting only when content changes, mirroring `AgentMcpServer.EnsureConfigFileAsync`'s write pattern. Gated on `AgentToolsEnabled`; the Default profile (no dedicated config dir) is skipped. Only the skill's one-line frontmatter `description` is always visible to a session; the body loads on demand. A one-line pointer to the skill is folded into `Constants.Instructions.NoWorkflowTool`, so it is surfaced at session start under the same `IsAgentToolsEnabled && IsNewSession` gate as the rest of that instruction (FR.14.11) — not repeated on every turn. Its supervisor section also states (ISSUE.012): worker results and wake-ups arrive as scheduled turns the user is not watching, and nothing tells the user that such a turn finished — call `notify_user` once, and only when the user must decide or act (FR.21.24) [**amended by FR.23.10:** the supervisor posts the ask once with `post_attention` (`notify: true` when the user must act soon) and does not restate it in later delivery turns, and workers call neither `notify_user` nor `post_attention`]; keep delivery turns short and don't narrate each worker's arrival; don't arm polling or "fallback" self-wakes to collect async results — they arrive by themselves, merged when they queue up (FR.15.12) — and use a self-wake only for a real deadline or a long-interval check on a worker that may be stuck. Its worker section tells a session it is a worker when its prompt is a scoped task sent by another session, and that `list_sessions` confirms this on its `You:` line (FR.15.13); it no longer suggests a full listing for that check.

**FR.15.12 — Mailbox coalescing:** A supervisor whose workers finish close together gets one delivery turn, not one turn per worker (ISSUE.012).
- **Merge:** the async path of `send_to_session` hands each result to `SchedulerService.AddOrMergeMailboxDelivery(supervisorNodeId, workerName, resultText)`. Under the scheduler's lock it appends the result to the supervisor's pending Mailbox Delivery if there is one (a blank line, then `[Worker finished] <name>` and the result text), otherwise it adds a new delivery (FR.15.3). A merged delivery's note becomes `Worker results (<n>): <name1>, <name2>, …`, in arrival order.
- **Hold while busy:** when a Mailbox Delivery is due but its target session has a turn in progress (its per-node turn lock, FR.14.9, is held), the scheduler leaves it pending and re-checks it on the next poll instead of queueing on the lock. Every result that arrives during a long supervisor turn therefore lands in the same delivery, which fires on the first poll after the supervisor becomes idle. Other schedules keep FR.14.9's wait-for-the-lock behavior.
- **Persistence:** `ScheduleModel.IsMailboxDelivery` (bool, default `false`) is persisted with the schedule (FR.14.8); schedules persisted before this change load as ordinary schedules.
- **Tool description:** `send_to_session`'s `mode` description states that `async` results arriving while the caller is busy are delivered together in one turn.

**FR.15.13 — Session listing: scope, filters, paging and cost:** `list_sessions` returns one bounded page of sessions, newest first, instead of the whole tree. Split out of ISSUE.013 (R8): by 2026-11-03 the unfiltered listing had reached 341,807 characters for 2,686 sessions — past the CLI's tool-output limit — and every call parsed all session files (51.5 MB) while holding the settings lock.

- **Arguments** (all optional):

  | Arg | Default | Meaning |
  |---|---|---|
  | `scope` | `project` | `project` — sessions under the caller's Directory Node; `all` — every Directory Node; `children` — the direct workers of `nodeId`; `subtree` — every descendant of `nodeId` |
  | `nodeId` | the caller | Root session for `children`/`subtree` (full or Short Node Id); ignored by `project`/`all` |
  | `query` | — | Case-insensitive substring of the session name or of a group name on its tree path, or a prefix of its NodeId; surrounding whitespace is ignored |
  | `running` | — | `true` — only sessions with a turn in progress; `false` — only idle ones |
  | `activeWithinHours` | — | Only sessions whose Last Activity is within the last N hours (N > 0) |
  | `includeWorkers` | `false` | `project`/`all` only: list every Worker Session as a row instead of only counting it on its supervisor's row |
  | `tags` | — | Array of at most 30 tags in the FR.24.4 input forms (URL, `HSE-230`, `owner/repo#215`, `owner/repo`); only sessions whose top-level session (itself, or for a Worker Session the top-level supervisor it rolls up to, FR.24.12) carries ANY of them are rows (OR). Combines with every other argument; an empty array (or only blank entries) means no filter. An entry that is no tag form is a tool error: `Not a tag: '<entry>' - not a Linear issue, GitHub pull request or repository.` (FR.24.15) |
  | `limit` | 30 | Rows per page, clamped to 1–200 |
  | `offset` | 0 | Rows to skip; a negative value counts as 0 |

  An unknown `scope`, an unresolvable `nodeId`, or a non-positive `activeWithinHours` is a tool error.
- **Which sessions are rows:** in `children`/`subtree` the candidates are the root's direct workers / all its descendants. In `project`/`all` they are the scope's top-level sessions, the caller and the caller's direct workers (wherever they live), and — when `includeWorkers` is true or `query` is set — every Worker Session in the scope. `query`, `running` and `activeWithinHours` then test each candidate on its own.
- **Order:** newest Last Activity first, ties by name and then NodeId; sessions whose file is missing come last. A row whose supervisor's row is also listed goes directly under it, indented one level per nesting step, siblings newest first — so by default the caller's own workers sit under the caller's row. In `all` scope the remaining top-level rows are grouped by Directory Node, groups ordered by their newest row.
- **Paging:** `offset`/`limit` apply to the ordered rows, nested rows included.
- **Last Activity:** the session file's last-write time (UTC). The listing never reads session-file contents.
- **Output:** plain text. The listing's own words, flags and separators are ASCII; session, group and directory names are printed as they are, except that control characters (line breaks included) become spaces, so every row stays on one line.
  - First line: `You: <id> "<name>" - top-level session in <label>`, or for a worker `You: <id> "<name>" - worker (depth <n>) of <id> "<name>" in <label>` (the `of …` part is dropped when the supervisor is no longer in the tree).
  - Section headers: `<label> (<path>) - <t> top-level sessions, <w> workers` per Directory Node in `project`/`all` (counts cover the whole directory, before filters); `Workers of <id> "<name>" - <d> direct, <s> in subtree` in `children`/`subtree`. The header of a page's first section is always printed.
  - Rows: `<indent><id>  <name>  <age>`, then only the flags that apply, in this order:
    - `running`;
    - `fav` — a Favorite Session (FR.9.7);
    - `no-resume` — no resumable Claude session (no `ClaudeSessionId`, or its JSONL is in no profile, FR.11.13): the next turn starts a fresh Claude session seeded from the transcript;
    - `<n> workers` or `<n> workers (<k> running)` (`1 worker` when there is one) — its direct workers anywhere in the tree;
    - `worker of <id>`, or `worker of you` — a worker not placed under its supervisor's row on this page (the supervisor is not listed, or the page starts inside its block) whose supervisor is not the scope root;
    - `[<label>]` — the row lives in another Directory Node than its section;
    - `(you)` — the caller's own row.

    Indent is two spaces per level; `<id>` is the Short Node Id; names are cut to 60 characters ending in `...` wherever the listing prints them (rows, the `You:` line, headers); directory paths drop a trailing separator unless they are a drive root; `<age>` is the Last Activity relative to now — `now` (under a minute), `<n>m`, `<n>h` (under a day), `<n>d` (under 14 days), `<n>w` (under 8 weeks), `<n>mo` (under a year), `<n>y` — or `-` when the session file is missing.
  - Footer: `Rows <a>-<b> of <n>.`, plus `Next page: offset=<b>.` when rows remain, then the hints that apply: `Other projects: scope=all.` (`project` scope), `Workers: scope=children nodeId=<id>.` (a listed row has workers that are not shown), `Narrow: query=, running=, activeWithinHours=.`. With no rows: `No sessions match.` and the same hints — or, when `offset` is past the end of a non-empty result, `No rows at offset=<o>; <n> in total.`
  - **Bound:** at most 200 rows and 60-character names per page keep even the largest page well inside the CLI's tool-output limit; the default page is typically 1–2 KB.
- **Short Node Ids:** the `You:` line, headers and rows print the shortest prefix of a NodeId — at least 8 characters — that no other NodeId in the tree shares. Every tool argument that takes a session id (`nodeId` of `send_to_session`, `read_session`, `stop_session`, `set_session_model` and `list_sessions`; `target` of `schedule_wake`; `parentNodeId` of `spawn_session`) accepts the full NodeId or any prefix of at least 6 characters that matches exactly one session, case-insensitively, ignoring surrounding whitespace. A shorter prefix is an error; an ambiguous one is an error listing up to 5 matches (full NodeId and name). An id matching no session is a not-found error, except for `stop_session`, which passes it on unchanged so the running turn of a session removed from the tree can still be cancelled. Tool results keep echoing full NodeIds.
- **Cost:** the settings lock (`IAppSettingsService.SyncRoot`) is held only while session fields are copied; last-write times and resumability are read after it is released. Resumability is checked only for the rows on the returned page, against one list of profile `projects` directories resolved per call. `BuildAgentSessionSummaries` (Telegram `/sessions` and `/open`, FR.21.8) follows the same lock and Last Activity rules and still returns every session.
- **Tool description:** states the defaults (the caller's project; the caller and its own workers; newest first; 30 rows per page), the flags, and that Short Node Ids are accepted wherever a session id is.

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

A plan without a cap on a given window (e.g. an Ultra-tier plan has no 5-hour session limit) reports that window as `"five_hour": null` (a JSON null, not an omitted key). `ClaudeUsageService.ParseUsage` treats a missing or null `five_hour`/`seven_day` as "0%, no limit" for that window alone — it never lets one capless window discard an otherwise-valid reading for the other window. The parse only fails (returns no usage data) if *neither* `five_hour` nor `seven_day` is present at all.

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

A bidirectional bridge between Tessyn sessions and a Telegram bot, letting a session reach the user away from the desktop and letting the user reply to, browse, message, and operate sessions from Telegram. Each session's conversation mirrors into that session's own topic — by default the final answer of each turn, or everything in Full mode (FR.21.9, FR.21.22) — so a topic is a window onto the session, not just a notification channel. The bridge never announces that a turn finished: only `notify_user` (FR.21.3) makes a sound or mentions the user — and shows a desktop toast with the same message (FR.21.5) — while everything else is posted silently (FR.21.21); see ISSUE.012.

**FR.21.1 — Data model:** `AppSettingsModel` gains:

| Field | Type | Notes |
|---|---|---|
| `TelegramEnabled` | bool | Master toggle. When false, the bridge does not start and all bot calls are no-ops. |
| `TelegramChatId` | long | The forum-topic supergroup's chat ID. Every inbound update from any other chat is dropped. |
| `TelegramAllowedUserIds` | `List<long>` | Telegram numeric user IDs authorized to message the bot. Everyone else's messages are logged and dropped, even inside the correct chat. |
| `TelegramTopicThreadIdsByNodeId` | `Dictionary<string, int>` | Persisted NodeId → Telegram `message_thread_id` map. The reverse lookup (thread → NodeId) is rebuilt in memory from this on `Start()`. |
| `TelegramTopicNamesByNodeId` | `Dictionary<string, string>` | Persisted NodeId → last-applied topic name (untruncated), recorded at topic creation (FR.21.2) and by topic name reconciliation (FR.21.18). Defaults to empty. No code path currently removes thread-id mappings; any future removal must also remove the corresponding entry here (FR.21.18). |
| `TelegramUpdateOffset` | long | Last-processed Telegram `update_id` + 1, persisted so a restart does not replay already-handled updates. |
| `TelegramMentionHandle` | string | @handle (e.g. `@harmlessevil`) prefixed onto `notify_user` messages (FR.21.3, FR.21.10). Empty = no prefix. Never prefixed onto mirrored conversation text (FR.21.9) or command replies, so routine back-and-forth doesn't page the user on every line. |
| `TelegramMirrorMode` | `TelegramMirrorMode` enum: `Full`, `AnswersOnly`, `Off` | What the Universal I/O Mirror posts (FR.21.22). Defaults to `AnswersOnly`. |

The bot token itself is **not** in `AppSettingsModel` — see FR.21.6.

**FR.21.2 — Topic-per-session, created lazily on first activity:** The first time a session runs a turn — interactive (`TurnController.SendAsyncCore`) or headless (`SessionTurnService.RunTurnAsync`, which also covers scheduled, orchestrated, and Telegram-triggered turns) — while `TelegramEnabled` is true, `ITelegramBridgeService.EnsureTopicForSessionAsync` fire-and-forget-creates a Telegram forum topic named `"{directory label} / {session name}"` via `createForumTopic` and persists the NodeId → thread mapping. The directory label follows FR.1.3, derived from the session's working directory (e.g. `HardStop / Fix login`); the name actually applied is recorded as the last-applied topic name (`TelegramTopicNamesByNodeId`, FR.21.1). Names are truncated to 128 characters (`Constants.Telegram.MaxTopicNameLength`) when sent. Sessions never touched while the bridge is enabled never get a topic — there is no bulk backfill over session history. Errors (offline, bot misconfigured) are logged and swallowed; a topic-creation failure never fails or delays the turn it was piggybacked on.

**FR.21.3 — Outbound: notifications and questions:** A new `mcp__cmx__notify_user` tool (`AgentMcpServer`) lets a running session send an arbitrary message to the user's Telegram, ensuring its topic exists first. There is deliberately **no blocking "ask and wait" tool** — a `TaskCompletionSource`-style block risks getting stuck indefinitely on Telegram connectivity issues or the user simply being away from their phone, with no clean way to time out a mid-turn CLI process. Instead, `Constants.Instructions.TelegramNotifyGuidance` (emitted once per fresh session, alongside `NoWorkflowTool`, when `AgentToolsEnabled && TelegramEnabled`) instructs Claude to call `notify_user` with the question in plain text and then end its turn normally — the user's reply becomes the next ordinary prompt via FR.21.4, resuming the same session. The message is posted with the configured `TelegramMentionHandle` prefix (FR.21.10) so it stands out among the mirrored conversation text (FR.21.9) sharing the same topic. It is the only message the bridge sends with notification sound (FR.21.21), and — together with its desktop toast (FR.21.5) — the only way a session reaches the user as a notification at all. **Amended by FR.23:** the guidance text now also mentions `post_attention` (`critical` items and `notify: true` ping the same channels) and recommends it over `notify_user`; every top-level `notify_user` call is additionally captured as an `Ask` Attention Item (FR.23.7, text in FR.23.10).

**FR.21.4 — Inbound routing:** `ITelegramBridgeService` long-polls `getUpdates` (30s timeout, offset persisted per FR.21.1) while started. For each accepted update (chat ID + user ID both allow-listed):
- A message whose `message_thread_id` matches an entry in the topic map runs a turn on that NodeId via `IDaemonClient.RunTurnAsync(node, text, TurnSource.Telegram)`. The routed message itself is not re-echoed back into its own topic (FR.21.9's echo suppression), and the turn's output reaches the topic via the same universal mirror every other turn source uses — there is no separate "post the reply" step.
- A message with attachments (FR.21.13–FR.21.16) is routed the same way; its turn text is the message text plus attachment lines (FR.21.15).
- `/stop`, sent inside a session's own topic, cancels that session's in-flight turn via `IDaemonClient.CancelTurn` (FR.21.11) and confirms whether a turn was actually cancelled.
- `/sessions` replies with sessions where `IsFavorite` is true or the session already has a topic (mirrors the recent-dropdown favorite rule, FR.9.7, without requiring the 24-hour recency computation the bridge has no cheap access to), each line showing running state.
- `/open <NodeId prefix or name>` replies with that session's last 20 entries (reusing the `read_session` tool's logic) without requiring the session to already have a topic.
- Anything else (unmapped thread, unrecognized command) gets a short "unrecognized" reply.
- Every command reply above is sent silently (FR.21.21).

**FR.21.5 — Notification channels (changed by ISSUE.012):** Turn completion is never announced — not in Telegram and not on the desktop. Until ISSUE.012, every completed scheduled turn (including each async worker result delivered to a supervisor, FR.15.3, and each self-wake) called `INotificationService.ShowResult`, which `CompositeNotificationService` turned into a mention-prefixed `✅ finished` Telegram ping plus a desktop toast; on 2026-10-28 that produced 22 tagged pings, 18 of them noise. Now the desktop toast delivers exactly what rings in Telegram — `notify_user` messages:
- **Fan-out:** `AgentMcpServer`'s `notify_user` handler drops a Worker Session's call before any channel (FR.21.19), then sends the message to the Telegram Bridge (`ITelegramBridgeService.NotifyAsync`, gated on `TelegramEnabled`; mention prefix and sound, FR.21.10, FR.21.21) and to the desktop toast (`INotificationService.ShowUserNotification`, gated on `NotificationsEnabled`), each independently of the other. The tool result names the channels that delivered (e.g. `Notification sent (Telegram, desktop).`) or says that none did.
- **Toast:** the title is the session's topic name (`{directory label} / {session name}`, FR.21.2), so a toast and its Telegram ping read the same; the body is the message, truncated to `Constants.Notifications.MaxBodyChars`. It is shown whether or not the session is selected, and clicking it selects the session (the existing click-to-activate wiring in `App.axaml.cs`).
- **Code shape:** `INotificationService.ShowResult` is replaced by `ShowUserNotification(nodeId, title, message)`, which returns whether a toast was shown. `CompositeNotificationService` is deleted — `WindowsNotificationService` is registered as `INotificationService` directly — and `SchedulerService` no longer depends on `INotificationService` at all, so it reports nothing when a scheduled turn finishes (its selected-session suppression goes with it).
- **Setting:** `NotificationsEnabled` keeps its Settings checkbox, relabelled "Show a desktop notification when a session asks for you", with a hint that it shows the same messages that ping Telegram.
- Sessions are told about `notify_user` only when the Telegram Bridge is enabled (FR.21.3, unchanged).
- **Shared by FR.23:** `post_attention` notifications (FR.23.4) go through this same fan-out, via one private helper shared with `notify_user` (FR.23.9).

**FR.21.6 — Bot token storage:** The bot token is never written to `appsettings.json`. It is DPAPI-protected (`CurrentUser` scope) at `%AppData%\TessynDesktop\telegram-token.dat` via a dedicated `ITelegramTokenStore`, mirroring the MCP Token Store (FR.20.5) but as a single file, since there is exactly one bot. Settings UI writes it immediately on a dedicated Save action, independent of the main Settings Save button, for the same reason FR.20.7's registry actions persist immediately.

**FR.21.7 — Settings UI:** A Telegram panel (Settings window) with: enable toggle, bot token entry (masked) + its own Save action, chat ID field, a comma-separated allowed-user-IDs field, a mention-handle field (FR.21.10), and a mirror-mode dropdown (Full / Answers only / Off, FR.21.22). The latter four are held in the normal copied ViewModel state and commit on the window's main Save button, like most other Settings fields.

**FR.21.8 — Session summary extensions:** `SessionSummaryModel` (used by `/sessions` and `/open`) gains `IsFavorite` and `LastActivityUtc`, populated by `SessionRegistry.BuildSummary`. As of FR.15.13, `LastActivityUtc` is the session file's last-write time — the file's contents are no longer read — and the former `LastPrompt` field (the last user entry's timestamp) is removed; `list_sessions` uses its own paged listing (FR.15.13) rather than this model.

**FR.21.9 — Universal I/O mirror:** In `Full` mirror mode (FR.21.22; the default `AnswersOnly` mode posts only each turn's final answer), every session's conversation mirrors into its Telegram topic as it happens — **except Worker Sessions**, excluded outright in every mode (FR.21.19) — regardless of which of the two turn-execution paths produced it — the interactive desktop path (`TurnController` driving `IClaudeProcessManager` directly, for live streaming) and the headless path (`SessionTurnService.RunTurnAsync`, used by the scheduler, orchestration, and Telegram-triggered turns themselves) both write every message through the single `ISessionFileService.AppendMessage`/`AppendCompactionSeparator` sink. `EntryAppended` — raised by that sink and relayed through `IDaemonClient.EntryAppended` per FR.21.10 — is the one tap point the bridge subscribes to; no per-turn-path integration is needed. Per appended entry:
- `ROLE=USER` content is posted with a `USER:` prefix — the Telegram Bot API cannot post a message as anyone but the bot itself, so this is the closest available approximation of "as written by me" for desktop-originated input.
- `ROLE=ASSISTANT` content posts with no prefix.
- `ROLE=SYSTEM` content (errors, phase/log markers, mid-run toggle notes) posts with a `⚙️` prefix.
- `ROLE=COMPACTION` separator entries (empty content) are not mirrored.
- Messages over Telegram's ~4096-character limit are chunked, preferring to split on a newline boundary.
- **Echo suppression:** the one `ROLE=USER` entry `SessionTurnService.RunTurnAsync` appends for a `TurnSource.Telegram` turn — the same text the user just typed into the topic — is suppressed rather than mirrored back, matched by NodeId + exact content against a short-lived flag set immediately before the routed turn starts.
- Raw tool-call/thinking events (visible only in the Claude JSONL, not the text Session File) are not mirrored — out of scope for this pass; deferred to a future richer/expandable view.
- A Mailbox Delivery's `ROLE=USER` entry (the worker results handed to a Supervisor Session) is posted as one line, not in full (FR.21.23).
- Every mirrored message is sent silently (FR.21.21).

**FR.21.10 — Bridge as a daemon client:** `TelegramBridgeService` depends on `IDaemonClient` (`FindSessionByNodeId`, `FindSessionByFileName`, `RunTurnAsync`, `CancelTurn`, `ReadEntries`, `BuildAgentSessionSummaries`, `EntryAppended`, and `TurnCompleted` since FR.21.22) instead of reaching into `ISessionRegistry`/`ISessionTurnService`/`ISessionFileService` directly — it is a peer of the Avalonia UI on the daemon boundary (`docs/refactoring/tdcp-spec.md`), not a registry-coupled service. `IDaemonClient.EntryAppended` and `FindSessionByFileName` (added to `ISessionRegistry`/`IDaemonClient` for this) are the two additions this required; `InProcDaemonClient` relays the former from `ISessionFileService` exactly as it already does for `TreeChanged`/`ScheduleChanged`. `IDirectoryLabelService` (topic naming) and `IAppSettingsService`/`ITelegramClient` (settings/transport) remain direct dependencies — stateless utilities outside the daemon-owned turn/session domain, matching the carve-outs `tdcp-spec.md` §5 already makes for the UI. `notify_user` messages — the bridge's only outbound pings since FR.21.5 stopped announcing turn completion — are prefixed with `TelegramMentionHandle` and sent with sound (FR.21.21), so they read as a ping directed at the user rather than more mirrored conversation text.

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

**FR.21.21 — Silent delivery:** `ITelegramClient.SendMessageAsync` gains a `disableNotification` parameter, sent as the Bot API's `disable_notification` field. It has no default value, so every call site states its intent. The bridge passes `false` only for `notify_user` messages (FR.21.3); everything else — Universal I/O Mirror posts (FR.21.9, FR.21.22, FR.21.23) and the replies to `/sessions`, `/open`, `/stop` and unrecognized input (FR.21.4) — is sent with `true`. Telegram still delivers and counts a silent message, but the user gets a notification with no sound, so the group can stay unmuted and only a deliberate `notify_user` ping is heard. Silence is decided per message by the bridge; there is no setting for it (ISSUE.012).

**FR.21.22 — Mirror modes and the Turn Completed event:** `AppSettingsModel.TelegramMirrorMode` (FR.21.1) selects what the Universal I/O Mirror posts:
- `Full` — every entry, as FR.21.9 specifies (with FR.21.23's one-line Mailbox Deliveries).
- `AnswersOnly` (default) — only the final `ROLE=ASSISTANT` entry of each turn. `ROLE=USER` and `ROLE=SYSTEM` entries, and the text blocks a turn emits between tool calls, are not posted.
- `Off` — nothing is mirrored; `notify_user`, inbound routing and commands keep working.

FR.21.9's prefixes, chunking and echo suppression apply to whatever is posted, Worker Sessions are never mirrored in any mode (FR.21.19), and a mode change applies from the next entry on.
- **Turn Completed event:** a turn's final entry is only known once the turn ends, so both turn paths report the end. `ISessionFileService.NotifyTurnCompleted(fileName, cancelled, errorText)` raises a new `ISessionFileService.TurnCompleted` event (it writes nothing to the file), relayed by `IDaemonClient.TurnCompleted` exactly as `EntryAppended` is (FR.21.10). `errorText` is the content of the turn's error `result` event — the same text both paths already append as a `ROLE=SYSTEM` entry, e.g. `You've hit your session limit · resets 1:50pm` — or `null` when the turn did not end in an error. Errors a turn path retries automatically ("No conversation found", the ISSUE.010 OAuth refresh race) are not error text. It is called once per turn, after the turn's last entry has been appended: by `SessionTurnService.RunTurnAsync` for every headless turn (scheduled, orchestrated, Telegram), including one routed into a live streaming process (FR.19.13), and by `TurnController.SendAsyncCore` for every interactive send, in both One-shot and Streaming Session Mode. A turn ends `cancelled` when it is stopped (desktop Stop, `/stop`, `stop_session`, the scheduled-turn timeout); any other end, including an error result such as a usage-limit message, is a completed turn.
- **AnswersOnly behavior:** the bridge keeps, per session, the most recent `ROLE=ASSISTANT` entry not yet posted; each new one replaces it. On `TurnCompleted` for that session it posts the kept entry (if any) and clears it; on a cancelled turn it clears it without posting. **Turn errors stay visible:** when `TurnCompleted` carries error text, the bridge then posts that text, worded exactly as the CLI reported it, with the `⚙️ ` prefix — unless it is identical (ignoring surrounding whitespace) to the entry it just posted, because the CLI usually reports a usage limit both as a synthetic assistant message and as the error result. A session that hits its usage limit therefore always shows the limit text in its topic, once and silently (Architect, 2026-10-28; in `Full` mode both entries are mirrored, as before). An entry appended outside any turn (e.g. the JSONL write-back of a turn run outside Tessyn, ISSUE.009) is not posted on its own — the next turn's answer supersedes it.

**FR.21.23 — Mailbox Deliveries in Full mode:** In `Full` mode, a `ROLE=USER` entry that is a Mailbox Delivery (FR.15.3, FR.15.12 — it contains one or more `[Worker finished] <name>` lines) is mirrored as its first line only, with the `⚙️ ` prefix (e.g. `⚙️ [Scheduled] Worker results (2): W2, W3`), instead of `USER:` plus the workers' full results. Those results are input for the Supervisor Session, whose own reply summarises them; on 2026-10-28 they made up 34 of 209 mirrored messages (ISSUE.012). In `AnswersOnly` mode `ROLE=USER` entries are never posted.

**FR.21.24 — Scheduled-turn notify reminder:** `InstructionOptionsModel` gains `IsScheduledTurn`, set by `SessionTurnService.RunTurnAsync` at both instruction-block call sites (one-shot and the FR.19.13 streaming redirect, like `IsWorkerSession`, FR.21.20) when the turn source is `TurnSource.Scheduled` — Delayed Sends, Self-Wakes, other `schedule_wake` targets and Mailbox Deliveries. When `IsAgentToolsEnabled && IsTelegramEnabled && IsScheduledTurn && !IsWorkerSession`, `InstructionBlockBuilder.Build` adds `Constants.Instructions.ScheduledTurnNotifyReminder` to the hidden instruction block (FR.11.2 — never written to the session file) on **every** such turn: unlike `TelegramNotifyGuidance` it describes this turn, not session policy, so the once-per-fresh-session gate does not apply. The reminder says the scheduler started this turn, the user may not be watching and is not told when it finishes, so if — and only if — the reply needs the user to decide or act, call `notify_user` once with that ask, and otherwise don't. **Amended by FR.23.10:** the reminder now points at `post_attention` (with `notify: true` if the user must act soon) and tells the session not to restate items that are already pinned. Rationale: on 2026-10-28, 2 of the 4 moments the user was needed reached them only through the completion ping FR.21.5 used to send (ISSUE.012).

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

A per-session, opt-in audible cue for the interactive command bar — the only turn-completion signal Tessyn gives, since neither the desktop toast nor Telegram announces turn completion any more (FR.21.5, ISSUE.012). It covers interactive turns only, deliberately (Architect, 2026-10-28): a turn started by the scheduler, by orchestration or from Telegram finishes without a sound, and a session that needs the user calls `notify_user` (FR.21.3, FR.21.24).

**FR.22.1 — Trigger point:** The sound fires when `TurnController.SendAsyncCore` clears the "Claude is thinking…" state on natural completion of the last concurrent send (the same point that stops the thinking timer and sets `IsBusy`/`IsRunning` false) — for both One-shot Mode (the spawned process exits) and Streaming Session Mode (`SendViaStreamingAsync` returns on the result event). It does not fire when the turn ends via user-initiated Stop (`OperationCanceledException` path) or any other non-natural termination.

**FR.22.2 — Per-session toggle:** `SessionNodeModel.PlayCompletionSound` (bool, default `true`), persisted like `IsStreamingMode`. Exposed via a bell-glyph (🔔) `ToggleButton` in the command bar, to the right of the Streaming Session Mode toggle. To fit three toggle buttons (`--chrome`, Streaming, Completion Sound) in the available space, all three are rendered as fixed-size squares (26×26, no padding) with reduced inter-button spacing and explicit `HorizontalContentAlignment`/`VerticalContentAlignment="Center"` (needed once `Padding="0"` is set, since Avalonia's default `ToggleButton` content alignment otherwise renders the glyph in the top-left corner of a fixed-size square), replacing their previous wider `Padding="7,2"` rendering.

**FR.22.3 — Per-session throttle:** No more than one sound per session within a configurable window (`AppSettingsModel.SessionCompletionSoundThrottleSeconds`, default 180 = 3 minutes). Enforced by `ISessionSoundService` (app-lifetime singleton) tracking last-played timestamp per `NodeId` in memory — a session that finishes multiple turns in quick succession (e.g. rapid follow-up prompts) is not spammed with sounds; a session that has been quiet for longer than the window is not blocked from playing.

**FR.22.4 — Sound source and playback:** `AppSettingsModel.SessionCompletionSoundFile` (string, default `%WINDIR%\Media\Windows Notify.wav`) names a `.wav` file. `ISessionSoundService` plays it via the Win32 `winmm.dll` `PlaySound` API (`SND_ASYNC | SND_FILENAME`) — chosen over `System.Media.SoundPlayer` or a NuGet audio package to avoid a project-reference change. Playback failures (missing file, no audio device) are logged and swallowed; they never fail or delay a turn.

**FR.22.5 — Sound picker:** Settings → Notifications gains a "Session completion sound" control group: a dropdown populated by `ISessionSoundService.GetAvailableSounds()` (every `.wav` under `%WINDIR%\Media\`, i.e. the Windows default sound library), a **Test** button that plays the selected sound immediately (bypassing the throttle), and a throttle-seconds numeric field bound to `SessionCompletionSoundThrottleSeconds`.

---

### FR.23 — Focus View (Attention Items)

Sessions have no record that the user has seen an ask, so they re-raise it: in the "HSE-150 Android backend" session one ask (the production app's open release switch) was restated 6 times in 50 minutes of scheduled turns, and the Architect finally had to ask "tell me about the urgent issue"; on 2026-11-02 a question went to Telegram only (`notify_user` writes nothing to the session file) and the Architect had to ask "what's the question, can you post here too?". The fix: sessions *post* items to a per-session list that Tessyn keeps (the Attention Ledger), a new **Focus** view shows it beside the unchanged raw output (the **Session** view), and every turn is told which items are already pinned. No extra LLM calls.

**FR.23.1 — Attention Items and kinds:** `AttentionKind`, from most to least severe. Any kind may carry answer options and a recommendation.

| Kind | Meaning | Badge colour (FR.23.13) |
|---|---|---|
| `Critical` | Something the user must know immediately. Posting one always notifies the user (FR.23.9). | DarkRed `#8B0000`, white number |
| `Ask` | The session needs the user's answer, decision or approval. | Goldenrod `#DAA520`, black number |
| `Todo` | Something only the user can do: rotate a password, add a DNS record, merge a PR. | Background = the theme's foreground colour; number in the theme's background colour |
| `Alert` | Worth knowing; no reply needed. | Transparent background; number in the normal foreground colour |
| `Decided` | A call the session made for the user so work isn't blocked; the user may override it. | Not counted in the Focus badge. Counted in the session-row badge (FR.23.17): grey `#707070`, white number, when it is the only open kind |

**FR.23.2 — Item fields, ids and statuses:**
- **Fields:** id, kind, title (one line), body (markdown, self-contained), options, recommendation, key, status, source, answer, resolution note, user-close-unreported flag, created/updated/closed timestamps (`AttentionItemModel`).
- **Ids:** `A{n}` per session. `n` comes from the ledger's `NextNumber`: it starts at 1, increments on every new item and is never reused.
- **`AttentionStatus`:** `Open` — active. `Answered` — the user answered it, either from the Focus view or in conversation, recorded by the session through `resolve_attention`. `Resolved` — done or no longer applies; the session closed it. `Dismissed` — the user closed it from the Focus view without an answer (Dismiss or Got it, FR.23.15), whatever its kind.
- **`AttentionSource`:** `Tool` (`post_attention`) or `NotifyUser` (captured `notify_user`, FR.23.7).
- **Ordering**, used everywhere a list is shown: open items first, by kind severity (Critical, Ask, Todo, Alert, Decided — the enum order), then number ascending; then closed items, newest `ClosedUtc` first. The ordering helper and the summary computation (open count excluding Decided, most severe open kind) live in Core so the UI does not reimplement them.

**FR.23.3 — Attention Ledger storage:**
- **Location:** `%AppData%\TessynDesktop\attention\<NodeId>.json`. Folder name `Constants.Attention.FolderName = "attention"`, built the same way as `McpTokenStore`'s path (`Path.Combine(appData, Constants.AppDataFolderName, …)`). Not in `appsettings.json` (already about 2.6 MB and saved synchronously) and not in any database.
- **Keyed by NodeId**, so it survives compaction, detach and Cross-Profile Session Adoption.
- **Writes:** temp file then rename (atomic, like FR.4.3), serialised per service instance.
- **Loading:** lazily on first use, then cached in memory. A missing file is an empty ledger. An unreadable or corrupt file is logged as a warning, moved aside to `<NodeId>.json.bad-<yyyyMMddHHmmss>`, and the ledger starts empty — never overwritten silently.
- **Never deleted by Tessyn**, the same no-cascade policy as FR.1.7.
- The store takes its root directory in the constructor, so tests use a temp dir. `IAttentionService` is a Singleton (it owns the cache and serialises writes).

**FR.23.4 — `post_attention` tool (cmx MCP server):** Input schema:

```
items   (array, required, 1–30) of objects:
   kind            string, required: critical | ask | todo | alert | decided   (case-insensitive)
   title           string, required: one line (newlines → spaces), trimmed, cut to 200 chars; empty after trim = error
   body            string, optional (default ""): markdown, cut to 20,000 chars
   options         array of string, optional: ≤ 10, each trimmed, empty ones dropped, each cut to 200
   recommendation  string, optional: cut to 1,000
   key             string, optional: stable dedupe key chosen by the session, trimmed, cut to 100
notify  boolean, optional (default false)
```

Behaviour per item:
- **Dedupe:** among **Open** items, an item with the same `key` (case-insensitive, both non-empty) is the match. Failing that, an item with the same normalised title (trimmed, inner whitespace collapsed, case-insensitive) is the match. **Match:** the existing item is updated (kind, title, body, options, recommendation, plus the key if one is given; `UpdatedUtc`); its id doesn't change, and the update is reported as "already pinned". **No match:** a new item `A{NextNumber}` is created.
- **Validation:** an invalid kind, an empty title or an empty `items` array is a tool error (`McpToolException`) naming the problem. Nothing is posted if any item is invalid.
- **Session file:** each created or updated item gets a SYSTEM entry (FR.23.8).
- **Notification:** at most one per call. It is sent when at least one item was **created** and either a created item is `Critical` or `notify` is true. Updates never notify (delivery: FR.23.9).

Tool description (this wording):

> Pin items in the user's Focus view, next to this session's output, where they stay visible until resolved. Kinds: critical (the user must know immediately — always notifies them), ask (you need their answer or approval), todo (something only they can do), alert (worth knowing, no reply needed), decided (a call you made for them that they may override). Write each body so it stands on its own. Post an item once: re-posting an open item with the same key or title updates it instead of adding a copy. Don't restate pinned items in later replies — refer to them by id (A1, A2, …). Set notify=true to also ping the user's Telegram and desktop, only when they must act soon. Top-level sessions only — a worker's call is refused; report to your supervisor instead.

Result text, one line per item, in order:
- `A12 posted (critical).`
- `A7 already pinned (ask) - updated, not re-posted.`

Then, only if a notification was attempted: `Notified: Telegram, desktop.` (the channels that delivered) or `Notification not sent - neither Telegram nor desktop notifications are enabled.`
Then always: `Pinned items stay in the user's Focus view until resolved - don't restate them; refer to them by id.`

**FR.23.5 — `resolve_attention` and `list_attention` tools:**

`resolve_attention` — input: `id` (string, required, case-insensitive, e.g. `a12`); `status` (string, required: `answered` | `resolved`); `note` (string, optional, cut to 2,000). `answered`: the user answered it in conversation; sets `Answer` = note. `resolved`: done, withdrawn or no longer applies; sets `ResolutionNote` = note. Writes a SYSTEM entry (FR.23.8). Results:
- `A12 marked answered.` or `A12 resolved.`
- If already closed: `A12 is already <status lower-case>.` (no change, no SYSTEM entry).
- Unknown id: tool error `Unknown attention item 'A99'.`

Description:

> Close a pinned Focus item: status=answered when the user answered it in conversation (note = their answer), status=resolved when it is done or no longer applies (note = why).

`list_attention` — input: `status` (string, optional: `open` (default) | `all`). Output is plain text, one line per item, ordered as in FR.23.2:
- open item: `A12 [critical, open] <title>`
- closed item: `A3 [ask, answered] <title> - answer: <answer>`, `A4 [todo, resolved] <title> - <note>`, or `A5 [alert, dismissed] <title>`
- empty: `No open items.` (open) or `No items.` (all)

Description:

> List this session's Focus items — open (default) or all — with ids, kinds, statuses and answers.

**FR.23.6 — Worker Sessions refused:** A Worker Session (`OrchestrationDepth > 0`) calling `post_attention` or `resolve_attention` gets a normal text result (not an error), and nothing is stored or written:

`Not posted - worker sessions can't post to the user's Focus view. Report to your supervisor instead.`

This matches the `notify_user` policy (FR.21.19). `list_attention` returns `No items.` for a worker.

**FR.23.7 — `notify_user` capture:**
- Every `notify_user` call from a top-level session also posts an item: kind `Ask`, source `NotifyUser`. Its title is the message's first non-empty line (trimmed, cut to 200); its body is the full message. The same dedupe and SYSTEM entry as `post_attention` apply.
- The capture never sends a second notification: `notify_user` keeps its own Telegram and desktop fan-out unchanged (FR.21.3, FR.21.5).
- Its result text gets ` Pinned as A12.` appended, or ` Already pinned as A12.` on a dedupe update.
- Worker calls stay dropped exactly as today (FR.21.19): no item.

**FR.23.8 — Session-file records:**
- **SYSTEM entries.** The service appends these through `ISessionFileService.AppendMessage` with `Constants.SessionFile.RoleSystem`, the way `workflow_phase` does. ASCII only.
  - Created: first line `[Attention A12 (critical)] <title>`, then the body (if any) on the following lines, then `Options: <o1> | <o2>` (if any), then `Recommendation: <text>` (if any).
  - Updated: the same layout, with first line `[Attention A12 (ask) updated] <title>`.
  - Session close: `[Attention A12 answered] <note>` or `[Attention A12 resolved] <note>`. With an empty note, there is no trailing space.
  - User close from the Focus view, any kind: `[Attention A12 dismissed by the user]`.
  - User answers from the Focus view write **no** SYSTEM entry: the USER prompt (FR.23.15) is the record.
- **Tool lines.** `ClaudeSessionImportService.ExtractToolDescription` (the JSONL → `[Tool: …]` line) learns three new input shapes: `message` is added to the generic string fallback list, so the `notify_user` line shows its text, cut to 120 like the others; an `items` array of objects with `title` → `<n> item(s): <title1>; <title2>`, cut to 120; `id` + `status` strings → `<id> <status>`.

**FR.23.9 — Notifications:**
- When FR.23.4 calls for a notification, it goes through exactly the fan-out `notify_user` uses (FR.21.5): Telegram `NotifyAsync` (mention and sound) plus the desktop toast, each gated as today (`TelegramEnabled`, `NotificationsEnabled`). That fan-out is extracted into one private helper shared by both tools; it is not duplicated.
- Message text: one created item: `A12 (critical): <title>` + blank line + body; several created items: `<n> new items in the Focus view:` then one `- A12 (critical): <title>` line each.
- Worker Sessions never notify (FR.23.6).

**FR.23.10 — Guidance (hidden instruction block, once per fresh session):**
- New `Constants.Instructions.AttentionGuidance`, emitted when `IsAgentToolsEnabled && IsNewSession && !IsWorkerSession`. It is session policy, so it uses the same gate as `NoWorkflowTool`. The `mcp__cmx__` prefix is built from `Agent.McpServerName`, as the existing constants do. Text:

  > The user has a Focus view beside this session's output. Pin anything they need there with `mcp__cmx__post_attention`: `critical` — they must know immediately (it notifies them at once); `ask` — you need their answer, decision or approval; `todo` — something only they can do; `alert` — worth knowing, no reply needed; `decided` — a call you made for them so work isn't blocked, which they may want to override. Write each item so it stands on its own: the user may read it days later without this conversation. Post each item once — it stays pinned until resolved, so don't restate pinned items in later replies; refer to them by id. When the user answers an item in conversation, or it no longer applies, call `mcp__cmx__resolve_attention`.

- **Amends `TelegramNotifyGuidance` (FR.21.3).** Gate unchanged. New text:

  > A Telegram bridge is available: `critical` items, and items you post with `notify: true`, also ping the user's Telegram and desktop. Use `notify: true` only when the user must act soon — never for progress updates — and only from this top-level session, never from a worker or Task-tool subagent (report back to your supervisor instead). `mcp__cmx__notify_user` still sends a plain message and is also pinned as an ask, but prefer `post_attention`. There is no blocking "ask and wait" tool — after posting an ask, end your turn normally instead of waiting idle for a reply. The user's reply arrives later as an ordinary new prompt in this same session.

- **Amends `ScheduledTurnNotifyReminder` (FR.21.24).** Gate unchanged. New text:

  > The scheduler started this turn, so the user may not be watching and is not told when it finishes. If — and only if — your reply needs the user to decide or act and it isn't pinned yet, post it with `mcp__cmx__post_attention` (`notify: true` if they must act soon). Don't restate items that are already pinned.

- **Amends the `cmx-orchestration` skill (`Constants.Agent.OrchestrationSkillMarkdown`, FR.15.11).** Supervisor section: where it says to call `notify_user` once when the user must decide or act, it says instead to post the ask once with `post_attention` (`notify: true` when they must act soon); it stays pinned, so don't restate it in later delivery turns. Worker section: don't call `notify_user` **or** `post_attention`; report to the supervisor.

**FR.23.11 — Pinned list (hidden instruction block, every turn):**
- **Gate:** emitted when `IsAgentToolsEnabled && !IsWorkerSession` and the session has open items **or** unreported user closures. It describes this turn's state, so it is **not** gated on `IsNewSession`, like FR.21.24.
- Exact lines appended to the block, after the existing lines. `<…>` are placeholders. The two-space indent on the item lines is part of the format.

```
- Pinned in the user's Focus view - they can see these, so don't restate them; refer to them by id, and call `mcp__cmx__resolve_attention` when one is answered in conversation or no longer applies:
  - A12 critical: <title>
  - A3 ask: <title>
- Closed by the user in the Focus view since your last turn: A5 todo "<title>" dismissed without an answer; A4 ask "<title>" dismissed without an answer.
```

- **Ordering and caps:** severity (Critical, Ask, Todo, Alert, Decided), then number ascending. Decided items are included: the session must not re-announce them either. At most 20 item lines; when more are open, add `  - ...and <n> more (call mcp__cmx__list_attention)`. Titles are cut to 120 chars plus `...`. The first line appears only when there are open items; the "Closed by the user" line only when there are unreported closures.
- **Closure wording:** every user-closed item, whatever its kind, is `dismissed without an answer`. Items the user answered from the Focus view (Reply or Send answers) are not listed: their answers arrive in the prompt itself.
- **Plumbing:** `InstructionOptionsModel` gains two optional parameters, `IReadOnlyList<AttentionItemModel>? PinnedAttentionItems = null` and `IReadOnlyList<AttentionItemModel>? UserClosedAttentionItems = null`, filled by all three call sites: `SessionTurnService.RunTurnAsync` ×2 and `SessionPromptComposer`. `IAttentionService.TakeUnreportedUserClosures(nodeId)` returns those items and clears their flag, so each closure is reported once.

**FR.23.12 — Session/Focus switch:**
- **Placement.** In the session view header (`SessionView.axaml` Row 0, the right-docked `DockPanel` group), immediately **right of the "Search output..." box**. Two toggle buttons labelled **Session** and **Focus** act as one segmented switch: exactly one is checked. Left to right: `[Search box] [Session][Focus <badge>] [MD] [JSONL]`. In the DockPanel, declare JSONL, MD, the switch, then the search box. Style them like the MD/JSONL toggles (`Padding="8,3"`, `FontSize="11"`).
- **Session** (default) shows today's output, unchanged.
- **Focus** replaces the output area (the message `ScrollViewer` and its search overlay) with the Focus view. The thinking/busy indicator stays visible. The **MD and JSONL toggles are hidden**.
- **Search.** Pressing Enter in the search box while Focus is selected switches to Session and then runs the search. Search always searches the Session output.
- **Selection** is kept per session in memory (on the session's view model) and is **not persisted**. A newly opened session starts on Session.

**FR.23.13 — Focus badge:**
- **Visible** only while **Session** is selected and the count is > 0.
- **Count:** open items whose kind is not `Decided`.
- **Colour:** the background is set by the most severe open kind among the counted items (Critical > Ask > Todo > Alert), using the colours in FR.23.1. Todo uses the theme's foreground colour as background, with the number in the theme's background colour, so it adapts to dark and light themes. Hex colours live in `Constants` (no magic strings).
- **Shape:** a small rounded rectangle (corner radius 3, `Constants.Focus.BadgeCornerRadius`), not a circle, next to the "Focus" text (font about 10–11, minimum width about 16). The kind chips on cards (FR.23.14) keep their pill shape.
- **Updates** live as items change (FR.23.16).

**FR.23.14 — Focus view layout:** Top to bottom:
1. A **"Send answers (n)"** button, enabled when n ≥ 1 reply boxes hold text and the session can send (the same rule as the main Send button, which stays enabled during a turn because concurrent sends are allowed).
2. **Open items** (Critical, Ask, Todo, Alert), as cards ordered as in FR.23.2.
3. A collapsed **"Decisions (n)"** expander with open Decided cards. Shown only when n > 0.
4. A **"Closed (n)"** expander with closed items, newest closed first, **collapsed by default** so that what is open is what the user sees when scrolling. It starts collapsed for every session, and collapses again each time Focus is selected; expanding it shows the history and it stays expanded while the user stays on Focus (refreshes don't collapse it). Closed cards are **compact**: only the header row and the outcome line (`Answered: <answer>` / `Resolved: <note>` / `Dismissed`). Clicking a closed card's header row shows or hides its body; closed cards start with the body hidden.
5. **Empty state**, when no item other than Decided is open: `Nothing needs your attention in this session.` The Decisions and Closed expanders still show if they have items.

Card contents:
- Header row: a kind chip coloured as in FR.23.1 (Decided muted grey), the id, the title (SemiBold) and the posted time (local; `HH:mm` today, otherwise `yyyy-MM-dd HH:mm`).
- The body, rendered as markdown with the existing `MarkdownView` and selectable.
- The options as small buttons.
- "Use recommendation: <text>" (when a recommendation exists).
- A multi-line reply box.
- An action row under the reply box: **Reply** on the left, enabled when the card's reply box holds text, and the close button on the **far right**, away from Reply: **Dismiss** for Ask and Todo, **Got it** for Critical, Alert and Decided. The close button's tooltip says it closes the item without sending anything. (An earlier build labelled the Todo close button **Done**, which read as "send my reply"; it never sent the reply.)

Behaviour: clicking an option puts the option text in the card's reply box (replacing it); the user can edit it before sending. Clicking the recommendation puts `Accepted your recommendation: <recommendation>` there.

**FR.23.15 — Answering and closing from the Focus view:**
- **"Send answers"** builds **one** prompt from every non-empty reply box, ordered as the cards are, with `AttentionPromptBuilder.BuildAnswersPrompt`. Exact format:

  ```
  Answers from the Focus view:
  - A3 (<title>): <answer>
  - A5 (<title>): <answer>
  ```

  Answers keep their line breaks. A multi-line answer's continuation lines are indented by two spaces.
- It is sent through the **same interactive send path as the main input box** (`TurnController`), so it uses the same model, effort, profile and instruction block. It must **not** clear the input box or delete the draft: `SendAsyncCore` currently invokes `InputCleared` and `_draftService.DeleteDraft` unconditionally, so an option is added to skip both for this caller.
- It first marks those items `Answered`, with `Answer` = the text being sent (`IDaemonClient.MarkAttentionAnswered`), clears the sent reply boxes, and then sends. Marking first matters: the send builds the hidden instruction block synchronously, and items still open at that moment would be listed as pinned in the very turn that carries their answers.
- **Reply** (per card) does the same for that card alone: the same prompt format with a single line, the same send path, the same mark-first order. Other cards' unsent text is untouched.
- **Close button** (Dismiss / Got it): `IDaemonClient.CloseAttentionByUser(nodeId, itemId)`. The item becomes `Dismissed`, whatever its kind. Sets `IsUserCloseUnreported = true` and writes the SYSTEM entry (FR.23.8). Sends no prompt; text typed in that card's reply box is discarded, which is why the close button sits apart from Reply.

**FR.23.16 — Live updates and daemon boundary:**
- `IAttentionService` raises `AttentionChanged(nodeId)` after every change. `InProcDaemonClient` relays it as `IDaemonClient.AttentionChanged`, the same way it relays `EntryAppended`.
- The session view model subscribes and refreshes the Focus view and badge for its own node on the UI thread (`IUiDispatcher`). Unsent reply-box text survives refreshes (kept by item id).
- The session tree view model subscribes too and refreshes the session-row badge (FR.23.17) of the changed node on the UI thread.
- The UI reaches the ledger only through `IDaemonClient` (`GetAttentionItems`, `GetAttentionSummary`, `MarkAttentionAnswered`, `CloseAttentionByUser`, `AttentionChanged`), never through `IAttentionService` directly. This keeps the UI a daemon client, as in FR.21.10.

**FR.23.17 — Session-row badge:**
- **Where:** on session rows in the session tree (FR.1), immediately left of the session name, after the status dots and the sub-session bar; and on rows of the top-bar recent-sessions dropdown (FR.9.8), between the status dots and the name. Not on Recent-tab rows, and not in the dropdown's closed box.
- **Count:** every open item, `Decided` included. The Focus badge (FR.23.13) leaves Decided out, so the two numbers can differ.
- **Visible** whenever the count is > 0, whether Session or Focus is selected.
- **Colour:** set by the most severe open kind (Critical > Ask > Todo > Alert > Decided), using the FR.23.1 colours. With only Decided items open: grey `#707070` (`Constants.Focus.ChipDecidedBackground`) with a white number.
- **Shape:** the same rounded rectangle as the Focus badge (FR.23.13).
- **Data:** `AttentionSummaryModel.AllOpenCount` and `AllOpenTopKind`, from `IDaemonClient.GetAttentionSummary`. Loaded for every session at startup and refreshed for the changed node on `AttentionChanged` (FR.23.16).
- **Recent dropdown:** a session with open items is a Pinned Session there (FR.9.8).

**FR.23.18 — References on items (amendment, built 2026-11-15, Focus references step 1):** Design: `docs/features/focus-references-attachments-previews.md`. Implementation report: `.wpf-work/230-focus-refs-core.md`.
- **Refs.** `post_attention` items take an optional `refs` array (at most 10 per item). Kinds: `entry` (`quote` exact text from an entry, optional `session` default = the caller, `occurrence` `last` default or `first`), `file` (`path`, absolute or relative to the session's working directory, must be an existing regular file), `url` (http/https, at most 2000 characters). Every ref may carry a `label` (at most 100 characters). `attention.list` items return the resolved refs.
- **Quote resolution.** Done when the item is posted, against the content store (entry ids, see `docs/refactoring/tdcp-spec.md` "Local client additions"). The search runs from the newest entry (or the oldest) in pages; whitespace runs are collapsed and the match is case-sensitive; the quote must occur exactly once in the entry it lands on. SYSTEM and COMPACTION entries are skipped. The resolved ref stores `nodeId`, `fileId`, `entryId`, `offset` and `length` (characters of the entry content as the API serves it). A ref that cannot be resolved fails the whole call with a message naming it, and nothing is pinned. Re-posting an open item with refs replaces its refs; re-posting without refs keeps them.
- **Jump to an entry.** `session.entries` accepts `aroundEntryId` and returns the page centred on that entry, so a client can open the session at the referenced entry and highlight `offset`/`length`. Back navigation is client state.
- **File handles.** A file ref gets an opaque `refId` (128-bit random) that survives restarts. Clients never send a raw path to read a file: `file.read { handle, offset?, length? }` takes a `refId`, or a preview handle from `file.preview { nodeId, entryId, path }` (the path must occur in that entry's text; the handle lives 1 hour). Responses carry no absolute path; `attention.list` gives only the path relative to the working directory, when the file lies inside it.
- **FileAccess policy** (`AppSettings.FileAccess`). Denied paths are always refused, on every transport (credential and key folders and files, token stores, `.dat` files, the data root's settings and token files). The relay (web) additionally applies a type allowlist (text, images, video, pdf), per-type size caps and a chunk cap (`MaxRelayChunkBytes`, 512 KB), and logs every read. The relay `Mode` is `ReferencedOnly` (default: only files referenced by a Focus item of that session) or `WorkingDirectory` (decision A11: additionally any file inside the session's working directory). No TOTP. Loopback clients (desktop) may read any non-denied file a handle points to.
- **Not built yet.** Attachment handles (FR.31), the WPF and web Focus UI for refs (jump, preview pane, Back control), and the FR.25 web contract amendment that opens `file.read`, `file.preview` and `aroundEntryId` on the relay.

**Out of scope for FR.23:**
- A cross-session Attention list in the left panel, and a badge on Recent-tab rows (tree and recent-dropdown rows have one, FR.23.17).
- Answering by replying to a Telegram ping, and a Telegram `/attention` command.
- Local-LLM extraction of items from sessions that don't post.
- Capturing Claude's native `AskUserQuestion` as items.
- TDCP methods for the ledger.

---

### FR.24 — Session Tags

The Architect asked for each session to show "tags" in a dedicated row under the session header — the Linear tickets, GitHub pull requests and GitHub repositories it works on — each opening its page in the default browser (`HSE-230` opens https://linear.app/hardstop/issue/HSE-230; `windows-app PR #215` opens https://github.com/tryhardstop/windows-app/pull/215). Phase 1 (this FR, except FR.24.9) shows and opens tags; Phase 2 (FR.24.9) colours them by status. Detecting tags from the conversation text was measured on 2026-11-04 and rejected as too noisy: "Postgres DB for backend" mentions 50 distinct PR URLs, "HSE-150 Android backend" mentions 16 PR URLs and 18 key-like strings, and bare keys also catch non-tickets such as `KVM-1`, `DNS-01`, `CORE-01` and `ACC-16` (one session has 115 of them). Session names, however, carry the main ticket ("HSE-150 Android backend"), so tags come from exactly two places: the session itself (declared with `tag_session`) and its name. No LLM calls. This also lays the ground for the WPF client's search and filter by project + tags. The Tags tab (FR.24.11–FR.24.13, Architect 2026-11-11) brings tag search and filtering to the desktop client.

**FR.24.1 — Tags and kinds:** `SessionTagKind`, in display order: `LinearIssue`, `GitHubPullRequest`, `GitHubRepository`.

| Kind | Canonical key (dedupe, case-insensitive) | Label | URL |
|---|---|---|---|
| LinearIssue | `HSE-230` (upper-cased) | `HSE-230` | `https://linear.app/<workspace>/issue/HSE-230` |
| GitHubPullRequest | `tryhardstop/windows-app#215` | `windows-app PR #215` | `https://github.com/tryhardstop/windows-app/pull/215` |
| GitHubRepository | `tryhardstop/windows-app` | `windows-app` | `https://github.com/tryhardstop/windows-app` |

- GitHub owner and repo keep the case they were first given in, for the label and URL. The key compares case-insensitively.
- Each tag also has a source and an added time: `Session` (declared, FR.24.4, with time) or `Name` (derived, FR.24.2, no time).

**FR.24.2 — Sources:**
- **Declared:** added or removed by the session with `tag_session` (FR.24.4), and stored (FR.24.5).
- **Name-derived:** every token in the session's name matching `(?<![A-Za-z0-9])([A-Z][A-Z0-9]{0,9})-(\d{1,6})(?![A-Za-z0-9])` whose team key (group 1) is in the Linear workspace map (FR.24.3) becomes a LinearIssue tag with source `Name`. These tags are computed whenever tags are read, never stored (like the Auto-derived label, FR.1.3). Renaming the session changes them.
- **Not a source:** nothing is detected from the conversation text, which is too noisy (see the intro).
- **One chip per key.** If a declared tag and a name-derived tag share a key, the name-derived one is shown, because it can't be removed anyway.

**FR.24.3 — Linear workspace map:**
- **Learning.** Every full Linear issue URL given to `tag_session` (`https://linear.app/<workspace>/issue/<KEY>-<n>`, with any trailing `/slug` or query ignored) teaches `team key → workspace`, e.g. `HSE → hardstop`. The last one learned wins.
- **Storage:** `%AppData%\TessynDesktop\tags\linear-workspaces.json`.
- **Use.** Short Linear keys given to the tool, and name-derived tags, resolve their URL through this map.
- **Unknown team key:** the tool rejects the short key with a message asking for the full URL once (FR.24.4), and a name token gives no tag.
- **Learning a new team key** raises `SessionTagsChanged` for every session (`NodeId = null`), because name-derived tags may now appear anywhere.

**FR.24.4 — `tag_session` tool (cmx MCP server):** Input:

```
add     array of strings, optional, ≤ 30
remove  array of strings, optional, ≤ 30
```

Each entry is one of:
- a Linear issue URL;
- a Linear key `HSE-230` (team key known);
- a GitHub PR URL `https://github.com/<owner>/<repo>/pull/<n>` (trailing path, query or fragment ignored);
- `<owner>/<repo>#<n>`;
- a GitHub repository URL `https://github.com/<owner>/<repo>` (optional `.git` or trailing `/`);
- `<owner>/<repo>`.

Entries are trimmed and empty ones ignored. More than 30 entries in an array is a tool error. With neither array (or both empty), the tool lists the current tags.

Behaviour. Entries are processed in order (`add` first, then `remove`), and each one is reported on its own line. An invalid entry doesn't stop the valid ones.
- Adding a new key stores it with source `Session` and `AddedUtc`.
- Adding a key that is already declared, or already name-derived, reports "Already tagged" and changes nothing.
- Removing a declared key deletes it.
- A name-derived key can't be removed.
- Removing a key that isn't tagged reports so.

Result lines (exact):
- `Tagged HSE-230 - https://linear.app/hardstop/issue/HSE-230`
- `Already tagged HSE-230.`
- `Removed windows-app PR #215.`
- `Not tagged: '<entry>' - not a Linear issue, GitHub pull request or repository.`
- `Not tagged: '<entry>' - unknown Linear team 'ABC'; pass the full https://linear.app/<workspace>/issue/ABC-123 URL once.`
- `Not removed: '<entry>' - not tagged.`
- `Not removed: 'HSE-150' - it comes from the session name; rename the session to drop it.`
- `Not removed: '<entry>' - not a Linear issue, GitHub pull request or repository.`
- The last line, always: `Tags: <label1>, <label2>.` in FR.24.7 order, or `Tags: none.`

Session file. When something was added or removed, one SYSTEM entry is appended the way `workflow_phase` does: `[Tags] added HSE-230, windows-app PR #215; removed windows-app`. Only the parts that apply appear, and labels are used. Nothing is written for a listing or a no-op.

Worker Sessions may tag too, without guidance (FR.24.6). Tags help future filtering and are harmless.

Tool description (this wording):

> Tag this session with the Linear issues, GitHub pull requests and GitHub repositories it works on. The user sees the tags under the session name and opens them in the browser from there. add/remove take full URLs (best) or short forms: HSE-230, owner/repo#215, owner/repo. Tag what this session is working on, not everything it mentions. Finished work stays tagged; remove only tags added by mistake. Call with no arguments to list the current tags.

**FR.24.5 — Storage:**
- Declared tags live in `%AppData%\TessynDesktop\tags\<NodeId>.json`. They are owned by one Singleton `ISessionTagService`.
- The same rules as the Attention Ledger (FR.23.3) apply: atomic write (temp file, then rename); load lazily, then cache; a missing file means no tags; a corrupt file is moved aside to `<NodeId>.json.bad-<yyyyMMddHHmmss>` with a warning; Tessyn never deletes these files; the root directory comes in through the constructor (tests use a temp dir).
- `linear-workspaces.json` lives in the same folder and follows the same rules.

**FR.24.6 — Guidance (hidden instruction block):**
- **When.** New `Constants.Instructions.SessionTagGuidance`, emitted on a **top-level session's first turn in each app run**: `IsAgentToolsEnabled && !IsWorkerSession && IsTagGuidanceDue`. The tag service tracks in memory which sessions have had it this run (`TakeTagGuidanceDue(nodeId)` returns true once per node per run), so long-running sessions learn the tool too, not only fresh ones.
- Text:

  > This session shows tags under its name in Tessyn: the Linear issues, GitHub pull requests and GitHub repositories it works on, which the user opens from there. Keep them current with `mcp__cmx__tag_session`: tag an issue, PR or repository when this session starts working on it (pass the full URL), and don't tag things it only mentions. Finished work stays tagged; remove a tag only if it was added by mistake.

  When the session already has tags, append ` Current tags: <label1>, <label2>.`, in FR.24.7 order.
- **Plumbing.** `InstructionOptionsModel` gains `bool IsTagGuidanceDue = false` and `IReadOnlyList<SessionTagModel>? SessionTags = null`. All three call sites fill them: `SessionTurnService.RunTurnAsync` ×2, and `SessionPromptComposer` through `IDaemonClient`. `TakeTagGuidanceDue` is called only when the line can be emitted (agent tools on, not a worker), so a turn that can't show it doesn't consume it.
- **Placement:** after the FR.23.10 attention guidance and before the Telegram guidance.

**FR.24.7 — Tag row (UI):**
- **Placement and visibility.** In the session header (`SessionView.axaml` Row 0 Border), **below** the existing DockPanel (name and location, search box, Session/Focus switch, MD, JSONL), inside the same Border, so the header's bottom rule stays under the tags. A WrapPanel of chips, shown only when the session has at least one tag; no empty space otherwise. It appears in both the Session and the Focus view (FR.23.12). The header grows taller when tags show, which is accepted.
- **Order:** by kind (Linear issues, then pull requests, then repositories); within a kind, name-derived tags first, then declared tags, newest first.
- **Chip:** rounded (CornerRadius ≈ 9); 1 px border in `SystemControlForegroundBaseLowBrush`; padding about 8,1; FontSize 11; hand cursor and a subtle hover highlight; neutral colours in Phase 1.
- **Tooltip:** line 1: the URL; line 2: `From the session name`, or `Tagged by the session <local time: HH:mm today, else yyyy-MM-dd HH:mm>`.
- **Click** opens the URL in the default OS browser (`Process.Start` with `UseShellExecute = true`), but only if `SessionTagUrlPolicy.IsOpenable(url)` is true: absolute `https` URL, host exactly `linear.app` or `github.com`. Anything else is logged and ignored: never shell-execute an arbitrary string.
- **Live updates:** the row refreshes on `SessionTagsChanged` for its node or for all (`NodeId == null`), and when the session is renamed. Refreshes are marshalled with `IUiDispatcher`.

**FR.24.8 — Daemon boundary:**
- The UI reaches tags only through `IDaemonClient` (`SessionTagsChanged`, `GetSessionTags(nodeId)`, `TakeTagGuidanceDue(nodeId)`), never through `ISessionTagService` directly (FR.21.10).
- `InProcDaemonClient` delegates to `ISessionTagService`, resolving the node (and its name) through the registry, and relays `SessionTagsChanged`.
- `WireDaemonClient`: a silent event, and the methods throw `NotSupportedException`, as for FR.23.

**FR.24.9 — Phase 2 (NOT built in Phase 1): status colours:** Each chip is coloured by its item's status: a Linear issue by its workflow state type (backlog, unstarted, started, completed, canceled); a pull request by its state (open, draft, merged, closed), possibly with review and check status. Statuses come from a scheduled pull — for example every 10 minutes for tags of sessions active in the last N days, plus once when a session is opened. They are cached with the tags, and the tooltip shows the status name and when it was last checked.

**Open decision for Phase 2 — how to read statuses.** Linear is reachable only through the Architect's account or through MCP from Claude Code. Options:
- **(a)** A Linear personal API key, created by the Architect in Linear settings, stored DPAPI-protected like the Telegram token (FR.21.6), with Tessyn polling Linear's GraphQL API.
- **(b)** Linear's own MCP server, registered in the External MCP Server Registry with its OAuth client (FR.20), once Tessyn has an in-process MCP client (FR.20.9).
- **(c)** Sessions report the status through `tag_session` when they check an issue or PR. This is cheap, but only as fresh as their last check.
- **GitHub:** the `gh` CLI already authenticated on this machine (`gh pr view <n> -R <owner>/<repo> --json state,isDraft,mergedAt,reviewDecision`), or a token.

**FR.24.10 — Out of scope (Phase 1):**
- Detecting tags from conversation text.
- Adding or removing tags by hand in the UI.
- Status colours (FR.24.9).
- Multiple focus panels (WPF programme). (Searching and filtering sessions by tags was out of Phase 1 and was added on 2026-11-11 as the Tags tab, FR.24.11–FR.24.13.)
- Related tags between Linear issues and pull requests (FR.24.14).
- TDCP methods.
- PR references without an owner (`#215`, `windows-app#215`).

**FR.24.11 — Tags tab: search:** The left panel's third tab (FR.1.15) searches Session Tags.

**Layout,** top to bottom:
- a search bar;
- the tag results, scrollable;
- a horizontal splitter;
- the session list (FR.24.12), scrollable.

By default the tag results take about a third of the height and the session list the rest. Both parts have a minimum height, and the splitter position isn't persisted.

**Search bar.** It has the same look as the Tree tab's:
- a text box with the watermark `Search tags…`, showing the FR.1.11 spinner while the tag index loads;
- the project-scope toggle (📁).

There is no exact-phrase toggle and no refresh button.

**Scope toggle.** It follows the FR.1.10 rules but is independent of the Tree tab's toggle:
- It is on by default whenever a session is selected, and forced off and disabled when none is.
- When on, tags and sessions are limited to the Directory node containing the selected session (for a selected Worker Session, the Directory node it is nested under). When off, every directory counts.
- Toggling it, or selecting a session in another Directory node while it is on, reloads the tab.

**Matching.**
- The text is split on whitespace. A tag matches when every part occurs, case-insensitively, in its label or its canonical key (FR.24.1); the URL isn't searched.
- Examples:
  - `HSE-254` matches `HSE-254`.
  - `#303` matches `backend PR #303` (key `tryhardstop/backend#303`).
  - `backend 303` matches `backend PR #303`.
  - `windows-app` matches the `windows-app` repository and every `windows-app` PR.
- Empty text matches every tag in scope.
- Filtering runs in memory and applies as the user types.

**Results.**
- **Chips.** One chip per distinct tag (by canonical key) carried by at least one top-level session in scope, drawn like the Tag row chips (FR.24.7).
- **Order.** Selected tags come first (FR.24.12), then the other matches. Within each part, chips sort by kind (Linear issues, pull requests, repositories), then by the most recent Last Activity among the sessions carrying the tag (newest first; sessions without activity last), then by label.
- **Tooltip.** Line 1 is the URL. Line 2 is `In 1 session` or `In N sessions`, counted within scope.
- **Empty states.** With nothing to show, the area shows:
  - `No tags in this directory` when the scope is on and has no tags;
  - `No tags yet` when the scope is off and there are no tags;
  - `No matching tags` otherwise.

**FR.24.12 — Tags tab: selection and session list:**

**Selecting tags.**
- Left-clicking a tag chip toggles its selection, and a selected chip is visibly highlighted (accent background) in both themes.
- Right-clicking a chip opens a context menu with **Open in browser**. It opens the URL under the FR.24.7 rule (`SessionTagUrlPolicy`); anything else is logged and ignored.
- The selection survives changes to the search text, so tags from several searches combine. For example, select `HSE-254`, then search `#303` and select `backend PR #303`.
- Selected tags always show, first, even when they don't match the text.
- The selection lasts for the app run (across tab switches and scope changes) and isn't persisted.
- A selected tag with no sessions in the current scope stays selected and shown, so it can be deselected, and adds no sessions.

**Session list.**
- **Content.** The top-level sessions in scope that carry at least one selected tag (OR). A Worker Session's tags count for its top-level supervisor (follow `SupervisorNodeId`), because Worker Sessions aren't listed on their own.
- **Look.** It is shown exactly like the Recent tab (FR.1.16): the same grouping, order and rows, and clicking a row selects the session. Groups are collapsible but start expanded, and a rebuild keeps the groups the user collapsed (by directory path) collapsed for the app run. There is no collapse-on-switch rule here.
- **No tag selected.** The list is empty and shows `Select tags to list their sessions`.
- **No session matches.** The list shows `No sessions with the selected tags`.

**FR.24.13 — Tags tab: tag index and refresh:**

**The index.**
- The tab reads an index of every distinct tag in scope, each with the top-level sessions carrying it, through `IDaemonClient.GetSessionTagIndex(directoryPath)` (null means every directory).
- It counts name-derived and declared tags exactly as the Tag row shows them (FR.24.2, FR.24.7), so a session's chips and its index entries agree.
- **Representative copy.** For a tag carried by several sessions, the label and URL come from a name-derived copy if any session has one, otherwise from the most recently added declared copy.

**Loading and reloading.**
- The index is built off the UI thread, since it may read tag files, and results from a superseded load are dropped.
- It reloads when the tab is selected, when the scope toggle changes, when the scope's Directory node changes, and on `SessionTagsChanged` while the tab is selected (throttled). Other changes, such as renames, are picked up at the next reload.

**Daemon boundary.**
- `InProcDaemonClient` builds the index from a snapshot of the tree, taken briefly under the registry lock, and from `ISessionTagService.GetTags`.
- `WireDaemonClient` throws `NotSupportedException`, as in FR.24.8.

**FR.24.14 — Related tags (later phase, NOT built):** For a selected Linear issue, the Tags tab would also suggest the pull requests that are or were for it, and for a pull request its Linear issue, as extra chips.

**Open decision: how to relate them.**
- **(a)** Co-occurrence: tags carried by the same sessions. This is cheap and available from the index today.
- **(b)** GitHub: pull requests whose branch, title or body mention the Linear key, e.g. `gh pr list -R <owner>/<repo> --search "HSE-254" --state all`.
- **(c)** Linear's GitHub integration (the issue's PR attachments), read through the Linear API. This shares the access decision of FR.24.9.

**FR.24.15 — Tag search for sessions (cmx MCP tools):** Sessions get the Tags tab's flow (FR.24.11–FR.24.13) through two tools: `search_tags` finds tags, and `list_sessions` with `tags` lists the sessions carrying them (FR.15.13). Matching, order, roll-up and scope are exactly the Tags tab's, through the same code (`SessionTagSearch`, `IDaemonClient.GetSessionTagIndex`'s builder); nothing is reimplemented.

**`search_tags` arguments** (all optional):

| Arg | Default | Meaning |
|---|---|---|
| `query` | — | FR.24.11 matching: every whitespace-separated part occurs, case-insensitively, in the label or canonical key; the URL isn't searched; blank or absent matches every tag |
| `scope` | `project` | `project` — the caller's Directory node (for a Worker Session, the Directory node it is nested under), as the Tags tab's 📁 toggle; `all` — every directory |
| `limit` | 30 | Tags per page, clamped to 1–200 |
| `offset` | 0 | Tags to skip; a negative value counts as 0 |

An unknown `scope` is a tool error (the same strict parsing as `list_sessions`). Surrounding whitespace of `query` and `scope` is ignored.

**Rows and order.** One row per distinct tag carried by at least one top-level session in scope (Worker Sessions roll up to their top-level supervisor, FR.24.12), with the representative label and URL (FR.24.13). Order is FR.24.11's: kind, then the newest Last Activity among the carriers (the session file's last-write time, FR.15.13; no activity last), then label. There is no selection, so nothing is pinned first.

**Output** (plain text, the conventions of FR.15.13 — the listing's own text is ASCII, labels printed as they are with control characters as spaces, names cut to 60 characters):
- First line: the `You:` line of `list_sessions`.
- Header: `Tags in <label> (<path>) - <n> tags` in `project` scope, `Tags in all directories - <n> tags` in `all`; `<n>` counts the matching tags. When `query` is set: `, matching "<query>"` is appended.
- Row: `<label>  <url>  <n> session(s): <id>, <id>, ...` — the carriers' Short Node Ids, newest Last Activity first, at most 5, then `+<k>` when more carry it (`1 session: …` singular).
- Footer: `Rows <a>-<b> of <n>.` plus `Next page: offset=<b>.` when rows remain, then the hints line: `Sessions: list_sessions tags=["<url or key>"].` always, `Other projects: scope=all.` in `project` scope.
- Empty: `No tags in this directory` (`project` scope, no tags at all), `No tags yet` (`all`, none at all), `No matching tags` (otherwise), each followed by the hints that apply; with rows but an `offset` past the end, `No rows at offset=<o>; <n> in total.`

**Cost.** The tree is snapshotted under `SyncRoot` (fields only); tag files and last-write times are read outside the lock, last-write times only for carriers of the tags on the returned page and for ordering.

**`list_sessions` `tags`.** See FR.15.13. Entries use the FR.24.4 input forms and are resolved to canonical keys by `ISessionTagService` with the same Linear workspace map as `tag_session`, without learning or persisting anything. A short Linear key whose team isn't in the map still filters (its canonical key is the key itself).

**Tool description** (this wording):

> Find the Linear issues, GitHub pull requests and repositories that sessions are tagged with, and how many sessions carry each. Use it to find sessions by what they work on: search here, then call list_sessions with tags=[...]. Defaults to your project; scope=all for every directory.

---

### FR.26 — Link Fidelity, Interactive Links and Output Previews

Complements FR.24 Session Tags (chips under the session header that come from `tag_session`): FR.26 covers links and files inside the transcript text. Source: `docs/refactoring/wpf-programme-plan.md` Appendix A and A13; design `docs/features/focus-references-attachments-previews.md` section 4. New desktop UI for FR.26.2-FR.26.5 is built in WPF only (decision A9); the web client gets the same features through the remote API.

- **FR.26.1 — Full-URL references.** [DONE] Every session's hidden instruction block (FR.11) asks the model to refer to GitHub pull requests, issues and commits, Linear issues and Slack messages by their full URL, for example `https://github.com/tryhardstop/windows-app/pull/196` or `https://linear.app/hardstop/issue/HSE-240`, and never by shorthand such as `PR#196` or `HSE-240`. This applies to worker sessions too.
- **FR.26.2 — Link chips.** The transcript renders recognised URLs (GitHub PR, issue or commit; Linear issue; Slack permalink) as chips with a short label, such as `windows-app#196`, `HSE-240` or the Slack channel name. Any other http(s) URL renders as a plain link. Recognition is pattern-based: no network call and no credentials.
- **FR.26.3 — Preview on click.** Clicking a link opens it in the embedded browser pane, and a modifier-click opens the system browser. Until the pane exists, a click opens the system browser.
- **FR.26.4 — Media previews.** *Planned (WPF and web).* When session output contains an https URL whose path ends in an image or video extension, or whose host is on the media allowlist, the transcript offers an inline preview. Hosts on the allowlist load automatically (initial list: `annielartens.online`, `*.githubusercontent.com`; editable in Settings). Every other host shows a "load preview" button and loads only on click (decision A13).
- **FR.26.5 — Local-file chips.** *Planned (WPF and web).* An absolute local path (`C:\...`, `\\server\...`) in session output renders as a chip. On desktop a click previews the file (markdown rendered, images, text and code, video). On the web a click asks `file.preview { nodeId, entryId, path }`: the daemon checks that the path occurs in that entry's text, applies the FileAccess policy (FR.23.18, decision A11) and returns a handle for `file.read`.

Status: FR.26.1 implemented (Phase 0). FR.26.2 and FR.26.3 arrive with the WPF transcript; FR.26.4 and FR.26.5 follow steps 4-6 of the design's phasing.

---

### FR.31 — Attachments

*Planned.* Design: `docs/features/focus-references-attachments-previews.md` section 3. (FR.27-FR.30 are reserved by the WPF programme plan: browser pane, output modes, multi-screen layout, helper models.)

- **FR.31.1 — Storage.** Attachments are stored per profile in app data (decision A12), under `%AppData%\TessynDesktop\attachments\`, grouped by profile and session, one timestamp-prefixed file per attachment, kept until the session is deleted. Name collisions get a suffix. The exact folder layout is settled when the feature is built.
- **FR.31.2 — Desktop.** In the WPF client, dropping files onto the input inserts their full paths at the caret. A "file mode" toggle beside the input instead copies the files into the session's attachments folder and inserts that path.
- **FR.31.3 — Web.** The input area has an attach button. The browser uploads the file to the Tessyn.Web server (cap 200 MB); the daemon fetches it over the link, checks its hash, stores it as an attachment and returns the path; the server then deletes its copy and the page inserts the path into the prompt.
- **FR.31.4 — Session side.** The prompt gets a plain path, for example `Attached file: C:\...\attachments\...\photo.png`. Sessions read it like any other file; no MCP change is needed.
- **FR.31.5 — Handles.** An attachment is readable through `file.read` with an `attachmentId` handle under the FileAccess policy (FR.23.18).

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
- Rate limits or cooldowns on `notify_user`, quiet hours, presence-aware suppression (no phone ping while the user is active at the desktop), and redaction of secrets in mirrored Telegram text — considered and declined in ISSUE.012: quiet hours are handled in the phone's notification settings, and anything agents touch is treated as exposed
- The Focus view's cross-session Attention list, Telegram answering and `/attention` command, local-LLM item extraction, native `AskUserQuestion` capture and TDCP ledger methods — see the out-of-scope list at the end of FR.23
- Detecting Session Tags from conversation text, adding/removing tags by hand in the UI, Phase 2 status colours (FR.24.9), multiple focus panels, related tags between Linear issues and pull requests (FR.24.14), TDCP methods and owner-less PR references — see FR.24.10

---

## Glossary Reference

See `/docs/glossary.md` for domain term definitions.
