> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

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

**FR.1.8 amendment (amended 2026-11-12, FR.25):** The running flag now covers every kind of turn: headless turns (Telegram, scheduled, orchestrated, web) and turns typed in the desktop's own session view. The flag is **reference-counted** per session: each turn source that starts a turn adds 1 and removes 1 when its turn ends, the count never goes below 0, and the session counts as running while the count is above 0. The change event fires only when the count moves between 0 and 1. The desktop's `TurnController` reports its interactive turns to the daemon (`IDaemonClient.SetSessionRunning`, true when its busy count goes from 0 to 1, false when it returns to 0), so a turn started at the desktop also shows as running in Tessyn Web (FR.25.19).

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

The selected tab isn't persisted: the app starts on Tree. Picking a session in the top-bar recent-sessions dropdown switches to the Tree tab, as it does today, and does what a click on the session's tree row does: the session is **selected in the tree**, its collapsed ancestors (groups, the directory, a supervisor session) are **expanded**, and the tree is **scrolled** so that its row is visible. The same applies to a session picked in the Recent tab or in a Tags list.

**FR.1.16 — Recent tab:** Documents today's behaviour plus the new collapse rule:
- **Content.** Every top-level session; Worker Sessions stay nested in the tree and aren't listed.
- **Grouping.** Sessions are grouped under a header per Directory node: a 📂 icon and the Auto-derived label (FR.1.3), with directories in tree order.
- **Order within a group.** Sessions with unsent draft text come first, then the rest, each newest first by Last Activity (`SessionNodeViewModel.LastActivityTimestamp`, FR.9.8).
- **Rows.** Status dots, name, the draft ✏ indicator, the favorite star (FR.9.7), profile name, last prompt time, the recency background (FR.9.5) and the dotted border for recently active sessions.
- **Click.** Clicking a row selects that session in the tree too. This also works when the same row is clicked again after another session was selected elsewhere.
- **Rebuild.** The list is rebuilt each time the tab is selected.
- **NEW: collapsible groups.** Each directory header shows a chevron (▾ expanded, ▸ collapsed); clicking the header toggles its group.
- **NEW: collapse on switch.** Each time the user switches to the Recent tab, every group is collapsed except the one for the Directory node containing the selected session. With no session selected, every group is expanded. Expanding or collapsing a group by hand holds until the next switch to the tab.

**FR.1.17 — Stable ids for Directory and Group nodes (amended 2026-11-12, FR.25):** Session nodes already have a `NodeId` (a 32-hex-digit GUID, `Guid.ToString("N")`). Directory and Group nodes now get one too (`DirectoryNodeModel.NodeId`, `GroupNodeModel.NodeId`), persisted in `appsettings.json` with the node. A node loaded without one gets an id during tree load, and the tree is saved once if any id was assigned; every code path that creates a Directory or Group node (Add directory, Add group, the agent session creation that creates a group) assigns an id at creation. Ids never change and are never reused. The registry finds a node by id (from a snapshot, under the registry lock). Tessyn Web addresses tree nodes by these ids (FR.25.07, FR.25.12).

---

---

## Amendments and additions (batch 2026-11-17)

> Clients: the rules are for the WPF desktop client and are client-neutral unless a line says otherwise.

New IDs start at **FR.1.18** (FR.1.17 is taken by the draft, stable ids for Directory and Group nodes).

### Amendments to existing requirements

