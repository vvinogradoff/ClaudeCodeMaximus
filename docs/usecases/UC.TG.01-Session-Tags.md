# UC.TG.01 — Session Tags

Related requirements: FR.24.1–FR.24.10 in [requirements.md](/docs/requirements.md). Related: [UC.FV.01](/docs/usecases/UC.FV.01-Focus-View.md) (Focus view, FR.23), FR.1.3 (Auto-derived label), FR.15 (Worker Sessions). Test cases: [TC.TG.01](/docs/testcases/TC.TG.01-Session-Tags.md).

## Actors

- **User** (the Architect) — reads the Tag row and opens tags in the browser.
- **Session** — a top-level Claude session that tags itself through the `cmx` MCP tool `tag_session`.
- **Worker Session** — a session spawned by an orchestrator (`OrchestrationDepth > 0`).
- **Tessyn** — stores declared tags, learns the Linear workspace map, derives tags from names, builds the instruction block.

## Preconditions (common)

- Agent tools are enabled, so the session has the `cmx` MCP server.
- Phase 1 only: tags come from `tag_session` (declared) and from the session name (name-derived). Nothing is detected from conversation text.
- Terms: Session Tag, Tag row, Name-derived tag, Linear workspace map (see the glossary).

---

## UC.TG.01 — A session tags a Linear issue by full URL, which teaches the workspace

**Actors:** Session, Tessyn, User.
**Preconditions:** the Linear workspace map has no entry for `HSE`; the session has no tags.

**Main flow:**
1. The Session starts work on HSE-230 and calls `tag_session` with `add: ["https://linear.app/hardstop/issue/HSE-230/fix-the-thing?x=1"]`.
2. Tessyn parses the URL (the trailing `/slug` and query are ignored), learns `HSE -> hardstop` into `%AppData%\TessynDesktop\tags\linear-workspaces.json`, and stores the tag in `%AppData%\TessynDesktop\tags\<NodeId>.json` with source `Session` and `AddedUtc`.
3. The tool returns:
   ```
   Tagged HSE-230 - https://linear.app/hardstop/issue/HSE-230
   Tags: HSE-230.
   ```
4. A SYSTEM entry `[Tags] added HSE-230` is appended to the session file.
5. Learning a new team key raises `SessionTagsChanged` for every session (`NodeId = null`).
6. The Tag row shows an `HSE-230` chip in the User's view.

**Alternative flows:**
- **A1 — Different workspace later:** a later full URL for the same team key with another workspace replaces the mapping (last one learned wins).
- **A2 — Same URL again:** result `Already tagged HSE-230.`; nothing changes, no SYSTEM entry, no event.
- **A3 — Short key afterwards:** `HSE-231` now resolves through the map (UC.TG.03).

**Postconditions:** the tag is stored; the workspace map knows `HSE`.
**Covers:** FR.24.1, FR.24.3, FR.24.4, FR.24.5.

---

## UC.TG.02 — A session tags a PR and a repository with short forms and URLs

**Actors:** Session, Tessyn.

**Main flow:**
1. The Session calls `tag_session` with `add: ["tryhardstop/windows-app#215", "https://github.com/tryhardstop/windows-app"]`.
2. Tessyn stores a `GitHubPullRequest` tag (key `tryhardstop/windows-app#215`, label `windows-app PR #215`, URL `https://github.com/tryhardstop/windows-app/pull/215`) and a `GitHubRepository` tag (key `tryhardstop/windows-app`, label `windows-app`, URL `https://github.com/tryhardstop/windows-app`).
3. The tool returns:
   ```
   Tagged windows-app PR #215 - https://github.com/tryhardstop/windows-app/pull/215
   Tagged windows-app - https://github.com/tryhardstop/windows-app
   Tags: windows-app PR #215, windows-app.
   ```
4. SYSTEM entry `[Tags] added windows-app PR #215, windows-app`.

**Alternative flows:**
- **A1 — URL forms:** `https://github.com/<owner>/<repo>/pull/215/files?x=1#y` is a PR (trailing path, query and fragment ignored); `https://github.com/<owner>/<repo>.git` and `…/` are the repository.
- **A2 — Case:** `TryHardStop/Windows-App#215` keeps its case in label and URL as first given; later `tryhardstop/windows-app#215` is the same key (case-insensitive) and reports `Already tagged windows-app PR #215.`
- **A3 — PR without owner:** `#215` or `windows-app#215` is not tagged (out of scope, FR.24.10): `Not tagged: '#215' - not a Linear issue, GitHub pull request or repository.`

**Postconditions:** two declared tags in the ledger of the session.
**Covers:** FR.24.1, FR.24.4, FR.24.10.

---

## UC.TG.03 — A short Linear key with an unknown team is rejected, then succeeds after the full URL

**Actors:** Session, Tessyn.
**Preconditions:** the workspace map has no entry for `ABC`.

**Main flow:**
1. The Session calls `tag_session` with `add: ["ABC-123"]`.
2. Tessyn cannot resolve the team and returns:
   ```
   Not tagged: 'ABC-123' - unknown Linear team 'ABC'; pass the full https://linear.app/<workspace>/issue/ABC-123 URL once.
   Tags: none.
   ```
   Nothing is stored or written; no event.
