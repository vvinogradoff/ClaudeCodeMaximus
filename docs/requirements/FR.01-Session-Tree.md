# FR.1 — Session Tree and Left Panel (batch 2026-11-17)

> **Staging file, phase 1.** This holds only the new and amended text of the 2026-11-17 batch for FR.1, in its target place. Phase 2 puts the existing FR.1 text (`requirements.md` lines 23–107, plus the FR.1.17 text of the uncommitted draft) above this block and keeps every existing ID. Use cases: [UC.LP.01](/docs/usecases/UC.LP.01-Left-Panel-Lists.md). Test cases: [TC.LP.01](/docs/testcases/TC.LP.01-Left-Panel-Lists.md).
> Clients: the rules are client-neutral unless a line says WPF. Avalonia is the visual reference (decision of the Architect); where WPF differed, WPF was changed.

New IDs start at **FR.1.18** (FR.1.17 is taken by the draft, stable ids for Directory and Group nodes).

## Amendments to existing requirements

| Existing | Amended by | Change |
|---|---|---|
| FR.1.15 Left panel tabs | FR.1.19 | The three tab headers are equal in width and together fill 100% of the panel. |
| FR.1.16 Recent tab | FR.1.20 | Rows are as the tree's rows, full width; no project name in a row. |
| FR.9.5 Recency bars | FR.9.10 | Four tiers (15 min, 1 h, 4 h, 12 h) plus the 24-hour dotted border. Replaces the three tiers 15/30/60 min. |
| FR.24.11 Tags tab: search | FR.1.24, FR.24.17 | Tag search scope and wildcard rules, and what the tab says when nothing matches. |

## New requirements

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