| Existing | Amended by | Change |
|---|---|---|
| FR.1.15 Left panel tabs | FR.1.19 | The three tab headers are equal in width and together fill 100% of the panel. |
| FR.1.16 Recent tab | FR.1.20 | Rows are as the tree's rows, full width; no project name in a row. |
| FR.9.5 Recency bars | FR.9.10 | Four tiers (15 min, 1 h, 4 h, 12 h) plus the 24-hour dotted border. Replaces the three tiers 15/30/60 min. |
| FR.24.11 Tags tab: search | FR.1.24, FR.24.17 | Tag search scope and wildcard rules, and what the tab says when nothing matches. |
| FR.1.21 Worker count and the recency box | FR.1.28 | The `N workers` total stays as it is. A new badge next to it counts the workers **running now**. |
| FR.1.5 Adding nodes (Add Session), FR.25.12 Create session | FR.1.30 | A session created **in a directory or group** goes **last** among its sessions, not first. Created beside a selected session (web): still right after it. |
| FR.1.25 Tree toolbar and header actions | FR.1.31 | Ctrl+Up and Ctrl+Down move the selected session one place up or down, as a drag would. |
| FR.1.31 Move a session with Ctrl+Up / Ctrl+Down | FR.1.32 | The same keys move a selected **group** one place among the groups of its directory or group. |

### New requirements

**FR.1.18 — Date in the session rows.** Every list that shows sessions (the tree, the Recent tab, the Tags tab session list and the Recent dropdown) shows **one** date text per row, chosen by one setting, **Date in the session rows**, in the Settings Appearance tab. Exactly two options:
- **Date** — `yyyy-MM-dd`, local time, no clock time.
- **Relative** — the age of the row's last activity: `now` (under a minute), `5m`, `3h`, `9d`, and from 14 days **weeks without a ceiling** (`107w`; no months, no years).
- The text is refreshed with the 60-second recency refresh (FR.9.10), so a relative age keeps growing with the clock.
- Only the chosen form is shown, **never both**.
- The text sits **inside** the row's recency box (FR.1.21), on the second line, at the right end, opposite the profile name (FR.11.14).
- Default: Date.

**FR.1.19 — Equal tabs.** The left panel's tab headers (Tree, Recent, Tags; FR.1.15) have **the same width** and together take the **whole width** of the panel at any panel width. Header text is centred.

**FR.1.20 — Rows of the Recent tab and of the Recent dropdown.** A session row looks the same in the tree, in the Recent tab, in the Tags tab's session list and in the Recent dropdown (FR.9.8): the status squares (FR.1.22), the Focus count pill (FR.23.17), the session name, the draft pencil, the schedule clock and the favourite star (FR.9.7), a second line with the profile (FR.11.14) on the left and the date (FR.1.18) on the right, the recency colour and the 24-hour dotted border (FR.9.10).
- **Full width.** In the Recent tab and the Tags tab a row is **one button spanning the full width** of the list, so the recency colour reaches both edges. The favourite / Hide icon (FR.9.6, FR.9.7) lies over its right end; the row reserves space for it so the text never runs under it.
- **No project name in the Recent tab.** The group header already names the project (FR.1.16), so a row repeats nothing.
- **In the dropdown** a row is one line, full width: squares, Focus pill, **session name, then the project name right after it** (the dropdown has no group headers), the schedule clock, and the icon slot (FR.9.7) at the right end.
- The group header's chevron is the tree's chevron (same glyph, size and turn-down).

**FR.1.21 — Worker count and the recency box.** The tinted background of a row is its **recency box** (FR.9.10). Everything that belongs to the row's second line, the date text (FR.1.18) and, for a supervisor, the worker count, is **inside** the box. The worker count reads **`N workers`** (`1 worker`), on the name line; a bare `104w` outside the box is not allowed.

**FR.1.22 — Status squares.** Each row has two squares: the upper one shows **running** (a turn is in progress), the lower one **resumable** (the Claude session can be resumed, FR.1.26). Colours are palette keys (FR.9.9).

**FR.1.23 — No dead space under the search.** The tree's status line (search result count, errors) takes **no space** while it has nothing to say. In the Tags tab, with no tag selected the session list takes only its hint line and the chips take the rest of the height. No empty band appears under the search box in any tab.