3. The Session calls again with `add: ["https://linear.app/acme/issue/ABC-123"]`: `Tagged ABC-123 - https://linear.app/acme/issue/ABC-123`.
4. From now on `ABC-124` resolves as a short key: `Tagged ABC-124 - https://linear.app/acme/issue/ABC-124`.

**Alternative flows:**
- **A1 — Other entries in the same call:** valid entries are still tagged; the rejected one is reported on its own line (partial success).
- **A2 — Not a Linear issue:** `foo` → `Not tagged: 'foo' - not a Linear issue, GitHub pull request or repository.`

**Postconditions:** `ABC → acme` is persisted.
**Covers:** FR.24.3, FR.24.4.

---

## UC.TG.04 — Name-derived tags follow the session name

**Actors:** User, Tessyn.
**Preconditions:** the workspace map knows `HSE`.

**Main flow:**
1. A session is named "HSE-150 Android backend". Without any `tag_session` call, the Tag row shows an `HSE-150` chip (source `Name`, URL via the map).
2. The User renames the session to "HSE-165 W14 rebase". The chip becomes `HSE-165` (computed at display time; nothing stored).
3. A name with several keys ("HSE-150 and HSE-151 sync") gives one chip each.

**Alternative flows:**
- **A1 — Team learned later:** the session is named "ABC-9 spike"; ABC is unknown, so no chip. Another session tags an ABC issue by full URL; `SessionTagsChanged(null)` fires and the "ABC-9 spike" session's row now shows `ABC-9`.
- **A2 — Unknown or non-ticket tokens** (`KVM-1`, `DNS-01` when the team is not in the map) give no chip.
- **A3 — Boundary:** `xHSE-150`, `HSE-1234567` (7 digits) and `HSE-150a` are not matched; `(HSE-150)` and `[HSE-150]` are.
- **A4 — Same key declared and derived:** one chip only, the name-derived one.
- **A5 — Rename away:** removing the key from the name removes the chip; a declared tag with that key, if any, shows again.

**Postconditions:** name-derived tags are never stored and cannot be removed with the tool.
**Covers:** FR.24.1, FR.24.2, FR.24.3, FR.24.7.

---

## UC.TG.05 — A session removes a mistaken tag, and cannot remove a name-derived one

**Actors:** Session, Tessyn.
**Preconditions:** declared tags `windows-app` (repo) and `HSE-230`; the session name contains `HSE-150`.

**Main flow:**
1. The Session calls `tag_session` with `remove: ["https://github.com/tryhardstop/windows-app"]`.
2. Tessyn deletes the declared tag; result `Removed windows-app.` and `Tags: HSE-150, HSE-230.` (name-derived first within a kind).
3. SYSTEM entry `[Tags] removed windows-app`.

**Alternative flows:**
- **A1 — Name-derived key:** `remove: ["HSE-150"]` → `Not removed: 'HSE-150' - it comes from the session name; rename the session to drop it.` Nothing changes.
- **A2 — Not tagged:** `Not removed: 'owner/other' - not tagged.`
- **A3 — Garbage entry:** `Not removed: 'foo' - not a Linear issue, GitHub pull request or repository.`
- **A4 — Add and remove in one call:** processed in order, `add` first; the SYSTEM entry combines both: `[Tags] added HSE-230, windows-app PR #215; removed windows-app`.
- **A5 — Guidance:** finished work stays tagged; removal is only for tags added by mistake.

**Postconditions:** the declared tag is gone; the Tag row refreshes.
**Covers:** FR.24.2, FR.24.4, FR.24.7.

---

## UC.TG.06 — The User clicks a chip and the browser opens; a non-allowed URL is never opened

**Actors:** User, Tessyn.

**Main flow:**
1. The User hovers the `HSE-230` chip: the tooltip shows the URL on line 1 and `Tagged by the session <time>` (or `From the session name`) on line 2.
2. The User clicks the chip. Tessyn checks `SessionTagUrlPolicy.IsOpenable(url)`: an absolute `https` URL whose host is exactly `linear.app` or `github.com`.
3. The URL opens in the default OS browser (`Process.Start` with `UseShellExecute = true`).

**Alternative flows:**
- **A1 — Policy rejects** (`http://…`, `https://github.com.evil.io/…`, `https://evil.io/github.com`, `file:///…`, a relative path, null/empty): nothing opens; the attempt is logged and ignored.
- **A2 — Browser start fails:** the failure is logged; the app keeps running.

**Postconditions:** only linear.app and github.com pages are ever launched.
**Covers:** FR.24.7.

---

## UC.TG.07 — The Tag row: hidden when empty, wraps, shows in Session and Focus, ordered

**Actors:** User.

