> Area file of the requirements (index: [README](README.md); old IDs and where they went: [ID-MAP](ID-MAP.md)). The text below is the unchanged existing text of this area; the batch of 2026-11-17 is at the end.

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

---

## Amendments and additions (batch 2026-11-17)

> **About FR.24.16.** Report 452 *proposed* a "FR.24.16 — Tag area modes (mini/full), persisted". It was **never entered into the docs** (FR.24 ends at FR.24.15), and the Architect replaced that design, so the number is used for the new rule below. The proposal of 452 is **superseded** and must not be entered anywhere. Its test-case proposal (`TC.TAGS.16`) is replaced by `TC.TA.01`.

### Amendments to existing requirements

| Existing | Amended by | Change |
|---|---|---|
| FR.24.7 Tag row (UI) | FR.24.16 | The row's height behaviour is now the collapsed/expanded rule. |
| FR.24.11 Tags tab: search | FR.1.24, FR.24.17 | Scope, wildcards and empty-result text. |
| FR.24.7 / FR.24.16 chip size | FR.24.18 | Chip font size is a setting; the mini line follows it. |

### New requirements

**FR.24.16 — Tag area: collapsed by default, expand on click (WPF and Avalonia).**
1. **Collapsed by default.** The tag area of a session shows **one line** of chips; overflow is hidden without a scrollbar, with a `+N` hint at the right end while chips are hidden (the hint never covers a chip).
2. **Expand.** A **click on empty space** of the tag area (or on the `+N` hint) **expands** it to up to **10 lines**, then it scrolls vertically. A click on a chip keeps its meaning (opens its link at once).
3. **Collapse.** A **double-click on empty space** of the area collapses it, **only on empty space**: a double-click on a chip (it opens its link) or on the area's scrollbar does not collapse it, and the scrollbar press is not swallowed.
4. **Session/Focus switch.** Switching between Session and Focus of the same session **keeps** the state.
5. **Every switch to another session resets it to collapsed** (also when returning to the session just left), and closing the session collapses it. The state is held per open session view in memory only.
6. **Nothing persists.** The state is saved neither in the client state nor in the daemon; a restart starts collapsed. An older client state that still holds the earlier `tagAreaExpanded` value is read and the value dropped; it is never written back.
7. With no tags the area is empty and expanding does nothing.
8. The area exposes an expand/collapse action for accessibility (WPF: UI Automation ExpandCollapse, name "Session tags").

Applies to **both desktop clients, WPF and Avalonia**. **Tessyn Web is not covered** by FR.24.16 or FR.24.18; its tag row keeps its own behaviour (FR.25).

**FR.24.17 — Tags tab results.** The tab finds tags: the search rules, the scope and the empty text are FR.1.24. With the real index (hundreds of chips) the chips wrap and scroll; chips and the session list share the height as FR.1.23 says.

**FR.24.18 — Tag cloud font size (WPF and Avalonia).** A setting, **Tag cloud font size**, sets the text size of the tag chips. It exists in **both desktop clients** (not in Tessyn Web): WPF under Settings › **Appearance**, Avalonia in its settings window.
- **Where.** WPF: Settings › **Appearance** › **Tag cloud** (a number box). Avalonia: its Settings window, **Appearance** tab, **Tag cloud font size** (a number box).
- **The mini line follows the chip size.** The collapsed one-line area (FR.24.16 item 1) is exactly one chip line high at the chosen size, so a larger size never clips a chip and a smaller one leaves no empty band. The 10-line limit of the expanded area and the `+N` hint follow the same chip height.
- **Range and defaults.** The value is a whole number of **8 to 40**. A value outside the range, or text that is not a number, is refused and nothing is stored. The default is **each client's previous chip size**: **WPF 14, Avalonia 11**.
- **When it applies.** WPF: **live**, every visible chip, in the tag area (FR.24.16) and in the Tags tab chips (FR.24.11), and the `+N` hint, restyles at once, with no restart and no reopening of the session. Avalonia: **on Save** of the Settings window.
- The value is a client setting (not per session, not in the daemon); each client keeps its own.
- **Not imported.** The Avalonia-to-WPF settings import (FR.32.4) does **not** carry it; WPF keeps its own value (default 14).