**FR.1.24 — Tags tab search scope and wildcards.** (Amends FR.24.11.)
- A **selected session or worker** scopes the Tags tab to that session's directory (scope on by default, as FR.24.11). A selected **directory, group or nothing** means **every tag is searched**; the scope does not follow a directory node.
- While the scope is on, a line above the chips says **"Only this directory's tags"** with a **Show all directories** button that switches the scope off.
- A query matches a tag when every word of the query is a substring of the tag's label or key. `*` and `?` are **word separators** (`HSE-*` searches `HSE-`, `HSE*240` searches `HSE 240`).
- When nothing matches with the scope on, the empty text says so and names the way out: "No tag matches in this directory. Switch off the project scope (the folder button) to search every directory."

**FR.1.25 — Tree toolbar and header actions (WPF).** Add directory, New session, New group and Refresh are **one line**, at the **bottom** of the tree panel. Rename, Delete and Move to are not toolbar buttons; they are in the node's context menu. In the session header: a **Delete icon** left of the title (the existing confirmation), and a **double-click on the title** turns it into a text box holding the name; Enter renames, Esc or leaving the box cancels, an empty or unchanged name does nothing, a refusal shows as a notice. The text box takes the whole title area (the stretching rule, NR.7).

**FR.1.26 — Resumable flag and worker-only groups (daemon).** The daemon fills `SessionStateDto.resumable` from one cached sweep per minute (one batched check, never a file probe per node; the first sweep sets the baseline and raises nothing; later flips raise `session.changed`). A Group the user created and left empty stays visible. A Group that held at least one session, **all of them Workers**, and whose child groups are all of that kind, is flagged `holdsOnlyWorkers`; clients hide it (an orchestration husk) but it stays a node (the import picker lists it). Wire details: `tdcp-spec` additions (§A2).

**FR.1.27 — Switch profile (WPF).** *Switch profile…* acts on the open session and sits in the session header, right of *Send later* (FR.14). Choice dialogs (this one and the *Send later* delay picker) keep a 44 px margin left and right; every choice button and Cancel is as wide as the dialog, with centred text.

**FR.1.28 — Active workers badge.** A session's row shows how many of its workers are **running right now**: a **red number inside a dark-red outlined square with rounded corners**, in the tree, the Recent tab, the Tags tab's session list and the Recent dropdown (the open list's rows **and** the closed button, which shows the open session).
- **Active, not total.** The number is the workers running at this moment, **direct and nested** (a worker of a worker counts for every supervisor above it). It is not the `N workers` total of FR.1.21, which counts every worker ever spawned and stays as it is.
- **Hidden at 0.** A session with no running worker shows no badge and takes no room for it.
- **Placement.** On the row's name line, at the right: the badge sits **immediately left of the `N workers` total** (in the Recent tab, the Tags tab and the dropdown, which show no total, it sits at the right end of the name line before the schedule clock and the icon slot). The two read as `[3] 104 workers`: the framed number is what is running, the plain text is how many exist. In the closed Recent dropdown the badge is on the button, left of the chevron, and follows the open session.
- **Live.** It follows the daemon's `session.changed` events (the supervisor's state carries `runningWorkerCount`, TDCP section 10.A6) with no polling per row: workers starting and finishing change it at once in every list.
- **One face.** The badge is part of the shared session row face (FR.1.20) and of the dropdown row, so all four lists show the same thing. Its tooltip reads `Workers running now: N`.
- **Colours.** Number and outline are palette entries (FR.9.16).
- **Daemon.** `SessionStateDto.runningWorkerCount` is computed from the registry's running state of the session's workers (TDCP 10.A6). The daemon publishes `session.changed` for a supervisor when one of its workers starts or stops running, coalesced per session (no event storm).