**Main flow:**
1. A session with no tags shows no Tag row and no empty space under the header.
2. After the first tag appears, a row of chips shows below the header DockPanel (name and location, search box, Session/Focus switch, MD, JSONL), inside the same Border, so the header's bottom rule is under the tags.
3. With many tags (e.g. 12) the chips wrap onto more lines and the header grows taller.
4. The row is the same in the Session view and the Focus view.
5. Order: Linear issues, then pull requests, then repositories; within a kind name-derived first, then declared newest first.

**Alternative flows:**
- **A1 — Last tag removed:** the row disappears again.
- **A2 — Switching sessions:** each session shows its own tags.

**Covers:** FR.24.1, FR.24.7.

---

## UC.TG.08 — Live refresh on tag and rename

**Actors:** Session, User.

**Main flow:**
1. With the session open, the Session calls `tag_session`. `SessionTagsChanged(nodeId)` reaches the view through `IDaemonClient` and the row refreshes on the UI thread without reopening the session.
2. The User renames the session; the row refreshes (name-derived chips change).
3. A Linear team learned in a different session (`NodeId = null` event) refreshes every open row.

**Alternative flows:**
- **A1 — Event for another node:** this session's row is not refreshed.
- **A2 — No-op call** (all "Already tagged"/"Not tagged"): no event.

**Covers:** FR.24.7, FR.24.8.

---

## UC.TG.09 — A long-running session gets the guidance once per app run

**Actors:** Session, Tessyn.

**Main flow:**
1. After an app restart, a long-running top-level session gets its next turn (any source). Tessyn calls `TakeTagGuidanceDue(nodeId)`, which returns true once per node per run.
2. The hidden instruction block includes `SessionTagGuidance`, after the attention guidance and before the Telegram guidance. If the session already has tags, ` Current tags: <label1>, <label2>.` is appended, in Tag-row order.
3. Later turns of the same app run do not carry it.

**Alternative flows:**
- **A1 — Worker Session or agent tools off:** not emitted, and the "due" flag is not consumed (a turn that can't show it doesn't use it up).
- **A2 — New session:** gets it on the first turn, like any session.
- **A3 — App restarted:** due again once.

**Covers:** FR.24.6.

---

## UC.TG.10 — A Worker Session tags itself

**Actors:** Worker Session, Tessyn.

**Main flow:**
1. A worker (`OrchestrationDepth > 0`) calls `tag_session` for the PR it is fixing.
2. Unlike `post_attention` (FR.23.6), the call is **not** refused: the tag is stored on the worker's own node and the usual result lines are returned.
3. The worker gets no tag guidance (FR.24.6); it tags only if it was told to.

**Postconditions:** the worker's node carries the tag; the supervisor's tags are unchanged.
**Covers:** FR.24.4, FR.24.6.

---

## UC.TG.11 — A session lists its tags and gets partial success

**Actors:** Session.

**Main flow:**
1. The Session calls `tag_session` with no arguments (or empty arrays): the result is the current list, ending in `Tags: <labels>.` or `Tags: none.`; no SYSTEM entry.
2. A call with `add: ["HSE-230", "foo", "owner/repo#5"]` tags the two valid entries and reports `foo`; the invalid entry does not stop the others.

**Alternative flows:**
- **A1 — Over the limit:** more than 30 entries in `add` or in `remove` is a tool error; nothing is changed.

**Covers:** FR.24.4.

---

## UC.TG.12 — Tags survive restarts and corruption

**Actors:** Tessyn.

**Main flow:**
1. Declared tags persist in `%AppData%\TessynDesktop\tags\<NodeId>.json` (temp file then rename); after a restart, compaction, detach or Cross-Profile Session Adoption the same NodeId reloads them.

**Alternative flows:**
- **A1 — Missing file:** no tags.
- **A2 — Corrupt file:** logged as a warning, moved to `<NodeId>.json.bad-<yyyyMMddHHmmss>`, tags start empty; never overwritten silently. The same rule applies to `linear-workspaces.json`.
- **A3 — Session deleted:** Tessyn never deletes tag files.

**Covers:** FR.24.5.

---

## UC.TG.13 — DEFERRED (not built in Phase 1): status colours

**Status:** not built in Phase 1. Described so the requirement is traceable; no test is runnable until Phase 2 is approved and the status source is decided.

**Actors:** User, Tessyn (scheduled pull).

**Main flow (future):**
1. A scheduler pulls the status of tagged Linear issues and GitHub PRs (e.g. every 10 minutes for tags of sessions active in the last N days, plus once when a session is opened).
2. Chips are coloured by status: Linear workflow state type (backlog, unstarted, started, completed, canceled); PR state (open, draft, merged, closed), possibly with review/check status.
3. The tooltip shows the status name and when it was last checked.

**Open decision (Phase 2):** Linear API key (DPAPI), Linear MCP via the External MCP Server Registry, or status reported by sessions through `tag_session`; GitHub via the `gh` CLI or a token.

**Covers:** FR.24.9.

---

## Out of scope (Phase 1)

- Detecting tags from conversation text.
- Adding or removing tags by hand in the UI.
- Status colours (UC.TG.13).
- Searching or filtering sessions by tags; multiple focus panels.
- TDCP methods.
- PR references without an owner (`#215`, `windows-app#215`).
