# UC.LP.01 — Left Panel and Session Lists

Related requirements: FR.1.18–FR.1.27, FR.9.10, FR.9.11, FR.1.24, FR.24.17 (see `docs/requirements/`). Related: [UC.RT.01](/docs/usecases/UC.RT.01-Recent-Tab.md), [UC.TG.01](/docs/usecases/UC.TG.01-Session-Tags.md). Test cases: [TC.LP.01](/docs/testcases/TC.LP.01-Left-Panel-Lists.md).

## Actors
- **User** (the Architect). **Tessyn** (WPF client and daemon).

## Preconditions (common)
- The tree holds directories, sessions with different last-activity times, supervisors with workers, and some empty groups.

---

## UC.LP.01 — Choose how the date shows
**Main flow:** 1. The User opens Settings, Appearance, *Date in the session rows*. 2. Two options show: *Date (yyyy-MM-dd)* and *Relative (for example 107w)*. 3. The User picks Relative. 4. Every row in the tree, the Recent tab, the Tags tab and the dropdown shows an age (`now`, `5m`, `3h`, `9d`, `107w`) and no date. 5. Within a minute the ages move with the clock.
**Alternative flows:** **A1** — Date chosen: only `yyyy-MM-dd` shows, never an age. **A2** — The text is always inside the row's tinted box, never beside it.
**Covers:** FR.1.18, FR.1.21.

## UC.LP.02 — Read a row at a glance
**Main flow:** 1. The User looks at the tree. 2. Each row shows two status squares, a Focus count pill, the name, pencil, clock, star; the second line shows the profile on the left and the date on the right. 3. A supervisor shows `104 workers` inside the box. 4. Green tiers show how recent the activity was (15 min, 1 h, 4 h, 12 h) and a dotted border marks the last 24 hours.
**Alternative flows:** **A1** — A session with unsent text and no recent activity has the palest tier. **A2** — A pinned profile with a queued switch shows `a -> b`.
**Covers:** FR.1.20–FR.1.22, FR.9.10, FR.11.14.

## UC.LP.03 — Same row in the Recent tab and the dropdown
**Main flow:** 1. The User opens the Recent tab: each group header names the project, each row is one full-width button with the recency colour reaching both edges, no project name in the row, the favourite/Hide icon over the right end. 2. The User opens the Recent dropdown in the title bar: rows are one line; the project name follows the session name. 3. The dropdown button stays flat and rows keep their recency colours while it is open and hovered.
**Covers:** FR.1.20, FR.9.11, FR.1.16.

## UC.LP.04 — Tabs and no dead space
**Main flow:** 1. The User widens and narrows the left panel. 2. Tree, Recent and Tags headers stay equal and fill 100%. 3. No empty band shows under the search box in Tree; in Tags with no tag selected the chips take the height.
**Covers:** FR.1.19, FR.1.23.

## UC.LP.05 — Find tags
**Main flow:** 1. The User opens the Tags tab with a session of directory X selected and types `HSE-*`. 2. The query reads `HSE-`; tags of X match. 3. Nothing matches: the text says "No tag matches in this directory…". 4. The User presses **Show all directories**; all 87 HSE tags show.
**Alternative flows:** **A1** — A directory, a group or nothing is selected: every tag is searched at once and no scope line shows.
**Covers:** FR.1.24, FR.24.17.

## UC.LP.06 — Rename, delete and switch profile from the header
**Main flow:** 1. The User double-clicks the session title; a box with the name fills the title area. 2. Enter renames; Esc cancels. 3. The Delete icon left of the title asks for confirmation. 4. *Switch profile…* and *Send later* are in the header; their dialogs have full-width buttons.
**Covers:** FR.1.25, FR.1.27.

## UC.LP.07 — Empty orchestration groups are hidden
**Main flow:** 1. A supervisor spawned workers into nine groups; the workers nest under the supervisor. 2. The groups are flagged `holdsOnlyWorkers` and are not drawn. 3. A group the User created and left empty stays visible after a restart. 4. A blue square shows for resumable sessions after the first sweep.
**Covers:** FR.1.26, FR.1.22.