**FR.1.29 — Limits-error warning.** When a session's **last turn ended with a usage or limits error** (Claude's "You've hit your session limit", "weekly limit" or "5-hour limit" message, "usage limit reached", or a rate-limit error), its row shows a **yellow warning triangle**.
- **Placement.** Tree, Recent tab and Tags tab session list: **left of the session date** (second line of the row, right end). Recent dropdown row: **left of the project name / profile name block**. **Workers and sub-sessions in the tree**: all the way to the **right of the sub-session's name line**, above the date.
- **Tooltip.** The error text, shortened to one line of at most 120 characters (for example `You've hit your session limit · resets 5:20pm (Asia/Makassar)`).
- **Clearing.** It goes away as soon as a **later turn succeeds** (a result without error). Another kind of error, or a turn that ends without a result, leaves it.
- **Live.** It follows the daemon's `session.changed` events (`SessionStateDto.limitError`, TDCP section 10.A7) in every list; nothing is polled.
- **Detection (daemon).** Structured first: the Claude CLI stamps the synthetic assistant message of a limit `error: "rate_limit"` (with `quotaLimits.status: "rejected"`); either marks the turn. A result or system error carries no stamp, so its text is matched against patterns kept in `Constants.LimitErrors` (tested with real messages from session files). Ordinary assistant text is **never** text-matched, so a session that talks about limits does not warn.
- **Not an auth error.** It is independent of the `authError` flag (FR.12.16). It lives in memory with the auth flag: after a daemon restart it is empty until the next turn.
- **Colour.** Palette entry *Limits-error warning* (FR.9.17).

**FR.1.30 — New session goes last.** (Amends FR.1.5 Add Session and FR.25.12.) A session created **in a directory or a group** (New session in the toolbar, Add Session in the context menu, the web's Create session with a directory or group selected) is placed **after the last session** of that directory or group, so it is the last row among its sessions. Groups keep their place.
- **Beside a session.** A session created with a **session** as the anchor (the web's Create session with a session selected) still goes **right after that session** (FR.25.12, unchanged).
- **Agent spawns.** Workers and agent-created sessions are appended as before (no change).
- **Where.** The daemon decides the place (`tree.createSession`); the desktop and the web show it where the daemon put it, then the desktop selects it and scrolls it into view.

**FR.1.31 — Move a session with Ctrl+Up / Ctrl+Down.** In the tree, with a **top-level session** selected (a session directly in a directory or group, the same sessions a drag can move), **Ctrl+Up** moves it **one place up** and **Ctrl+Down** **one place down** among the sessions of its directory or group.
- **Same as a drag.** It is the move a drag onto the neighbour would make (`tree.moveNode` with the new index), with the same rules and the same refusals: a running session is not moved, and the reason shows in the tree status line, as for a drag.
- **Stays in its container.** It never leaves its directory or group and never enters a group. On the first session Ctrl+Up does nothing; on the last, Ctrl+Down does nothing. Workers move with their supervisor. With a worker, a group or a directory selected the keys do nothing.
- **Selection and focus.** The moved session stays selected and in view, and the keyboard focus stays in the tree, so the keys can be pressed again at once. A press while the previous move is still in flight is ignored (no queue).
- **Search.** While a tree search is active (the tree is in relevance order, FR.1.12) the keys do nothing.
- **Only in the tree.** The keys act only while the tree has the keyboard focus; elsewhere Ctrl+Up / Ctrl+Down keep their meaning (input navigation, FR.12).

**FR.1.32 — Move a group with Ctrl+Up / Ctrl+Down.** (Extends FR.1.31.) With a **group** selected in the tree, **Ctrl+Up** moves it **one place up** and **Ctrl+Down** **one place down** among the **groups** of its parent (a directory or a group). Its sessions, sub-groups and workers move with it.
- **Stays in its parent.** A group never leaves its parent this way and never passes the sessions: groups stay above the sessions of the same parent. On the first group Ctrl+Up does nothing; on the last, Ctrl+Down does nothing.
- **Running sessions.** A running session inside the group does not block the move: its working directory does not change.
- **Same behaviour as FR.1.31.** The group stays selected and in view, the tree keeps the keyboard focus, a press while a move is in flight is ignored, and the keys do nothing while a tree search is active. A directory selected: nothing happens.
- **Daemon.** `tree.moveNode` accepts a group whose `parentId` is its current parent, with the new index among the parent's groups (TDCP section 10.A8). Moving a group to another parent stays refused.
- **Stored.** The order is saved in `appsettings.json` like any tree change and is kept after a restart; the web's tree shows the same order.
