# UC.RT.01 — Recent Tab

Related requirements: FR.1.15, FR.1.16 in [requirements.md](/docs/requirements.md). Related: FR.1.3 (Auto-derived label), FR.9.5 (recency background), FR.9.7 (favorite star), FR.9.8 (Last Activity), FR.15 (Worker Sessions), [UC.TG.01](/docs/usecases/UC.TG.01-Session-Tags.md) (the Tags tab reuses the grouped list, UC.TG.22). Test cases: [TC.RT.01](/docs/testcases/TC.RT.01-Recent-Tab.md).

## Actors

- **User** (the Architect) — switches tabs, reads the list, selects sessions.
- **Tessyn** — builds the list and applies the collapse rule.

## Preconditions (common)

- The tree holds Directory nodes (some in Groups) with top-level sessions; some sessions have Worker Sessions nested under them.
- Terms: Recent tab, Directory node, Worker Session, Last Activity (see the glossary).

---

## UC.RT.01 — The Recent tab lists every top-level session, grouped and ordered

**Actors:** User, Tessyn.

**Main flow:**
1. The User opens the **Recent** tab (second tab, after Tree).
2. Sessions are grouped under a header per Directory node: a 📂 icon and the Auto-derived label (FR.1.3), with directories in tree order.
3. Within a group, sessions with unsent draft text come first, then the rest, each newest first by Last Activity.
4. Each row shows status dots, name, the draft ✏ indicator, the favorite star, the profile name, the last prompt time, the recency background and the dotted border for recently active sessions.

**Alternative flows:**
- **A1 — Worker Sessions:** not listed; they stay nested in the tree.
- **A2 — Rebuild:** the list is rebuilt each time the tab is selected, so new sessions, drafts and activity show.
- **A3 — Sessions in Groups** are listed under their Directory node.

**Covers:** FR.1.16.

---

## UC.RT.02 — Clicking a row selects the session, including a repeat click

**Actors:** User.

**Main flow:**
1. The User clicks a row in the Recent tab. The session is selected in the tree and opens.
2. The User selects another session in the tree, returns to the Recent tab and clicks the same row again. The session is selected again.

**Alternative flows:**
- **A1 — Repeat click without leaving:** clicking the row that was last clicked in the list, after the selection moved elsewhere, still selects it (no swallowed click).

**Covers:** FR.1.16.

---

## UC.RT.03 — Switching to Recent collapses every directory except the selected session's

**Actors:** User, Tessyn.
**Preconditions:** a session S in directory X is selected; directories Y and Z exist.

**Main flow:**
1. The User switches to the Recent tab.
2. Group X is expanded (chevron ▾); Y and Z are collapsed (chevron ▸).
3. The User switches to Tree and back: the rule applies again (each switch).

**Alternative flows:**
- **A1 — Another session selected in the tree meanwhile:** the next switch expands that session's directory and collapses the rest.
- **A2 — Directory names compare case-insensitively** (the same path in another case is the same directory).

**Covers:** FR.1.16.

---

## UC.RT.04 — With no session selected, every group is expanded

**Actors:** User.
**Preconditions:** no session selected (e.g. fresh start).

**Main flow:**
1. The User switches to the Recent tab.
2. Every group is expanded.

**Covers:** FR.1.16.

---

## UC.RT.05 — Manual expand and collapse hold until the next switch

**Actors:** User.

**Main flow:**
1. On the Recent tab the User clicks a directory header: the group toggles (chevron ▾/▸).
2. The User expands Y and collapses X by hand. The state stays while the User stays on the tab (including while selecting sessions from it).
3. The User switches to Tree and back. The collapse rule re-applies (UC.RT.03), overriding the manual state.

**Covers:** FR.1.16.

---

## UC.RT.06 — A Worker Session is selected in the tree

**Actors:** User.
**Preconditions:** Worker Session W nested under supervisor S (possibly several levels deep, possibly under a Group); the tree's selection is W.

**Main flow:**
1. The User switches to the Recent tab.
2. The group of the Directory node containing W (the one W is nested under) is expanded; every other group is collapsed.
3. W is not listed; its supervisor S is, in that group.

**Covers:** FR.1.16.

---

## UC.RT.07 — Three tabs; picking from the top-bar dropdown returns to Tree

**Actors:** User.

**Main flow:**
1. The left panel has three tabs in order: Tree, Recent, Tags.
2. The app starts on Tree; the selected tab is not persisted.
3. The User picks a session in the top-bar recent-sessions dropdown: the panel switches to the Tree tab and selects it.

**Covers:** FR.1.15.

---

## Out of scope

- Persisting the selected tab or the collapsed state of groups.
- Reordering, pinning or filtering sessions in the Recent tab (the Tags tab filters by tag, UC.TG.14).


## UC.RT.08 — A Focus reply draft makes a session recent
**Actor:** the User. **Precondition:** a session last active two days ago has an open Ask item.
**Main flow:** 1. The User types an answer into the item's reply box without sending it. 2. The session's row in the tree gets the 15-minute recency colour, and the session appears at the top of the Recent tab and the Recent dropdown. 3. The draft pencil does not appear (it is for the composer draft).
**Alternative flows:** **A1** — The User deletes all the text: the reply draft no longer counts, and the recency falls back to the other activity. **A2** — The answer is typed on the web: the same on the desktop. **A3** — The item is closed: its draft is deleted and no longer counts.
**Covers:** FR.9.20.
